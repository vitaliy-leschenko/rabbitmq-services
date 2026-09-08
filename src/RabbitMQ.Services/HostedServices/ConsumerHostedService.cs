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
        ILogger<ConsumerHostedService<T>> logger) : IHostedService, IAsyncDisposable where T : class
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        private readonly IAsyncMessageConsumer<T> consumer = consumer;
        private readonly IOptions<ConsumerConfiguration<T>> options = options;
        private readonly IUriMasker uriMasker = uriMasker;
        private readonly ILogger<ConsumerHostedService<T>> logger = logger;

        private readonly SemaphoreSlim connectionLostSignal = new(0);

        private CancellationTokenSource? executingCancellationTokenSource;
        private int connectionLost;

        public bool Started { get; private set; }

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

            await consumer.StopAsync();
            Started = false;
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
                    // Reset before connecting, so that a connection lost right after a successful
                    // start is not missed while we are not waiting on the signal yet.
                    Interlocked.Exchange(ref connectionLost, 0);

                    await StartConsumerAsync(url, token);

                    await connectionLostSignal.WaitAsync(token);

                    Started = false;
                    logger.LogWarning("{name} message consumer {url} lost the connection, reconnecting",
                        typeof(T).Name, url);

                    await consumer.StopAsync();
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

        private async Task StartConsumerAsync(string url, CancellationToken token)
        {
            Started = false;
            do
            {
                try
                {
                    await consumer.StartAsync();
                    Started = true;
                    logger.LogInformation("{name} message consumer {url} has been started", typeof(T).Name, url);
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Can't start {name} message consumer {url} with error: {message}",
                        typeof(T).Name, url, ex.Message);

                    await Task.Delay(RetryDelay, token);
                }
            }
            while (!Started);
        }

        private void OnConnectionLost(object? sender, EventArgs e)
        {
            // Called on the RabbitMQ client's event dispatcher: release the loop and return at once.
            if (Interlocked.Exchange(ref connectionLost, 1) == 0)
            {
                connectionLostSignal.Release();
            }
        }
    }
}
