using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq.AutoMock;
using RabbitMQ.Services.Entities;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    file class TestMessage : BaseMessage
    {
        public int Data { get; set; }
    }

    public class AsyncMessageSenderTests
    {
        private const string ConnectionName = "demo";
        private const string Namespace = "namespace";

        private readonly FakeTimeProvider timeProvider = new();
        private readonly AutoMocker mocker = new();
        private readonly AsyncMessageSender sender;
        private readonly OutboxOptions options;
        private readonly IOutboxDbContext db;

        public AsyncMessageSenderTests(IOutboxDbContext db)
        {
            this.db = db;

            options = new OutboxOptions
            {
                ConnectionName = ConnectionName,
                Namespace = Namespace
            };
            mocker.Use(db);
            mocker.Use<TimeProvider>(timeProvider);
            mocker.Use(Options.Create(options));
            sender = mocker.CreateInstance<AsyncMessageSender>();
        }

        [Fact]
        public async Task SendMessageAsync_ShouldSendMessageAndBindQueue()
        {
            // Arrange
            var uri = "amqp://localhost/exchange";
            var message = new TestMessage
            {
                Data = 42
            };

            var now = DateTimeOffset.UtcNow;
            timeProvider.SetUtcNow(now);

            // Act
            await sender.SendMessageAsync(uri, message, true);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Assert
            var result = db.Set<OutboxMessage>().First();
            Assert.True(result.BindQueue);
            Assert.Equal(uri, result.Uri);
            Assert.Equal(now, result.CreatedAtUtc);
            Assert.Equal(Namespace, result.Namespace);
        }

        [Fact]
        public async Task SendMessageAsync_ShouldSendMessageButNotBindQueue()
        {
            // Arrange
            var uri = "amqp://localhost/exchange";
            var message = new TestMessage
            {
                Data = 42
            };

            var now = DateTimeOffset.UtcNow;
            timeProvider.SetUtcNow(now);

            // Act
            await sender.SendMessageAsync(uri, message, false);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Assert
            var result = db.Set<OutboxMessage>().First();
            Assert.False(result.BindQueue);
            Assert.Equal(uri, result.Uri);
            Assert.Equal(now, result.CreatedAtUtc);
            Assert.Equal(Namespace, result.Namespace);
        }
    }
}
