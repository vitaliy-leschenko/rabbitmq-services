namespace RabbitMQ.Services.Interfaces
{
    public interface IMessageDeliveryService
    {
        Task SendMessagesAsync();
    }
}
