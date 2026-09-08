using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
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
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(() => new BrokerUnreachableException(new Exception()));

            // Act
            Task start() => consumer.StartAsync();

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
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        public async Task Start_ShouldForwardToken_ToBuilderAndChannelSetupAsync()
        {
            // Arrange
            using var cancellation = new CancellationTokenSource();
            var token = cancellation.Token;

            var endpoint = new RabbitMQEndpoint
            {
                ConsumersCount = 1
            };

            var channel = mocker.GetMock<IChannel>();
            channel.Setup(t => t.QueueDeclareAsync(
                endpoint.Queue.Name,
                endpoint.Queue.Durable,
                endpoint.Queue.Exclusive,
                endpoint.Queue.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                token))
                .ReturnsAsync(() => new QueueDeclareOk(endpoint.Queue.Name, 0, 0));

            var connection = mocker.GetMock<IConnection>();
            connection.Setup(t => t.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), token))
                .ReturnsAsync(() => channel.Object);

            mocker.GetMock<IRabbitMQEndpointParser>()
                .Setup(t => t.Parse(It.IsAny<string>()))
                .Returns(() => endpoint);

            mocker.GetMock<IConnectionBuilder>()
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, 0, token))
                .ReturnsAsync(() => connection.Object);

            options.BindQueue = true;

            // Act
            await consumer.StartAsync(token);

            // Assert
            channel.Verify(t => t.BasicQosAsync(0, endpoint.PrefetchCount, false, token), Times.Once);

            channel.Verify(t => t.ExchangeDeclareAsync(
                endpoint.Exchange.Name,
                endpoint.Exchange.Type,
                endpoint.Exchange.Durable,
                endpoint.Exchange.AutoDelete,
                It.IsAny<Dictionary<string, object?>>(),
                false, false,
                token), Times.Once);

            channel.Verify(t => t.QueueBindAsync(
                endpoint.Queue.Name,
                endpoint.Exchange.Name,
                endpoint.Queue.Routing,
                It.IsAny<IDictionary<string, object?>>(),
                false,
                token), Times.Once);

            channel.Verify(t => t.BasicConsumeAsync(
                endpoint.Queue.Name, false, It.IsAny<string>(), false, false, null, It.IsAny<IAsyncBasicConsumer>(),
                token), Times.Once);
        }

        [Fact]
        public async Task Start_ShouldNotPublishChannel_WhenTokenCancelledDuringSetupAsync()
        {
            // Arrange
            using var cancellation = new CancellationTokenSource();
            var connection = SetupConnection();

            var channel = mocker.GetMock<IChannel>();
            channel.SetupGet(t => t.IsOpen).Returns(true);
            channel.Setup(t => t.BasicConsumeAsync(
                It.IsAny<string>(), false, It.IsAny<string>(), false, false, null, It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()))
                .Callback(() => cancellation.Cancel())
                .ReturnsAsync("tag");

            // Act
            Task start() => consumer.StartAsync(cancellation.Token);

            // Assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => start());
            Assert.False(consumer.IsConnected);

            channel.Verify(t => t.CloseAsync(Constants.ReplySuccess, It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
            channel.Verify(t => t.DisposeAsync(), Times.Once);
            connection.VerifyAdd(
                t => t.ConnectionShutdownAsync += It.IsAny<AsyncEventHandler<ShutdownEventArgs>>(),
                Times.Never);
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
            channel.SetupGet(t => t.IsOpen).Returns(true);

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
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>()))
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

            // Assert: the channel is aborted asynchronously, never through the blocking Dispose.
            channel.Verify(t => t.CloseAsync(Constants.ReplySuccess, It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
            channel.Verify(t => t.DisposeAsync(), Times.Once);
            channel.Verify(t => t.Dispose(), Times.Never);
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

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        public async Task IsConnected_ShouldBeTrue_OnlyWhenConnectionAndChannelAreOpenAsync(bool connectionOpen, bool channelOpen, bool expected)
        {
            // Arrange
            var connection = SetupConnection();
            connection.SetupGet(t => t.IsOpen).Returns(connectionOpen);
            mocker.GetMock<IChannel>().SetupGet(t => t.IsOpen).Returns(channelOpen);

            Assert.False(consumer.IsConnected);

            // Act
            await consumer.StartAsync();

            // Assert
            Assert.Equal(expected, consumer.IsConnected);

            await consumer.StopAsync();
            Assert.False(consumer.IsConnected);
        }

        [Fact]
        public async Task ConnectionShutdown_ShouldRaiseConnectionLostAsync()
        {
            // Arrange
            var connection = SetupConnection();
            await consumer.StartAsync();

            var raised = 0;
            consumer.ConnectionLost += (_, _) => raised++;

            // Act
            connection.Raise(t => t.ConnectionShutdownAsync += null, connection.Object, ShutdownArgs);

            // Assert
            Assert.Equal(1, raised);
        }

        [Fact]
        public async Task ConnectionShutdown_ShouldNotRaiseConnectionLost_AfterStopAsync()
        {
            // Arrange
            var connection = SetupConnection();
            await consumer.StartAsync();

            var raised = 0;
            consumer.ConnectionLost += (_, _) => raised++;

            // Act
            await consumer.StopAsync();
            connection.Raise(t => t.ConnectionShutdownAsync += null, connection.Object, ShutdownArgs);

            // Assert
            Assert.Equal(0, raised);

            connection.VerifyRemove(
                t => t.ConnectionShutdownAsync -= It.IsAny<AsyncEventHandler<ShutdownEventArgs>>(),
                Times.Once);
        }

        [Fact]
        public async Task ChannelShutdown_ShouldRaiseConnectionLostAsync()
        {
            // Arrange
            SetupConnection();
            await consumer.StartAsync();

            var raised = 0;
            consumer.ConnectionLost += (_, _) => raised++;

            // Act: the broker closed the channel alone, the connection is still up.
            var channel = mocker.GetMock<IChannel>();
            channel.Raise(t => t.ChannelShutdownAsync += null, channel.Object, ShutdownArgs);

            // Assert
            Assert.Equal(1, raised);
        }

        [Fact]
        public async Task ChannelShutdown_ShouldNotRaiseConnectionLost_AfterStopAsync()
        {
            // Arrange
            SetupConnection();
            await consumer.StartAsync();

            var raised = 0;
            consumer.ConnectionLost += (_, _) => raised++;

            // Act
            await consumer.StopAsync();
            var channel = mocker.GetMock<IChannel>();
            channel.Raise(t => t.ChannelShutdownAsync += null, channel.Object, ShutdownArgs);

            // Assert
            Assert.Equal(0, raised);

            channel.VerifyRemove(
                t => t.ChannelShutdownAsync -= It.IsAny<AsyncEventHandler<ShutdownEventArgs>>(),
                Times.Once);
        }

        [Fact]
        public async Task Start_ShouldCloseChannel_WhenSetupFailsAsync()
        {
            // Arrange
            SetupConnection();

            var channel = mocker.GetMock<IChannel>();
            channel.SetupGet(t => t.IsOpen).Returns(true);
            channel.Setup(t => t.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<Dictionary<string, object?>>(), false, false, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("declare failed"));

            // Act
            Task start() => consumer.StartAsync();

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => start());
            Assert.False(consumer.IsConnected);

            channel.Verify(t => t.CloseAsync(Constants.ReplySuccess, It.IsAny<string>(), true, It.IsAny<CancellationToken>()), Times.Once);
            channel.Verify(t => t.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task Stop_ShouldNotThrow_WhenClosingChannelFailsAsync()
        {
            // Arrange
            SetupConnection();

            var channel = mocker.GetMock<IChannel>();
            channel.SetupGet(t => t.IsOpen).Returns(true);
            channel.Setup(t => t.CloseAsync(It.IsAny<ushort>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker is gone"));

            await consumer.StartAsync();

            // Act
            await consumer.StopAsync();

            // Assert
            Assert.False(consumer.IsConnected);
        }

        private static ShutdownEventArgs ShutdownArgs =>
            new(ShutdownInitiator.Peer, Constants.ConnectionForced, "connection closed");

        private Mock<IConnection> SetupConnection()
        {
            var endpoint = new RabbitMQEndpoint
            {
                ConsumersCount = 1
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
                .Setup(t => t.GetConnectionAsync(It.IsAny<IRabbitMQEndpoint>(), ConnectionName, ConnectionMode.Consumer, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => connection.Object);

            return connection;
        }
    }
}
