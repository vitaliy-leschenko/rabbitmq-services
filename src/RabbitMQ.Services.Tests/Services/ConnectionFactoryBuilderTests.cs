using Moq;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    public class ConnectionFactoryBuilderTests
    {
        private readonly AutoMocker mocker = new();
        private readonly ConnectionFactoryBuilder builder;

        public ConnectionFactoryBuilderTests()
        {
            builder = mocker.CreateInstance<ConnectionFactoryBuilder>();
        }

        [Fact]
        public void CreateConnectionFactory()
        {
            // Arrange
            var endpoint = new Mock<IRabbitMQEndpoint>();
            endpoint.SetupGet(t => t.Host).Returns("localhost");
            endpoint.SetupGet(t => t.Port).Returns(-1);
            endpoint.SetupGet(t => t.UserName).Returns("username");
            endpoint.SetupGet(t => t.Password).Returns("password");
            endpoint.SetupGet(t => t.VirtualHost).Returns("vhost");
            endpoint.SetupGet(t => t.Heartbeat).Returns((TimeSpan?)null);

            // Act
            var factory = builder.CreateConnectionFactory(endpoint.Object) as ConnectionFactory;

            // Assert
            Assert.NotNull(factory);
            endpoint.VerifyAll();

            Assert.Equal("amqp://localhost:5672", factory.Endpoint.ToString());
            Assert.Equal("localhost", factory.HostName);
            Assert.Equal("vhost", factory.VirtualHost);
            Assert.Equal("username", factory.UserName);
            Assert.Equal("password", factory.Password);
            Assert.False(factory.AutomaticRecoveryEnabled);
            Assert.Equal(ConnectionFactory.DefaultHeartbeat, factory.RequestedHeartbeat);
        }

        [Fact]
        public void CreateConnectionFactory_ShouldUseCustomHeartbeat()
        {
            // Arrange
            var heartbeat = TimeSpan.FromSeconds(15);

            var endpoint = new Mock<IRabbitMQEndpoint>();
            endpoint.SetupGet(t => t.Host).Returns("localhost");
            endpoint.SetupGet(t => t.Port).Returns(-1);
            endpoint.SetupGet(t => t.UserName).Returns("username");
            endpoint.SetupGet(t => t.Password).Returns("password");
            endpoint.SetupGet(t => t.VirtualHost).Returns("vhost");
            endpoint.SetupGet(t => t.Heartbeat).Returns(heartbeat);

            // Act
            var factory = builder.CreateConnectionFactory(endpoint.Object) as ConnectionFactory;

            // Assert
            Assert.NotNull(factory);
            endpoint.VerifyAll();

            Assert.Equal("amqp://localhost:5672", factory.Endpoint.ToString());
            Assert.Equal("localhost", factory.HostName);
            Assert.Equal("vhost", factory.VirtualHost);
            Assert.Equal("username", factory.UserName);
            Assert.Equal("password", factory.Password);
            Assert.False(factory.AutomaticRecoveryEnabled);
            Assert.Equal(heartbeat, factory.RequestedHeartbeat);
        }

        [Fact]
        public void GetFactoryHash_ShouldUsePrefetchCount_ConsumerMode()
        {
            // Arrange
            var endpoint = new Mock<IRabbitMQEndpoint>();
            endpoint.SetupGet(t => t.Host).Returns("localhost");
            endpoint.SetupGet(t => t.Port).Returns(5672);
            endpoint.SetupGet(t => t.UserName).Returns("username");
            endpoint.SetupGet(t => t.Password).Returns("password");
            endpoint.SetupGet(t => t.VirtualHost).Returns("vhost");

            // Act
            var hash = builder.GetFactoryHash(endpoint.Object, ConnectionMode.Consumer);

            // Assert
            Assert.Equal("localhost|5672|username|password|vhost", hash);

            endpoint.VerifyGet(t => t.Host);
            endpoint.VerifyGet(t => t.Port);
            endpoint.VerifyGet(t => t.UserName);
            endpoint.VerifyGet(t => t.Password);
            endpoint.VerifyGet(t => t.VirtualHost);
        }

        [Fact]
        public void GetFactoryHash_ShouldUsePrefetchCount_ProducerMode()
        {
            // Arrange
            var endpoint = new Mock<IRabbitMQEndpoint>();
            endpoint.SetupGet(t => t.Host).Returns("localhost");
            endpoint.SetupGet(t => t.Port).Returns(5672);
            endpoint.SetupGet(t => t.UserName).Returns("username");
            endpoint.SetupGet(t => t.Password).Returns("password");
            endpoint.SetupGet(t => t.VirtualHost).Returns("vhost");
            endpoint.SetupGet(t => t.PrefetchCount).Returns(4);

            // Act
            var hash = builder.GetFactoryHash(endpoint.Object, ConnectionMode.Producer);

            // Assert
            Assert.Equal("localhost|5672|username|password|vhost", hash);

            endpoint.VerifyGet(t => t.Host);
            endpoint.VerifyGet(t => t.Port);
            endpoint.VerifyGet(t => t.UserName);
            endpoint.VerifyGet(t => t.Password);
            endpoint.VerifyGet(t => t.VirtualHost);
            endpoint.VerifyGet(t => t.PrefetchCount, Times.Never);
        }

        [Fact]
        public void GetFactoryHash_ShouldUseDefaultPort()
        {
            // Arrange
            var endpoint = new Mock<IRabbitMQEndpoint>();
            endpoint.SetupGet(t => t.Host).Returns("localhost");
            endpoint.SetupGet(t => t.Port).Returns(-1);
            endpoint.SetupGet(t => t.UserName).Returns("username");
            endpoint.SetupGet(t => t.Password).Returns("password");
            endpoint.SetupGet(t => t.VirtualHost).Returns("vhost");

            // Act
            var hash = builder.GetFactoryHash(endpoint.Object, ConnectionMode.Consumer);

            // Assert
            Assert.Equal("localhost|5672|username|password|vhost", hash);

            endpoint.VerifyGet(t => t.Host);
            endpoint.VerifyGet(t => t.Port);
            endpoint.VerifyGet(t => t.UserName);
            endpoint.VerifyGet(t => t.Password);
            endpoint.VerifyGet(t => t.VirtualHost);
        }
    }
}
