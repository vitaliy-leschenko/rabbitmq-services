using RabbitMQ.Client;

namespace RabbitMQ.Services.Configurations
{
    internal sealed class RabbitMQEndpoint : IRabbitMQEndpoint
    {
        public string Uri { get; set; } = string.Empty;

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = -1;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string VirtualHost { get; set; } = string.Empty;

        public ushort PrefetchCount { get; set; } = 1;

        public int ConsumersCount { get; set; } = 1;

        public TimeSpan? Heartbeat { get; set; }

        public List<AmqpTcpEndpoint> AmqpTcpEndpoints { get; set; } = [];

        public ExchangeOptions Exchange { get; } = new ExchangeOptions();

        public QueueOptions Queue { get; } = new QueueOptions();

        IQueueOptions IRabbitMQEndpoint.Queue => Queue;

        IExchangeOptions IRabbitMQEndpoint.Exchange => Exchange;

        IList<AmqpTcpEndpoint> IRabbitMQEndpoint.AmqpTcpEndpoints => AmqpTcpEndpoints;
    }
}
