using System.Diagnostics;

namespace RabbitMQ.Services.Interfaces
{
    public interface IActivityBuilder
    {
        Activity StartNewChildActivity<T>(BaseMessage message) where T : class;
    }
}
