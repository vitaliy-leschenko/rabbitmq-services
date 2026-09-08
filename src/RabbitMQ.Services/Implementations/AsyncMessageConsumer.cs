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
        IUriMasker uriMasker,
        ILogger<T> logger) : IAsyncMessageConsumer<T> where T : class
    {
        private readonly IOptions<ConsumerConfiguration<T>> options = options;
        private readonly ILogger<T> logger = logger;
        private readonly IUriMasker uriMasker = uriMasker;
        private readonly IRabbitMQEndpointParser endpointParser = endpointParser;
        private readonly IConnectionBuilder builder = builder;
        private readonly List<AdvancedConsumer<T>> consumers = [];
        private readonly IMessageHandler<T> handler = processor;

        // Start and stop never overlap: after an abandoned (timed out) start the supervisor calls
        // StartAsync again on the same instance while the old attempt may still be unwinding.
        private readonly SemaphoreSlim gate = new(1, 1);

        private IChannel? channel = null;
        private IConnection? connection = null;
        private volatile bool stopping = false;

        public event EventHandler? ConnectionLost;

        public bool IsConnected
        {
            get
            {
                var currentConnection = connection;
                var currentChannel = channel;
                return currentConnection is { IsOpen: true } && currentChannel is { IsOpen: true };
            }
        }

        public async Task StartAsync(CancellationToken token = default)
        {
            await gate.WaitAsync(token);
            try
            {
                stopping = false;

                var endpoint = endpointParser.Parse(options.Value.Url);
                var current = await GetConnectionAsync(endpoint, token);

                var created = await SetupChannelAsync(endpoint, current, token);

                if (token.IsCancellationRequested)
                {
                    // The supervisor has already given up on this attempt: do not publish a channel
                    // nobody will stop.
                    consumers.Clear();
                    await CloseChannelAsync(created, CancellationToken.None);
                    token.ThrowIfCancellationRequested();
                }

                channel = created;
                connection = current;
                created.ChannelShutdownAsync += OnChannelShutdownAsync;
                current.ConnectionShutdownAsync += OnConnectionShutdownAsync;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task StopAsync(CancellationToken token = default)
        {
            stopping = true;

            await gate.WaitAsync(token);
            try
            {
                if (connection is IConnection current)
                {
                    current.ConnectionShutdownAsync -= OnConnectionShutdownAsync;
                    connection = null;
                }

                if (channel is IChannel open)
                {
                    open.ChannelShutdownAsync -= OnChannelShutdownAsync;
                    channel = null;

                    await CloseChannelAsync(open, token);
                }

                consumers.Clear();
            }
            finally
            {
                gate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            await StopAsync();
        }

        private Task OnConnectionShutdownAsync(object sender, ShutdownEventArgs e)
        {
            if (stopping)
            {
                return Task.CompletedTask;
            }

            logger.LogWarning("ConnectionShutdown: {error}", e.ToString());

            // Reconnecting here would run the retries on the client's event dispatcher. Report the
            // loss instead and let ConsumerHostedService restart us with its own retry loop.
            ConnectionLost?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        }

        private Task OnChannelShutdownAsync(object sender, ShutdownEventArgs e)
        {
            if (stopping)
            {
                return Task.CompletedTask;
            }

            // The broker closes a channel on its own for a protocol error (406, 404): the connection
            // stays open, so without this the consumer would be dead with nobody knowing.
            logger.LogWarning("ChannelShutdown: {error}", e.ToString());

            ConnectionLost?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        }

        private async Task<IChannel> SetupChannelAsync(IRabbitMQEndpoint endpoint, IConnection connection, CancellationToken token)
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: token);
            try
            {
                await channel.BasicQosAsync(0, endpoint.PrefetchCount, false, token);

                string queue = await channel.QueueDeclareAsync(
                    endpoint.Queue.Name,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    endpoint.Queue.Arguments.ToDictionary(),
                    cancellationToken: token);

                if (options.Value.BindQueue)
                {
                    await channel.ExchangeDeclareAsync(
                        endpoint.Exchange.Name,
                        endpoint.Exchange.Type,
                        endpoint.Exchange.Durable,
                        endpoint.Exchange.AutoDelete,
                        endpoint.Exchange.Arguments.ToDictionary(),
                        cancellationToken: token);

                    await channel.QueueBindAsync(queue, endpoint.Exchange.Name, endpoint.Queue.Routing, cancellationToken: token);
                }

                for (var t = 0; t < endpoint.ConsumersCount; t++)
                {
                    var consumer = new AdvancedConsumer<T>(handler, endpoint, options, channel, logger);
                    await channel.BasicConsumeAsync(endpoint.Queue.Name, false, consumer, token);
                    consumers.Add(consumer);
                }

                return channel;
            }
            catch
            {
                // Without this a failed setup leaks a channel on every retry.
                consumers.Clear();
                await CloseChannelAsync(channel, CancellationToken.None);
                throw;
            }
        }

        private async Task CloseChannelAsync(IChannel channel, CancellationToken token)
        {
            try
            {
                if (channel.IsOpen)
                {
                    // Abort rather than close: the channel's Dispose would do the same synchronously
                    // and could wait on a broker that is already gone.
                    await channel.CloseAsync(Constants.ReplySuccess, "Consumer stopped", true, token);
                }

                await channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Closing the channel of {url} failed: {message}",
                    uriMasker.Mask(options.Value.Url), ex.Message);
            }
        }

        private async Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint, CancellationToken token)
        {
            try
            {
                return await builder.GetConnectionAsync(endpoint, options.Value.ConnectionName, ConnectionMode.Consumer, 0, token);
            }
            catch (BrokerUnreachableException ex)
            {
                logger.LogError(ex, "Can't connect to {url}", uriMasker.Mask(options.Value.Url));
                throw;
            }
        }
    }
}
