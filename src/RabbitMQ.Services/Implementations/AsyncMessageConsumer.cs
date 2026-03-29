using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;

namespace RabbitMQ.Services.Implementations
{
    public sealed class AsyncMessageConsumer<T>(
        IRabbitMQEndpointParser endpointParser,
        IConnectionBuilder builder,
        IMessageHandler<T> processor,
        IOptions<ConsumerConfiguration<T>> options,
        ILogger<T> logger) : IAsyncMessageConsumer<T> where T : class
    {
        private readonly IOptions<ConsumerConfiguration<T>> options = options;
        private readonly ILogger<T> logger = logger;
        private readonly IRabbitMQEndpointParser endpointParser = endpointParser;
        private readonly IConnectionBuilder builder = builder;
        private readonly List<AdvancedConsumer<T>> consumers = [];
        private readonly IMessageHandler<T> handler = processor;

        private IChannel? channel = null;
        private bool stopping = false;

        public async Task StartAsync()
        {
            stopping = false;

            var endpoint = endpointParser.Parse(options.Value.Url);
            var connection = await GetConnectionAsync(endpoint);

            channel = await SetupChannelAsync(endpoint, connection);

            connection.ConnectionShutdownAsync += OnConnectionShutdownAsync;
        }

        public async Task StopAsync()
        {
            stopping = true;
            if (channel != null)
            {
                await Task.CompletedTask;

                channel.Dispose();
                channel = null;
            }

            consumers.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            await StopAsync();
        }

        private async Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs e)
        {
            if (stopping)
            {
                return;
            }

            logger.LogWarning("ConnectionShutdown: {error}", e.ToString());
            await StopAsync();
            await StartAsync();
        }

        private async Task<IChannel> SetupChannelAsync(IRabbitMQEndpoint endpoint, IConnection connection)
        {
            var channel = await connection.CreateChannelAsync();
            await channel.BasicQosAsync(0, endpoint.PrefetchCount, false);

            string queue = await channel.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                endpoint.Queue.Arguments.ToDictionary());

            if (options.Value.BindQueue)
            {
                await channel.ExchangeDeclareAsync(
                    endpoint.Exchange.Name,
                    endpoint.Exchange.Type,
                    endpoint.Exchange.Durable,
                    endpoint.Exchange.AutoDelete,
                    endpoint.Exchange.Arguments.ToDictionary());

                await channel.QueueBindAsync(queue, endpoint.Exchange.Name, endpoint.Queue.Routing);
            }

            for (var t = 0; t < endpoint.ConsumersCount; t++)
            {
                var consumer = new AdvancedConsumer<T>(handler, endpoint, options, channel, logger);
                await channel.BasicConsumeAsync(endpoint.Queue.Name, false, consumer);
                consumers.Add(consumer);
            }

            return channel;
        }

        private async Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint)
        {
            try
            {
                return await builder.GetConnectionAsync(endpoint, options.Value.ConnectionName, ConnectionMode.Consumer, 0);
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogError(ex, "Can't connect to {url}", options.Value.Url);
                throw;
            }
        }
    }
}
