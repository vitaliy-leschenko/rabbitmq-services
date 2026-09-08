using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;

namespace RabbitMQ.Services.HostedServices
{
    public class ConsumerHostedService<T>(
        IAsyncMessageConsumer<T> consumer,
        IOptions<ConsumerConfiguration<T>> options,
        IUriMasker uriMasker,
        TimeProvider timeProvider,
        ILogger<ConsumerHostedService<T>> logger) : IHostedService, IAsyncDisposable where T : class
    {
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Backstop for a start attempt stuck in a call that ignores cancellation. Longer than the
        /// connection builder's own connect timeout plus the channel setup, so it only fires when
        /// everything else has failed to bound the attempt.
        /// </summary>
        internal static readonly TimeSpan StartAttemptTimeout = TimeSpan.FromSeconds(90);

        /// <summary>
        /// Bound for stopping the consumer on the reconnect path: covers the client's continuation
        /// timeout for the channel close and a message handler still in flight.
        /// </summary>
        internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// How often the supervisor checks the consumer while no loss has been reported, so that a
        /// missed event still gets the consumer restarted.
        /// </summary>
        internal static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);

        private readonly IAsyncMessageConsumer<T> consumer = consumer;
        private readonly IOptions<ConsumerConfiguration<T>> options = options;
        private readonly IUriMasker uriMasker = uriMasker;
        private readonly TimeProvider timeProvider = timeProvider;
        private readonly ILogger<ConsumerHostedService<T>> logger = logger;

        private volatile TaskCompletionSource connectionLostSignal = NewSignal();
        private volatile bool started;

        private CancellationTokenSource? executingCancellationTokenSource;
        private int connectionLost;

        public bool Started => started;

        public Task? ExecutingTask { get; private set; }

        public async ValueTask DisposeAsync()
        {
            executingCancellationTokenSource?.Cancel();
            await consumer.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        public Task StartAsync(CancellationToken token)
        {
            // Store the task we're executing
            executingCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            ExecutingTask = ExecuteAsync(executingCancellationTokenSource.Token);

            // Otherwise it's running
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token)
        {
            // Stop called without start
            if (ExecutingTask == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                executingCancellationTokenSource!.Cancel();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(ExecutingTask, Task.Delay(Timeout.Infinite, token));
            }

            await consumer.StopAsync(token);
            started = false;
        }

        /// <summary>
        /// Supervises the consumer for the whole lifetime of the application: connects it with
        /// retries, then waits for the broker connection to drop and connects it again. The consumer
        /// itself does not recover, so this loop is the only place where reconnection happens.
        /// </summary>
        protected virtual async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Yield();
            var url = uriMasker.Mask(options.Value.Url);
            logger.LogInformation("Starting {name} message consumer {url}", typeof(T).Name, url);

            consumer.ConnectionLost += OnConnectionLost;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Arm the signal before connecting, so that a connection lost right after a
                    // successful start is not missed while we are not waiting on it yet.
                    var signal = NewSignal();
                    connectionLostSignal = signal;
                    Interlocked.Exchange(ref connectionLost, 0);

                    await StartConsumerAsync(url, token);

                    await WaitForConnectionLossAsync(signal.Task, url, token);

                    started = false;
                    logger.LogWarning("{name} message consumer {url} lost the connection, reconnecting",
                        typeof(T).Name, url);

                    await StopConsumerAsync(url, token);
                }

                // The loop only ends when the application is shutting down. Throwing here keeps the
                // task cancelled no matter where the cancellation was noticed.
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                consumer.ConnectionLost -= OnConnectionLost;
            }
        }

        private static TaskCompletionSource NewSignal() =>
            // Continuations run on the pool, not on the client's event dispatcher that signals us.
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task StartConsumerAsync(string url, CancellationToken token)
        {
            started = false;
            do
            {
                try
                {
                    // Each attempt gets its own deadline: a start stuck in a call that ignores
                    // cancellation is abandoned and retried instead of hanging the supervisor for good.
                    using var attemptTimeout = new CancellationTokenSource(StartAttemptTimeout, timeProvider);
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token, attemptTimeout.Token);

                    await consumer.StartAsync(attempt.Token);
                    started = true;
                    logger.LogInformation("{name} message consumer {url} has been started", typeof(T).Name, url);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Application shutdown, not a failed attempt.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Can't start {name} message consumer {url} with error: {message}",
                        typeof(T).Name, url, ex.Message);

                    await Task.Delay(RetryDelay, timeProvider, token);
                }
            }
            while (!started);
        }

        private async Task WaitForConnectionLossAsync(Task signal, string url, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    await signal.WaitAsync(WatchdogInterval, timeProvider, token);
                    return;
                }
                catch (TimeoutException)
                {
                    // No event within the interval: make sure the consumer is really still connected.
                    if (!consumer.IsConnected)
                    {
                        logger.LogWarning("{name} message consumer {url} is disconnected but reported no loss",
                            typeof(T).Name, url);
                        return;
                    }
                }
            }
        }

        private async Task StopConsumerAsync(string url, CancellationToken token)
        {
            using var stopTimeout = new CancellationTokenSource(StopTimeout, timeProvider);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, stopTimeout.Token);
            try
            {
                await consumer.StopAsync(stop.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A channel that will not close is abandoned; the next start builds a new one.
                logger.LogWarning(ex, "Stopping {name} message consumer {url} failed: {message}",
                    typeof(T).Name, url, ex.Message);
            }
        }

        private void OnConnectionLost(object? sender, EventArgs e)
        {
            // Called on the RabbitMQ client's event dispatcher: release the loop and return at once.
            if (Interlocked.Exchange(ref connectionLost, 1) == 0)
            {
                connectionLostSignal.TrySetResult();
            }
        }
    }
}
