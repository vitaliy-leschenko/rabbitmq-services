using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;

namespace RabbitMQ.Services.Interfaces
{
    public interface IConnectionBuilder
    {
        Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint, string connectionName, ConnectionMode mode, int attempts = 5);
    }
}
