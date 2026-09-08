using Microsoft.Extensions.Time.Testing;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    public class ConnectionBuilderTests
    {
        private const string ConnectionName = "connectionName";

        private readonly AutoMocker mocker = new();
        private readonly FakeTimeProvider timeProvider = new();
        private readonly ConnectionBuilder builder;

        public ConnectionBuilderTests()
        {
            mocker.Use<IUriMasker>(new UriMasker());
            mocker.Use<TimeProvider>(timeProvider);
            builder = mocker.CreateInstance<ConnectionBuilder>();
        }

        [Fact]
        public async Task GetConnection_ShouldCreateNewConnectionAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();

            var connection = new Mock<IConnection>();
            connection.SetupGet(t => t.IsOpen).Returns(true);

            var factory = new Mock<IConnectionFactory>();
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            // Act
            var result = await builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);

            // Assert
            mocker.VerifyAll();
            factory.VerifyAll();
            Assert.True(result.IsOpen);
        }

        [Fact]
        public async Task GetConnection_ShouldCacheConnectionsAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();

            var connection = new Mock<IConnection>();
            connection.SetupGet(t => t.IsOpen).Returns(true);

            var factory = new Mock<IConnectionFactory>();
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            // Act
            var conn1 = await builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);
            var conn2 = await builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);

            // Assert
            Assert.Equal(conn1, conn2);

            mocker.GetMock<IConnectionFactoryBuilder>().Verify(t => t.CreateConnectionFactory(endpoint), Times.Once);
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetConnection_ShouldReConnect_WhenConnectionIsClosedAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();

            var closed = new Mock<IConnection>();
            closed.SetupGet(t => t.IsOpen).Returns(false);
            closed.Setup(t => t.DisposeAsync()).Verifiable();

            var opened = new Mock<IConnection>();
            opened.SetupGet(t => t.IsOpen).Returns(true);

            var connectionAttempt = 0;
            var factory = new Mock<IConnectionFactory>();
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    return connectionAttempt++ == 0 ? closed.Object : opened.Object;
                });

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            // Act
            var result = await AdvanceUntilCompletedAsync(
                builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer),
                ConnectionBuilder.RetryDelay);

            // Assert
            Assert.Equal(opened.Object, result);

            mocker.VerifyAll();
            factory.VerifyAll();
            closed.VerifyAll();
            opened.VerifyAll();

            mocker.GetMock<IConnectionFactoryBuilder>().Verify(t => t.CreateConnectionFactory(endpoint), Times.Once);
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetConnection_ShouldThrowException_WhenCanNotConnectAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint
            {
                Uri = "amqp://user:secret@localhost/vhost/queue"
            };

            var connection = new Mock<IConnection>();
            connection.SetupGet(t => t.IsOpen).Returns(false);

            var factory = new Mock<IConnectionFactory>();
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            // Act
            Task<IConnection> act() => AdvanceUntilCompletedAsync(
                builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer),
                ConnectionBuilder.RetryDelay);

            // Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => act());
            Assert.Equal("Can't open connection to amqp://***:***@localhost/vhost/queue", ex.Message);
            Assert.DoesNotContain("secret", ex.Message);
        }

        [Fact]
        public async Task Dispose_ShouldDisposeConnectionsAsync()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();

            var connection = new Mock<IConnection>();
            connection.SetupGet(t => t.IsOpen).Returns(true);

            var factory = new Mock<IConnectionFactory>();
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            await builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);

            // Act
            builder.Dispose();

            // Assert
            connection.Verify(t => t.Dispose());
        }

        [Fact]
        public async Task GetConnection_ShouldShareOneConnectAttempt_WhenCalledConcurrently()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();
            var connection = OpenConnection();

            var pending = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = SetupFactory(endpoint);
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .Returns(pending.Task);

            // Act
            var callers = Enumerable.Range(0, 15)
                .Select(_ => Task.Run(() => builder.GetConnectionAsync(
                    endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken)))
                .ToArray();

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.All(callers, caller => Assert.False(caller.IsCompleted));

            pending.SetResult(connection.Object);
            var results = await Task.WhenAll(callers);

            // Assert
            Assert.All(results, result => Assert.Same(connection.Object, result));
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetConnection_ShouldRetryConnect_WhenPreviousAttemptFaulted()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();
            var connection = OpenConnection();

            var factory = SetupFactory(endpoint);
            factory.SetupSequence(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new BrokerUnreachableException(new Exception()))
                .ReturnsAsync(connection.Object);

            // Act
            await Assert.ThrowsAsync<BrokerUnreachableException>(() => builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken));

            var result = await builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);

            // Assert
            Assert.Same(connection.Object, result);
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetConnection_ShouldStopWaiting_WhenCallerTokenIsCancelled()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();
            var connection = OpenConnection();

            var pending = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            var factory = SetupFactory(endpoint);
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .Returns(pending.Task);

            using var cancellation = new CancellationTokenSource();
            var first = builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer, 0, cancellation.Token);

            // Act
            cancellation.Cancel();

            // Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            // The attempt itself survives the cancelled waiter and serves the next caller.
            var second = builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);
            pending.SetResult(connection.Object);

            Assert.Same(connection.Object, await second);
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetConnection_ShouldFail_WhenConnectExceedsConnectTimeout()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();
            var connection = OpenConnection();

            var attempt = 0;
            var factory = SetupFactory(endpoint);
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .Returns(async (IEnumerable<AmqpTcpEndpoint> _, string _, CancellationToken token) =>
                {
                    if (Interlocked.Increment(ref attempt) == 1)
                    {
                        // The broker accepted TCP but never answers: only cancellation ends this.
                        await Task.Delay(Timeout.Infinite, token);
                    }

                    return connection.Object;
                });

            var first = builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);

            // Act
            timeProvider.Advance(ConnectionBuilder.ConnectTimeout);

            // Assert
            await Assert.ThrowsAsync<TimeoutException>(() => first);

            var second = await builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);
            Assert.Same(connection.Object, second);

            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task GetConnection_ShouldReplaceClosedConnectionOnce_WhenManyCallersReconnect()
        {
            // Arrange: fifteen consumers share one connection name, the way common-services does.
            var endpoint = new RabbitMQEndpoint();

            var isOpen = true;
            var closed = new Mock<IConnection>();
            closed.SetupGet(t => t.IsOpen).Returns(() => isOpen);
            closed.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);

            var opened = OpenConnection();

            var attempt = 0;
            var factory = SetupFactory(endpoint);
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Interlocked.Increment(ref attempt) == 1 ? closed.Object : opened.Object);

            var cached = await builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);
            Assert.Same(closed.Object, cached);

            // The broker restarts and every consumer notices at once.
            isOpen = false;

            // Act
            var callers = Enumerable.Range(0, 15)
                .Select(_ => Task.Run(() => builder.GetConnectionAsync(
                    endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken)))
                .ToArray();
            var results = await Task.WhenAll(callers);

            // Assert
            Assert.All(results, result => Assert.Same(opened.Object, result));
            factory.Verify(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()), Times.Exactly(2));
            closed.Verify(t => t.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task GetConnection_ShouldNotBlock_WhenDisposingOldConnectionHangs()
        {
            // Arrange
            var endpoint = new RabbitMQEndpoint();

            var isOpen = true;
            var closed = new Mock<IConnection>();
            closed.SetupGet(t => t.IsOpen).Returns(() => isOpen);
            closed.Setup(t => t.DisposeAsync()).Returns(new ValueTask(new TaskCompletionSource().Task));

            var opened = OpenConnection();

            var attempt = 0;
            var factory = SetupFactory(endpoint);
            factory.Setup(t => t.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, ConnectionName, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Interlocked.Increment(ref attempt) == 1 ? closed.Object : opened.Object);

            await builder.GetConnectionAsync(
                endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken);
            isOpen = false;

            // Act
            var result = await AdvanceUntilCompletedAsync(
                builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer, 0, TestContext.Current.CancellationToken),
                ConnectionBuilder.DisposeTimeout);

            // Assert
            Assert.Same(opened.Object, result);
        }

        private static Mock<IConnection> OpenConnection()
        {
            var connection = new Mock<IConnection>();
            connection.SetupGet(t => t.IsOpen).Returns(true);
            return connection;
        }

        private Mock<IConnectionFactory> SetupFactory(RabbitMQEndpoint endpoint)
        {
            var factory = new Mock<IConnectionFactory>();

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.GetFactoryHash(endpoint, ConnectionMode.Consumer))
                .Returns("hash");

            mocker.GetMock<IConnectionFactoryBuilder>()
                .Setup(t => t.CreateConnectionFactory(endpoint))
                .Returns(factory.Object);

            return factory;
        }

        /// <summary>
        /// Drives the fake clock forward in steps until the task completes: the timers behind the
        /// builder's delays only exist once the code has reached them, so a single Advance is not enough.
        /// </summary>
        private async Task<TResult> AdvanceUntilCompletedAsync<TResult>(Task<TResult> task, TimeSpan step)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                timeProvider.Advance(step);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            return await task;
        }
    }
}
