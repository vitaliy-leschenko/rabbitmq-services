using System.Web;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;
using static RabbitMQ.Services.Implementations.RabbitMQEndpointParserHelpers;

namespace RabbitMQ.Services.Implementations
{
    public sealed class RabbitMQEndpointParser(IDnsResolver dnsResolver) : IRabbitMQEndpointParser
    {
        private readonly IDnsResolver dnsResolver = dnsResolver;

        public IRabbitMQEndpoint Parse(string data)
        {
            var uri = new Uri(data);

            if (string.Equals("amqps", uri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("AMQP over TLS is not supported");
            }

            if (!string.Equals("rabbitmq", uri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals("amqp", uri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The invalid scheme was specified: " + uri.Scheme);
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var (name, vhost) = segments.Length switch
            {
                2 => (segments[1], segments[0].ToLower()),
                1 => (segments[0], "/"),
                _ => throw new InvalidOperationException("Invalid uri"),
            };

            var query = HttpUtility.ParseQueryString(uri.Query);

            var (userName, password) = GetUserInfo(uri);

            var isTemporary = IsTemporaryQueue(query);
            var isHighAvailable = IsHighAvailableQueue(query);
            if (isHighAvailable && isTemporary)
            {
                throw new InvalidOperationException("A highly available queue cannot be temporary");
            }

            var host = uri.Host;
            var port = uri.IsDefaultPort ? Protocols.DefaultProtocol.DefaultPort : uri.Port;

            var endpoint = new RabbitMQEndpoint
            {
                Uri = data,
                Host = host,
                Port = port,
                VirtualHost = vhost,
                UserName = userName,
                Password = password,
                Heartbeat = GetHeartbeat(query),
                PrefetchCount = GetPrefetchCount(query),
                ConsumersCount = GetConsumersCount(query),
                Queue =
                {
                    Name = name,
                    Routing = GetRouting(query),
                    Durable = IsDurableQueue(query, isTemporary),
                    Exclusive = IsExclusiveQueue(query, isTemporary),
                    AutoDelete = IsAutoDeleteQueue(query, isTemporary),
                    IsTransactional = IsTransactionalQueue(query),
                },
                Exchange =
                {
                    Name = GetExchangeName(name, query),
                    Type = GetExchangeType(query)
                },
                AmqpTcpEndpoints = GetAmqpTcpEndpoints(dnsResolver, query, host, port),
            };

            AddArguments(endpoint, query, isHighAvailable);

            return endpoint;
        }
    }
}
