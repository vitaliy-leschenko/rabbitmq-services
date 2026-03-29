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
        ILogger<ConsumerHostedService<T>> logger) : IHostedService, IAsyncDisposable where T : class
    {
        private readonly IAsyncMessageConsumer<T> consumer = consumer;
        private readonly IOptions<ConsumerConfiguration<T>> options = options;
        private readonly ILogger<ConsumerHostedService<T>> logger = logger;

        private CancellationTokenSource? executingCancellationTokenSource;

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

        protected virtual async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Yield();
            logger.LogInformation("Starting {name} message consumer {url}", typeof(T).Name, options.Value.Url);

            Started = false;
            do
            {
                try
                {
                    await consumer.StartAsync();
                    Started = true;
                    logger.LogInformation("{name} message consumer {url} has been started", typeof(T).Name, options.Value.Url);
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Can't start {name} message consumer {url} with error: {message}",
                        typeof(T).Name, options.Value.Url, ex.Message);

                    await Task.Delay(1000, token);
                }
            }
            while (!Started);
        }
    }
}
