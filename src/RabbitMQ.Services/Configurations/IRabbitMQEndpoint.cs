using RabbitMQ.Client;

namespace RabbitMQ.Services.Configurations
{
    public interface IRabbitMQEndpoint
    {
        string Uri { get; }

        string Host { get; }

        int Port { get; }

        string UserName { get; }

        string Password { get; }

        string VirtualHost { get; }

        ushort PrefetchCount { get; }

        int ConsumersCount { get; }

        TimeSpan? Heartbeat { get; }

        IList<AmqpTcpEndpoint> AmqpTcpEndpoints { get; }

        IExchangeOptions Exchange { get; }

        IQueueOptions Queue { get; }
    }
}
