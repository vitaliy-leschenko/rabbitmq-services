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
        IUriMasker uriMasker,
        ILogger<MessageDeliveryService> logger) : IMessageDeliveryService
    {
        private readonly IOutboxDbContext db = db;
        private readonly IConnectionBuilder builder = builder;
        private readonly IRabbitMQEndpointParser endpointParser = endpointParser;
        private readonly IOptions<OutboxOptions> options = options;
        private readonly IUriMasker uriMasker = uriMasker;
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
                        .Take(options.Value.BatchSize)
                        .ToListAsync();

                    if (messages.Count == 0)
                    {
                        break;
                    }

                    logger.LogInformation("Found {count} undelivered messages", messages.Count);

                    foreach (var group in messages.GroupBy(t => new { t.Uri, t.BindQueue, t.ContentType }))
                    {
                        var items = group.ToList();
                        var (uri, bindQueue, contentType) = (group.Key.Uri, group.Key.BindQueue, group.Key.ContentType);
                        var maskedUri = uriMasker.Mask(uri);
                        try
                        {
                            var bodies = items.Select(t => t.Body).ToList();
                            logger.LogDebug("Send messages to queue '{queue}'", maskedUri);

                            await SendMessagesAsync(uri, bindQueue, bodies, contentType);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Something went wrong while sending messages to queue '{queue}'", maskedUri);

                            continue;
                        }

                        db.Set<OutboxMessage>().RemoveRange(items);
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

        private async Task SendMessagesAsync(string uri, bool bindQueue, List<byte[]> bodies, string contentType)
        {
            var endpoint = endpointParser.Parse(uri);
            var connection = await builder.GetConnectionAsync(endpoint, options.Value.ConnectionName, ConnectionMode.Producer);
            using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: false));

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
                Headers = new Dictionary<string, object?>
                {
                    { "Content-Type", contentType }
                },
                MessageId = Guid.NewGuid().ToString(),
                Persistent = true,
                ContentType = contentType
            };

            var publishTasks = bodies.Select(body => channel.BasicPublishAsync(endpoint.Exchange.Name, endpoint.Queue.Routing, false, properties, body).AsTask()).ToList();
            await Task.WhenAll(publishTasks);
        }
    }
}
