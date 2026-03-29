using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Exceptions;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Text;

namespace RabbitMQ.Services.Implementations
{
    public sealed class AdvancedConsumer<T> : AsyncEventingBasicConsumer where T : class
    {
        private const string RetryingCountHeaderKey = "retrying-count";
        private const string DelaysCountHeaderKey = "delays-count";

        private readonly IMessageHandler<T> handler;
        private readonly IRabbitMQEndpoint endpoint;
        private readonly IOptions<ConsumerConfiguration<T>> options;
        private readonly ILogger logger;

        private CancellationTokenSource cancellationTokenSource = new();

        public AdvancedConsumer(
            IMessageHandler<T> handler,
            IRabbitMQEndpoint endpoint,
            IOptions<ConsumerConfiguration<T>> options,
            IChannel model,
            ILogger logger)
            : base(model)
        {
            this.logger = logger;
            this.handler = handler;
            this.endpoint = endpoint;
            this.options = options;

            ReceivedAsync += OnMessageReceived;
            UnregisteredAsync += OnUnregistered;
        }

        public async Task CancelAsync()
        {
            logger.LogInformation("Canceling consumer.");
            foreach (var tag in ConsumerTags)
            {
                await Channel.BasicCancelAsync(tag);
            }
        }

        private Task OnUnregistered(object sender, ConsumerEventArgs e)
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource = new();
            return Task.CompletedTask;
        }

        private async Task OnMessageReceived(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                var token = cancellationTokenSource.Token;
                await OnMessageReceived(e, token);
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Something went wrong.");
            }
        }

        private async Task OnMessageReceived(BasicDeliverEventArgs e, CancellationToken token)
        {
            var channel = Channel;

            var properties = new BasicProperties(e.BasicProperties);
            var headers = GetOrCreateHeaders(properties);

            try
            {
                var body = Encoding.UTF8.GetString(e.Body.ToArray());
                T message;

                try
                {
                    message = handler.GetMessage(body);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Can't parse message.");
                    AddErrorHeaders(ex, headers);
                    headers[RetryingCountHeaderKey] = 0;
                    headers[DelaysCountHeaderKey] = 0;
                    await channel.BasicPublishAsync("", await DeclareErrorQueueAsync(), true, properties, e.Body, token);
                    await channel.BasicAckAsync(e.DeliveryTag, false, token);
                    return;
                }

                var retryAttempt = (int?)headers[RetryingCountHeaderKey] ?? 0;
                var delayAttempt = (int?)headers[DelaysCountHeaderKey] ?? 0;
                await handler.HandleAsync(message, retryAttempt, delayAttempt, token);
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException ex)
            {
                logger.LogError(ex, "Can't process message. Task was canceled.");
                await channel.BasicNackAsync(e.DeliveryTag, false, true, token);
                return;
            }
            catch (DelayMessageException ex)
            {
                var delaysCount = (int?)headers[DelaysCountHeaderKey] ?? 0;
                var maxDelaysCount = options.Value.MaxDelays;

                var timeout = ex.Timeout != null ? (int)Math.Round(ex.Timeout.Value.TotalSeconds) : 60;
                logger.LogError(ex, "Can't process message. Send to delay queue for {timeout} sec. Delays count: {delaysCount} of {maxDelaysCount}.",
                    timeout, delaysCount, maxDelaysCount);

                var newDelaysCount = delaysCount + 1;

                headers[DelaysCountHeaderKey] = newDelaysCount;
                await PublishDelayMessageAsync(ex, headers, e, timeout * 1000);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Can't process message.");

                AddErrorHeaders(ex, headers);

                var maxRetryingCount = options.Value.MaxRetryCount;
                if (maxRetryingCount > 0)
                {
                    var retryingCount = ((int?)headers[RetryingCountHeaderKey] ?? 0) + 1;
                    if (retryingCount <= maxRetryingCount)
                    {
                        headers[RetryingCountHeaderKey] = retryingCount;
                        await channel.BasicPublishAsync("", endpoint.Queue.Name, true, properties, e.Body, token);
                    }
                    else
                    {
                        headers.Remove(RetryingCountHeaderKey);
                        await channel.BasicPublishAsync("", await DeclareErrorQueueAsync(), true, properties, e.Body, token);
                    }
                }
                else if (maxRetryingCount == 0)
                {
                    headers.Remove(RetryingCountHeaderKey);
                    await channel.BasicPublishAsync("", await DeclareErrorQueueAsync(), true, properties, e.Body, token);
                }
            }

            await channel.BasicAckAsync(e.DeliveryTag, false, token);

            async Task PublishDelayMessageAsync(Exception ex, IDictionary<string, object?> headers, BasicDeliverEventArgs e, int timeout)
            {
                AddErrorHeaders(ex, headers);

                properties.Expiration = timeout.ToString();
                await Channel.BasicPublishAsync("", await DeclareDelayQueueAsync(), true, properties, e.Body, token);
            }

            void AddErrorHeaders(Exception ex, IDictionary<string, object?> headers)
            {
                var current = ex;
                var error = current.Message;
                while (current.InnerException != null)
                {
                    current = current.InnerException;
                    error += " -> " + current.Message;
                }

                headers.Remove("error-stacktrace");
                if (ex.StackTrace is string stack)
                {
                    headers["error-stacktrace"] = stack;
                }

                headers.Remove("error-type");
                if (ex.GetType().FullName is string fullName)
                {
                    headers["error-type"] = fullName;
                }

                headers["error"] = error;
            }
        }

        private static IDictionary<string, object?> GetOrCreateHeaders(BasicProperties properties)
        {
            var headers = properties.Headers;
            if (headers == null)
            {
                properties.Headers = headers = new Dictionary<string, object?>();
            }

            if (!headers.ContainsKey(RetryingCountHeaderKey))
            {
                headers.Add(RetryingCountHeaderKey, 0);
            }

            if (!headers.ContainsKey(DelaysCountHeaderKey))
            {
                headers.Add(DelaysCountHeaderKey, 0);
            }

            return headers;
        }

        private async Task<string> DeclareErrorQueueAsync()
        {
            return await Channel.QueueDeclareAsync(
                endpoint.Queue.Name + "_error",
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                endpoint.Queue.Arguments.ToDictionary());
        }

        private async Task<string> DeclareDelayQueueAsync()
        {
            var arguments = new Dictionary<string, object?>(endpoint.Queue.Arguments)
                {
                    { "x-dead-letter-exchange", "" },
                    { "x-dead-letter-routing-key", endpoint.Queue.Name },
                    { "x-message-ttl", 10 * 60 * 1000 } // default 10 min. can be overrided by lower value per message
                };

            return await Channel.QueueDeclareAsync(
                endpoint.Queue.Name + "_delay",
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                arguments);
        }
    }
}
