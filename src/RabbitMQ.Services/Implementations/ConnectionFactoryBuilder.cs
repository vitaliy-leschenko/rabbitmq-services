using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;

namespace RabbitMQ.Services.Implementations
{
    public sealed class ConnectionFactoryBuilder : IConnectionFactoryBuilder
    {
        public IConnectionFactory CreateConnectionFactory(IRabbitMQEndpoint endpoint) =>
            new ConnectionFactory()
            {
                HostName = endpoint.Host,
                Port = endpoint.Port,
                UserName = endpoint.UserName,
                Password = endpoint.Password,
                VirtualHost = endpoint.VirtualHost,
                AutomaticRecoveryEnabled = false, // we will use own recovery behaviour
                ConsumerDispatchConcurrency = 8,
                RequestedHeartbeat = endpoint.Heartbeat ?? ConnectionFactory.DefaultHeartbeat
            };

        public string GetFactoryHash(IRabbitMQEndpoint endpoint, ConnectionMode mode)
        {
            var port = endpoint.Port == -1 ? Protocols.DefaultProtocol.DefaultPort : endpoint.Port;
            return $"{endpoint.Host}|{port}|{endpoint.UserName}|{endpoint.Password}|{endpoint.VirtualHost}";
        }
    }
}
