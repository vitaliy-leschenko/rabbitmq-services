namespace RabbitMQ.Services.Interfaces
{
    public interface IAsyncMessageConsumer<T> : IAsyncDisposable
    {
        /// <summary>
        /// Raised when the broker connection is shut down unexpectedly. Recovery is left to the
        /// subscriber: the handler runs on the RabbitMQ client's event dispatcher and must not block.
        /// </summary>
        event EventHandler? ConnectionLost;

        Task StartAsync();

        Task StopAsync();
    }
}
