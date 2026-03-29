using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Exceptions;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Text;
using Xunit;

namespace RabbitMQ.Services.Tests.Consumer
{
    public class AdvancedConsumerTests
    {
        public class T
        {
        }

        private const string RetryingCountHeaderKey = "retrying-count";
        private const string DelaysCountHeaderKey = "delays-count";

        private readonly AutoMocker mocker = new();
        private readonly AdvancedConsumer<T> consumer;
        private readonly RabbitMQEndpoint endpoint;
        private readonly ConsumerConfiguration<T> options;

        public AdvancedConsumerTests()
        {
            endpoint = new RabbitMQEndpoint
            {
                Queue =
                {
                    Name = "test",
                }
            };
            mocker.Use<IRabbitMQEndpoint>(endpoint);

            options = new ConsumerConfiguration<T>
            {
                MaxRetryCount = 0
            };
            mocker.Use(Options.Create(options));

            consumer = mocker.CreateInstance<AdvancedConsumer<T>>();
        }

        [Fact]
        public async Task HandlersHandleAsync_ShouldBeCanceledByCancelCall()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = mocker.GetMock<IBasicProperties>();
            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            await consumer.HandleBasicConsumeOkAsync(consumerTag, TestContext.Current.CancellationToken);

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(async (string tag, bool _, CancellationToken _) =>
                {
                    await consumer.HandleBasicCancelAsync(tag, TestContext.Current.CancellationToken);
                });

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((T message, int _, int _, CancellationToken token) => Task.Delay(Timeout.Infinite, token));

            async Task Cancel()
            {
                await Task.Delay(100);
                await consumer.CancelAsync();
            }

            var handler = consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties.Object, body, TestContext.Current.CancellationToken);

            // Act
            await Task.WhenAll(handler, Cancel());

            // Assert
            mocker.VerifyAll();
            mocker.GetMock<IChannel>().Verify(t => t.BasicNackAsync(deliveryTag, false, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnMessageReceived_ShouldBeCanceledByCancelCall_EvenIfHandleAsyncIgnoreIt()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = mocker.GetMock<IBasicProperties>();
            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            await consumer.HandleBasicConsumeOkAsync(consumerTag, TestContext.Current.CancellationToken);

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicCancelAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(async (string tag, bool _, CancellationToken _) =>
                {
                    await consumer.HandleBasicCancelAsync(tag, TestContext.Current.CancellationToken);
                });

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns((T message, int _, int _, CancellationToken _) => Task.Delay(500, TestContext.Current.CancellationToken));

            async Task Cancel()
            {
                await Task.Delay(100);
                await consumer.CancelAsync();
            }

            var handler = consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties.Object, body, TestContext.Current.CancellationToken);

            // Act
            await Task.WhenAll(handler, Cancel());

            // Assert
            mocker.VerifyAll();
            mocker.GetMock<IChannel>().Verify(t => t.BasicNackAsync(deliveryTag, false, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task OnMessageReceived_ShouldAckMessage()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = mocker.GetMock<IBasicProperties>();
            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            var message = mocker.GetMock<T>();
            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.GetMessage(It.IsAny<string>()))
                .Returns(() => message.Object);

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(message.Object, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Verifiable();

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties.Object, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Fact]
        public async Task OnMessageReceived_ShouldMoveMessageToErrorQueue_WhenGetMessageThrowsException()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = mocker.GetMock<IBasicProperties>();
            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            var message = mocker.GetMock<T>();
            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.GetMessage(It.IsAny<string>()))
                .Throws(new Exception("error"));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            var errorQueueName = endpoint.Queue.Name + "_error";
            mocker.GetMock<IChannel>()
                .Setup(t => t.QueueDeclareAsync(
                    errorQueueName,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    It.IsAny<Dictionary<string, object?>>(),
                    false,
                    false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(errorQueueName, 0, 0));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", errorQueueName, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Verifiable();

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties.Object, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Theory]
        [InlineData(null)]
        [InlineData(10)]
        public async Task OnMessageReceived_ShouldMoveMessageToDelayQueue_WhenHandleAsyncThrowsDelayMessageException(int? timeout)
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";

            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            var message = mocker.GetMock<T>();
            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new DelayMessageException(timeout != null ? TimeSpan.FromSeconds(timeout.Value) : null));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            var delayQueueName = endpoint.Queue.Name + "_delay";
            mocker.GetMock<IChannel>()
                .Setup(t => t.QueueDeclareAsync(
                    delayQueueName,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    It.IsAny<Dictionary<string, object?>>(),
                    false, false,
                    It.IsAny<CancellationToken>()))
                .Callback((string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?> arguments, bool _, bool _, CancellationToken _) =>
                {
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-dead-letter-exchange", "")));
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-dead-letter-routing-key", endpoint.Queue.Name)));
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-message-ttl", 10 * 60 * 1000))); // 10 min
                })
                .ReturnsAsync(() => new QueueDeclareOk(delayQueueName, 0, 0));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", delayQueueName, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Callback((string _, string _, bool _, BasicProperties properties, ReadOnlyMemory<byte> _, CancellationToken _) =>
                {
                    if (timeout is null)
                    {
                        Assert.Equal("60000", properties.Expiration);
                    }
                    else
                    {
                        Assert.Equal((timeout * 1000).ToString(), properties.Expiration);
                    }
                });

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, new BasicProperties(), body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task OnMessageReceived_ShouldMoveMessageToDelayQueue_WhenHandleAsyncThrowsDelayMessageException_AndUseDelayCountFlagIsEnabled(int delayAttempt)
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";

            var headers = new Dictionary<string, object?>()
            {
                { DelaysCountHeaderKey, delayAttempt }
            };

            var properties = new BasicProperties
            {
                Headers = headers
            };

            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            var message = mocker.GetMock<T>();
            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback<T, int, int, CancellationToken>((_, a, _, _) =>
                {
                    Assert.Equal(0, a);
                })
                .Throws(new DelayMessageException(TimeSpan.FromSeconds(15)));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            var delayQueueName = endpoint.Queue.Name + "_delay";
            mocker.GetMock<IChannel>()
                .Setup(t => t.QueueDeclareAsync(
                    delayQueueName,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    It.IsAny<Dictionary<string, object?>>(),
                    false, false,
                    It.IsAny<CancellationToken>()))
                .Callback((string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object?> arguments, bool _, bool _, CancellationToken _) =>
                {
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-dead-letter-exchange", "")));
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-dead-letter-routing-key", endpoint.Queue.Name)));
                    Assert.True(arguments.Contains(new KeyValuePair<string, object?>("x-message-ttl", 10 * 60 * 1000))); // 10 min
                })
                .ReturnsAsync(() => new QueueDeclareOk(delayQueueName, 0, 0));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", delayQueueName, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Callback((string _, string _, bool _, BasicProperties properties, ReadOnlyMemory<byte> _, CancellationToken _) =>
                {
                    Assert.NotNull(properties.Headers);
                    var count = properties.Headers[DelaysCountHeaderKey];
                    Assert.Equal(delayAttempt + 1, count);
                });

            options.MaxRetryCount = 0;
            options.MaxDelays = 3;

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Fact]
        public async Task OnMessageReceived_LogCritical_WhenSomethingWentWrong()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = new BasicProperties();
            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            var message = mocker.GetMock<T>();
            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.GetMessage(It.IsAny<string>()))
                .Returns(() => message.Object);

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(message.Object, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Verifiable();

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Throws(new Exception());

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body, TestContext.Current.CancellationToken);

            // Assert
#pragma warning disable CA1873 // Avoid potentially expensive logging
            mocker.GetMock<ILogger>()
                .Verify(t => t.Log(LogLevel.Critical, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()));
#pragma warning restore CA1873 // Avoid potentially expensive logging
        }

        [Fact]
        public async Task OnMessageReceived_ShouldMoveMessageToErrorQueue_WhenHandleAsyncThrowsException_MaxRetryCountIsZero()
        {
            // Arrange
            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = new BasicProperties();

            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception());

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            var errorQueueName = endpoint.Queue.Name + "_error";
            mocker.GetMock<IChannel>()
                .Setup(t => t.QueueDeclareAsync(
                    errorQueueName,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    It.IsAny<Dictionary<string, object?>>(),
                    false, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(errorQueueName, 0, 0));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", errorQueueName, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Callback((string exchange, string routingKey, bool mandatory, IBasicProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken _) =>
                {
                    Assert.NotNull(basicProperties.Headers);
                    Assert.False(basicProperties.Headers.ContainsKey(RetryingCountHeaderKey));
                });

            options.MaxRetryCount = 0;

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Fact]
        public async Task OnMessageReceived_ShouldMoveMessageToErrorQueue_WhenHandleAsyncThrowsExceptionOnLastAttempt()
        {
            // Arrange
            var currentRetryCounter = 2;

            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = new BasicProperties
            {
                Headers = new Dictionary<string, object?>
                {
                    { RetryingCountHeaderKey, currentRetryCounter }
                }
            };

            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception("a", new Exception("b")));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            var errorQueueName = endpoint.Queue.Name + "_error";
            mocker.GetMock<IChannel>()
                .Setup(t => t.QueueDeclareAsync(
                    errorQueueName,
                    endpoint.Queue.Durable,
                    endpoint.Queue.Exclusive,
                    endpoint.Queue.AutoDelete,
                    It.IsAny<Dictionary<string, object?>>(),
                    false, false,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new QueueDeclareOk(errorQueueName, 0, 0));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", errorQueueName, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Callback((string exchange, string routingKey, bool mandatory, IBasicProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken _) =>
                {
                    Assert.NotNull(basicProperties.Headers);
                    Assert.False(basicProperties.Headers.ContainsKey(RetryingCountHeaderKey));
                    Assert.Equal("a -> b", basicProperties.Headers["error"]);
                });

            options.MaxRetryCount = currentRetryCounter;

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }

        [Fact]
        public async Task OnMessageReceived_ShouldMoveBackMessageToQueue_WhenHandleAsyncThrowsExceptionAndRetryCounterLessThatMaxValue()
        {
            // Arrange
            var currentRetryCounter = 2;

            var consumerTag = Guid.NewGuid().ToString();
            var deliveryTag = (ulong)new Random().Next();
            var redelivered = false;
            var exchange = "exhcnage";
            var routingKey = "";
            var properties = new BasicProperties
            {
                Headers = new Dictionary<string, object?>
                {
                    { RetryingCountHeaderKey, currentRetryCounter }
                }
            };

            ReadOnlyMemory<byte> body = Encoding.UTF8.GetBytes("{}");

            mocker.GetMock<IMessageHandler<T>>()
                .Setup(t => t.HandleAsync(It.IsAny<T>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Throws(new Exception("a", new Exception("b")));

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicAckAsync(deliveryTag, false, It.IsAny<CancellationToken>()))
                .Verifiable();

            mocker.GetMock<IChannel>()
                .Setup(t => t.BasicPublishAsync("", endpoint.Queue.Name, true, It.IsAny<BasicProperties>(), body, It.IsAny<CancellationToken>()))
                .Callback((string exchange, string routingKey, bool mandatory, IBasicProperties basicProperties, ReadOnlyMemory<byte> body, CancellationToken _) =>
                {
                    Assert.NotNull(basicProperties.Headers);
                    Assert.Equal(currentRetryCounter + 1, basicProperties.Headers[RetryingCountHeaderKey]);
                });

            options.MaxRetryCount = currentRetryCounter * 2;

            // Act
            await consumer.HandleBasicDeliverAsync(consumerTag, deliveryTag, redelivered, exchange, routingKey, properties, body, TestContext.Current.CancellationToken);

            // Assert
            mocker.VerifyAll();
        }
    }
}
