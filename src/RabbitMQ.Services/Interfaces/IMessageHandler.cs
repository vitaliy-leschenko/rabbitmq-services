namespace RabbitMQ.Services.Interfaces
{
    public interface IMessageHandler<T> where T : class
    {
        T GetMessage(string data);

        Task HandleAsync(T message, int retryAttempt, int delayAttempt, CancellationToken token);
    }
}
