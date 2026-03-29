namespace RabbitMQ.Services.Interfaces
{
    public interface IAsyncMessageConsumer<T> : IAsyncDisposable
    {
        Task StartAsync();

        Task StopAsync();
    }
}
