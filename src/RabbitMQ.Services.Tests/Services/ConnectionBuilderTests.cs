using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    public class ConnectionBuilderTests
    {
        private readonly AutoMocker mocker = new();
        private readonly ConnectionBuilder builder;

        public ConnectionBuilderTests()
        {
            builder = mocker.CreateInstance<ConnectionBuilder>();
        }

        [Fact]
        public async Task GetConnection_ShouldCreateNewConnectionAsync()
        {
            // Arrange
            const string ConnectionName = "connectionName";

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
            const string ConnectionName = "connectionName";

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
            const string ConnectionName = "connectionName";

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
            var result = await builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);

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
            const string ConnectionName = "connectionName";

            var endpoint = new RabbitMQEndpoint
            {
                Uri = "amqp://localhost/vhost/queue"
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
            Task<IConnection> act() => builder.GetConnectionAsync(endpoint, ConnectionName, ConnectionMode.Consumer);

            // Assert
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => act());
            Assert.Equal($"Can't open connection to {endpoint.Uri}", ex.Message);
        }

        [Fact]
        public async Task Dispose_ShouldDisposeConnectionsAsync()
        {
            // Arrange
            const string ConnectionName = "connectionName";

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
    }
}
