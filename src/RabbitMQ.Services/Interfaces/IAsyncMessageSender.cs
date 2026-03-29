namespace RabbitMQ.Services.Interfaces
{
    public interface IAsyncMessageSender
    {
        Task SendMessageAsync<T>(string uri, T message, bool bindQueue = true) where T : BaseMessage;

        Task SendMessageAsync(string uri, string contentType, byte[] body, bool bindQueue);
    }
}
