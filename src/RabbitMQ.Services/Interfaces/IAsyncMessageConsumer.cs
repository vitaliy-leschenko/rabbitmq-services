namespace RabbitMQ.Services.Interfaces
{
    public interface IAsyncMessageConsumer<T> : IAsyncDisposable
    {
        /// <summary>
        /// Raised when the broker connection or the channel is shut down unexpectedly. Recovery is
        /// left to the subscriber: the handler runs on the RabbitMQ client's event dispatcher and
        /// must not block.
        /// </summary>
        event EventHandler? ConnectionLost;

        /// <summary>
        /// Snapshot of whether both the broker connection and the channel are open. False before
        /// the first start and after a stop. Lets a supervisor notice a loss whose event was missed.
        /// </summary>
        bool IsConnected { get; }

        Task StartAsync(CancellationToken token = default);

        Task StopAsync(CancellationToken token = default);
    }
}
