using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;

namespace RabbitMQ.Services.Interfaces
{
    public interface IConnectionFactoryBuilder
    {
        IConnectionFactory CreateConnectionFactory(IRabbitMQEndpoint endpoint);

        string GetFactoryHash(IRabbitMQEndpoint endpoint, ConnectionMode mode);
    }
}
