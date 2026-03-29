using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Services.Entities;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Text;

namespace RabbitMQ.Services
{
    public class MessageDeliveryService(
        IOutboxDbContext db,
        IConnectionBuilder builder,
        IRabbitMQEndpointParser endpointParser,
        IOptions<OutboxOptions> options,
        ILogger<MessageDeliveryService> logger) : IMessageDeliveryService
    {
        private readonly IOutboxDbContext db = db;
        private readonly IConnectionBuilder builder = builder;
        private readonly IRabbitMQEndpointParser endpointParser = endpointParser;
        private readonly IOptions<OutboxOptions> options = options;
        private readonly ILogger<MessageDeliveryService> logger = logger;

        public async Task SendMessagesAsync()
        {
            try
            {
                var cursor = 0L;
                while (true)
                {
                    var messages = await db.Set<OutboxMessage>()
                        .Where(t => t.Namespace == options.Value.Namespace)
                        .Where(t => t.MessageId > cursor)
                        .OrderBy(t => t.MessageId)
                        .Take(42)
                        .ToListAsync();

                    if (messages.Count == 0)
                    {
                        break;
                    }

                    logger.LogInformation("Found {count} undelivered messages", messages.Count);

                    foreach (var message in messages)
                    {
                        try
                        {
                            logger.LogDebug("Send message to queue '{queue}'", message.Uri);

                            await SendMessageAsync(message.Uri, message.BindQueue, message.Body, message.ContentType);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Can't send message to {queue} message: {message}",
                                message.Uri, Encoding.UTF8.GetString(message.Body));

                            continue;
                        }

                        db.Set<OutboxMessage>().Remove(message);
                        await db.SaveChangesAsync();
                    }

                    cursor = messages.Last().MessageId;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Something went wrong.");
            }
        }

        private async Task SendMessageAsync(string uri, bool bindQueue, byte[] body, string contentType)
        {
            var endpoint = endpointParser.Parse(uri);
            var connection = await builder.GetConnectionAsync(endpoint, options.Value.ConnectionName, ConnectionMode.Producer);
            using var channel = await connection.CreateChannelAsync();

            await channel.ExchangeDeclareAsync(
                endpoint.Exchange.Name,
                endpoint.Exchange.Type,
                endpoint.Exchange.Durable,
                endpoint.Exchange.AutoDelete,
                endpoint.Exchange.Arguments.ToDictionary());

            if (bindQueue && endpoint.Queue.Name != "*")
            {
                string queue = await channel.QueueDeclareAsync(
                    endpoint.Queue.Name,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    endpoint.Queue.Arguments.ToDictionary());
                await channel.QueueBindAsync(queue, endpoint.Exchange.Name, endpoint.Queue.Routing);
            }

            var properties = new BasicProperties
            {
                Headers = new Dictionary<string, object?> // mass transit needs this
                {
                    { "Content-Type", contentType }
                },
                MessageId = Guid.NewGuid().ToString(),
                Persistent = true,
                ContentType = contentType
            };
            await channel.BasicPublishAsync(endpoint.Exchange.Name, endpoint.Queue.Routing, true, properties, body);
        }
    }
}
