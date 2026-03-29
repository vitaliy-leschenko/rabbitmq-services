using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Services.Entities;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Text;
using System.Text.Json;

namespace RabbitMQ.Services.Implementations
{
    public sealed class AsyncMessageSender(
        TimeProvider timeProvider,
        IOutboxDbContext context,
        IOptions<OutboxOptions> options,
        IActivityDataProvider activityDataProvider,
        ILogger<AsyncMessageSender> logger) : IAsyncMessageSender
    {
        private readonly JsonSerializerOptions serializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private readonly TimeProvider timeProvider = timeProvider;
        private readonly IOutboxDbContext context = context;
        private readonly IOptions<OutboxOptions> options = options;
        private readonly IActivityDataProvider activityDataProvider = activityDataProvider;
        private readonly ILogger<AsyncMessageSender> logger = logger;

        public async Task SendMessageAsync<T>(string uri, T message, bool bindQueue) where T : BaseMessage
        {
            activityDataProvider.SetActivityData(message);

            var json = JsonSerializer.Serialize(message, serializerOptions);
            var body = Encoding.UTF8.GetBytes(json);

            await SendMessageAsync(uri, "application/json", body, bindQueue);
        }

        public async Task SendMessageAsync(string uri, string contentType, byte[] body, bool bindQueue)
        {
            logger.LogDebug("Send message to queue '{queue}'", uri);

            await context.Set<OutboxMessage>().AddAsync(new OutboxMessage
            {
                CreatedAtUtc = timeProvider.GetUtcNow(),
                Uri = uri,
                Body = body,
                ContentType = contentType,
                BindQueue = bindQueue,
                Namespace = options.Value.Namespace,
            });
        }
    }
}
