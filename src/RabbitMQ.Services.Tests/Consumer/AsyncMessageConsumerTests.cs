using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using Xunit;

namespace RabbitMQ.Services.Tests.Consumer
{
    public class AsyncMessageConsumerTests
    {
        public class TestMessage
        {
        }

        public const string ConnectionName = "ConnectionName";

        private readonly AutoMocker mocker = new();
        private readonly AsyncMessageConsumer<TestMessage> consumer;
        private readonly ConsumerConfiguration<TestMessage> options;

        public AsyncMessageConsumerTests()
        {
            options = new ConsumerConfiguration<TestMessage>
            {
                ConnectionName = ConnectionName,
                Url = "amqp://localhost/queue",
            };
            mocker.Use(Options.Create(options));

            consumer = mocker.CreateInstance<AsyncMessageConsumer<TestMessage>>();
        }

        [Fact]
        public async Task Start_ShouldThrowBrokerUnreachableException_CanNotGetConnectionAsync()
        {
            // Arrange
            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>()))
                .Throws(() => new BrokerUnreachableException(new Exception()));

            // Act
            var start = consumer.StartAsync;

            // Assert
            await Assert.ThrowsAsync<BrokerUnreachableException>(() => start());
        }

        [Fact]
        public async Task Start_ShouldStartConsumersAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint
            {
                ConsumersCount = 2
            };

            var channel = mocker.GetMock<IChannel>();

            channel.Setup(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(endpoint.Queue.Name, 0, 0));

            var connection = mocker.GetMock<IConnection>();
            connection.Setup(t => t.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => channel.Object);

            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(It.IsAny<string>()))
                .Returns(() => endpoint);

            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>()))
                .ReturnsAsync(() => connection.Object);

            options.BindQueue = true;

            // Act
            await consumer.StartAsync();

            // Assert
            channel.Verify(t => t.BasicConsumeAsync(
                endpoint.Queue.Name, false, It.IsAny<string>(), false, false, null, It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));

            channel.Verify(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()), Times.Once);

            channel.Verify(t => t.ExchangeDeclareAsync(
                endpoint.Exchange.Name,
                endpoint.Exchange.Type,
                endpoint.Exchange.Durable,
                endpoint.Exchange.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()), Times.Once);

            channel.Verify(t => t.QueueBindAsync(
                endpoint.Queue.Name,
                endpoint.Exchange.Name,
                endpoint.Queue.Routing,
                It.IsAny<IDictionary<string, object?>>(),
                false,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Start_ShouldStartConsumers_WithoutQueueBindingAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint
            {
                ConsumersCount = 2
            };

            var channel = mocker.GetMock<IChannel>();

            channel.Setup(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(endpoint.Queue.Name, 0, 0));

            var connection = mocker.GetMock<IConnection>();
            connection.Setup(t => t.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => channel.Object);

            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(It.IsAny<string>()))
                .Returns(() => endpoint);

            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>()))
                .ReturnsAsync(() => connection.Object);

            options.BindQueue = false;

            // Act
            await consumer.StartAsync();

            // Assert
            channel.Verify(t => t.BasicConsumeAsync(
                endpoint.Queue.Name, false, It.IsAny<string>(), false, false, null, It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));

            channel.Verify(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()), Times.Once);

            channel.Verify(t => t.ExchangeDeclareAsync(
                endpoint.Exchange.Name,
                endpoint.Exchange.Type,
                endpoint.Exchange.Durable,
                endpoint.Exchange.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()), Times.Never);

            channel.Verify(t => t.QueueBindAsync(
                endpoint.Queue.Name,
                endpoint.Exchange.Name,
                endpoint.Queue.Routing,
                It.IsAny<IDictionary<string, object?>>(),
                false,
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Stop_ShouldStopConsumersAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint
            {
                ConsumersCount = 2
            };

            var channel = mocker.GetMock<IChannel>();

            channel.Setup(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(endpoint.Queue.Name, 0, 0));

            var connection = mocker.GetMock<IConnection>();
            connection.Setup(t => t.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => channel.Object);

            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(It.IsAny<string>()))
                .Returns(() => endpoint);

            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>()))
                .ReturnsAsync(() => connection.Object);

            var consumerTags = new List<string>();

            channel.Setup(t => t.BasicConsumeAsync(
                endpoint.Queue.Name, false, It.IsAny<string>(), false, false, null, It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
                .Callback((string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                    IDictionary<string, object> arguments, IAsyncBasicConsumer consumer, CancellationToken _) =>
                    {
                        consumerTag = Guid.NewGuid().ToString();
                        ((AdvancedConsumer<TestMessage>)consumer).HandleBasicConsumeOkAsync(consumerTag, default);
                        consumerTags.Add(consumerTag);
                    });

            await consumer.StartAsync();

            // Act
            await consumer.StopAsync();

            // Assert
            channel.Verify(t => t.Dispose(), Times.Once);
        }

        [Fact]
        public async Task Stop_ShouldSkip_WhenCallsBeforeStartAsync()
        {
            // Act
            await consumer.StopAsync();
        }

        [Fact]
        public async Task Dispose_ShouldBePossibleToDisposeManyTimesAsync()
        {
            // Act
            await consumer.DisposeAsync();
            await consumer.DisposeAsync();
            await consumer.DisposeAsync();
        }
    }
}
