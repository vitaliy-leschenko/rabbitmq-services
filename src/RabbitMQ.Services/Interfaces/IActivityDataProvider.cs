namespace RabbitMQ.Services.Interfaces
{
    public interface IActivityDataProvider
    {
        void SetActivityData(BaseMessage message);
    }
}
