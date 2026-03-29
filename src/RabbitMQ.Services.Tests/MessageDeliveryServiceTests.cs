using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Entities;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Text;
using Xunit;

namespace RabbitMQ.Services.Tests
{
    public class MessageDeliveryServiceTests
    {
        private const string ConnectionName = "demo";
        private const string DefaultNamespace = "default";
        private readonly AutoMocker mocker = new();
        private readonly IOutboxDbContext db;
        private readonly OutboxOptions options;
        private readonly MessageDeliveryService service;

        public MessageDeliveryServiceTests(IOutboxDbContext db)
        {
            this.db = db;
            options = new OutboxOptions
            {
                ConnectionName = ConnectionName,
                Namespace = DefaultNamespace
            };
            mocker.Use(db);
            mocker.Use(Options.Create(options));

            service = mocker.CreateInstance<MessageDeliveryService>();
        }

        [Fact]
        public async Task SendMessagesAsync_ShouldTryToSendAllMessages()
        {
            // Arrange
            var message = new OutboxMessage
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Uri = "amqp://localhost",
                BindQueue = true,
                Body = Encoding.UTF8.GetBytes("{}"),
                ContentType = "application/json",
                Namespace = DefaultNamespace,
            };
            db.Set<OutboxMessage>().Add(message);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            ((DbContext)db).ChangeTracker.Clear();

            var uri = message.Uri;
            SetupSendMessage(uri);

            // Act
            await service.SendMessagesAsync();

            // Assert
            ((DbContext)db).ChangeTracker.Clear();
            Assert.False(db.Set<OutboxMessage>().Any(t => t.MessageId == message.MessageId));

            mocker.VerifyAll();
        }

        [Fact]
        public async Task SendMessagesAsync_ShouldSkipMessageWhenCanNotSendIt()
        {
            // Arrange
            var message = new OutboxMessage
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Uri = "amqp://localhost",
                BindQueue = true,
                Body = Encoding.UTF8.GetBytes("{}"),
                ContentType = "application/json",
                Namespace = DefaultNamespace,
            };
            db.Set<OutboxMessage>().Add(message);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            ((DbContext)db).ChangeTracker.Clear();

            var uri = message.Uri;
            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(uri))
                .Throws(new Exception("example"));

            // Act
            await service.SendMessagesAsync();

            // Assert
            ((DbContext)db).ChangeTracker.Clear();
            Assert.True(db.Set<OutboxMessage>().Any(t => t.MessageId == message.MessageId));

            mocker.VerifyAll();
        }

        [Fact]
        public async Task SendMessagesAsync_ShouldNotCrashes()
        {
            // Arrange
            var message = new OutboxMessage
            {
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Uri = "amqp://localhost/queue",
                BindQueue = true,
                Body = Encoding.UTF8.GetBytes("{}"),
                ContentType = "application/json",
                Namespace = DefaultNamespace,
            };
            db.Set<OutboxMessage>().Add(message);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            ((DbContext)db).ChangeTracker.Clear();

            var uri = message.Uri;
            SetupSendMessage(uri);

            ((DbContext)db).SavingChanges += SavingChanges;

            // Act
            await service.SendMessagesAsync();

            // Assert
            ((DbContext)db).ChangeTracker.Clear();
            Assert.True(db.Set<OutboxMessage>().Any(t => t.MessageId == message.MessageId));

            mocker.VerifyAll();

            static void SavingChanges(object? sender, SavingChangesEventArgs e)
            {
                throw new InvalidProgramException();
            }
        }

        private void SetupSendMessage(string uri)
        {
            var endpoint = new RabbitMQEndpoint { Uri = uri, };
            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(uri))
                .Returns(endpoint);

            var channel = new Mock<IChannel>();
            channel.Setup(t => t.ExchangeDeclareAsync(
                endpoint.Exchange.Name,
                endpoint.Exchange.Type,
                endpoint.Exchange.Durable,
                endpoint.Exchange.AutoDelete,
                It.IsAny<IDictionary<string, object?>>(),
                false,
                false,
                It.IsAny<CancellationToken>()))
                .Verifiable();

            channel.Setup(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<IDictionary<string, object?>>(),
                false,
                false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(endpoint.Queue.Name, 0, 0));

            channel.Setup(t => t.BasicPublishAsync(
                endpoint.Exchange.Name,
                endpoint.Queue.Routing,
                false,
                It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()));

            var connection = new Mock<IConnection>();
            connection.Setup(t => t.CreateChannelAsync(
                It.IsAny<CreateChannelOptions>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => channel.Object);

            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Producer, It.IsAny<int>()))
                .ReturnsAsync(() => connection.Object);
        }
    }
}
