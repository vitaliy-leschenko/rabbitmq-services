using RabbitMQ.Services.Interfaces;
using System.Text.Json;

namespace RabbitMQ.Services
{
    public abstract class BaseMessageHandler<T> : IMessageHandler<T> where T : class
    {
        private readonly JsonSerializerOptions serializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public virtual T GetMessage(string data)
        {
            var message = JsonSerializer.Deserialize<T>(data, serializerOptions)
                ?? throw new JsonException("Can't deserialize object");

            return message;
        }

        public virtual async Task HandleAsync(T message, CancellationToken token)
        {
            await Task.CompletedTask;
        }

        public virtual async Task HandleAsync(T message, int retryAttempt, CancellationToken token)
        {
            await HandleAsync(message, token);
        }

        public virtual async Task HandleAsync(T message, int retryAttempt, int delayAttempt, CancellationToken token)
        {
            await HandleAsync(message, retryAttempt, token);
        }
    }
}
