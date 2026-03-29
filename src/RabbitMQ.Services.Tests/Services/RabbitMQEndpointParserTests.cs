using System.Net;
using System.Web;
using Moq.AutoMock;
using RabbitMQ.Client;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    public class RabbitMQEndpointParserTests
    {
        private readonly AutoMocker mocker = new();
        private readonly RabbitMQEndpointParser parser;

        public RabbitMQEndpointParserTests()
        {
            parser = mocker.CreateInstance<RabbitMQEndpointParser>();
        }

        [Theory]
        [InlineData("amqp")]
        [InlineData("rabbitmq")]
        public void Parse_ShouldParseEndpoint_Default(string schema)
        {
            // Arrange
            var data = schema + "://rabbitmq.com/exchangeName";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal("rabbitmq.com", endpoint.Host);
            Assert.Equal(Protocols.DefaultProtocol.DefaultPort, endpoint.Port);
            Assert.Equal("/", endpoint.VirtualHost);
            Assert.Equal("exchangeName", endpoint.Queue.Name);
            Assert.Equal(string.Empty, endpoint.Queue.Routing);
            Assert.Equal("exchangeName", endpoint.Exchange.Name);
            Assert.Equal("fanout", endpoint.Exchange.Type);
        }

        [Fact]
        public void Parse_ShouldThrowNotSupportedException_AmqpOverTls()
        {
            // Arrange
            var data = "amqps://rabbitmq.com";

            // Act
            var act = () => parser.Parse(data);

            // Assert
            var ex = Assert.Throws<NotSupportedException>(() => act());
            Assert.Equal("AMQP over TLS is not supported", ex.Message);
        }

        [Fact]
        public void Parse_ShouldThrowInvalidOperationException_InvalidSchema()
        {
            // Arrange
            var data = "https://rabbitmq.com";

            // Act
            var act = () => parser.Parse(data);

            // Assert
            var ex = Assert.Throws<InvalidOperationException>(() => act());
            Assert.Equal("The invalid scheme was specified: https", ex.Message);
        }

        [Fact]
        public void Parse_ShouldThrowInvalidOperationException_InvalidUri()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/seg1/seg2/seg3";

            // Act
            var act = () => parser.Parse(data);

            // Assert
            var ex = Assert.Throws<InvalidOperationException>(() => act());
            Assert.Equal("Invalid uri", ex.Message);
        }

        [Fact]
        public void Parse_ShouldThrowInvalidOperationException_WhenHighAvailableAndTemporary()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/seg1?ha=true&temporary=true";

            // Act
            var act = () => parser.Parse(data);

            // Assert
            var ex = Assert.Throws<InvalidOperationException>(() => act());
            Assert.Equal("A highly available queue cannot be temporary", ex.Message);
        }

        [Fact]
        public void Parse_ShouldParseVirtualHost()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/vhost/exchangeName";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal("rabbitmq.com", endpoint.Host);
            Assert.Equal(Protocols.DefaultProtocol.DefaultPort, endpoint.Port);
            Assert.Equal("vhost", endpoint.VirtualHost);
            Assert.Equal("exchangeName", endpoint.Queue.Name);
            Assert.Equal(string.Empty, endpoint.Queue.Routing);
            Assert.Equal("exchangeName", endpoint.Exchange.Name);
            Assert.Equal("fanout", endpoint.Exchange.Type);
        }

        [Fact]
        public void Parse_ShouldParsePort()
        {
            // Arrange
            var data = "amqp://rabbitmq.com:10000/exchangeName";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(10000, endpoint.Port);
        }

        [Fact]
        public void Parse_ShouldParseUserName()
        {
            // Arrange
            var data = "amqp://name@rabbitmq.com/exchangeName";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal("name", endpoint.UserName);
            Assert.Equal("", endpoint.Password);
        }

        [Fact]
        public void Parse_ShouldParseUserNameAndPassword()
        {
            // Arrange
            var data = "amqp://name:pass@rabbitmq.com/exchangeName";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal("name", endpoint.UserName);
            Assert.Equal("pass", endpoint.Password);
        }

        [Fact]
        public void Parse_ShouldParseTemporary()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?temporary=true";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.False(endpoint.Queue.Durable);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Parse_ShouldParseExclusive(bool exclusive)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?exclusive=" + exclusive;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(exclusive, endpoint.Queue.Exclusive);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Parse_ShouldParseAutodelete(bool autodelete)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?autodelete=" + autodelete;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(autodelete, endpoint.Queue.AutoDelete);
        }

        [Fact]
        public void Parse_ShouldParseTtl()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?ttl=" + 42;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(42, endpoint.Queue.Arguments["x-message-ttl"]);
        }

        [Fact]
        public void Parse_ShouldParseHighAvailable()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?ha=true";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.True(endpoint.Queue.Arguments.ContainsKey("x-ha-policy"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Parse_ShouldParseDurable(bool durable)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?durable=" + durable;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(durable, endpoint.Queue.Durable);
        }

        [Theory]
        [InlineData("", 1)]
        [InlineData("0", 1)]
        [InlineData("-1", 1)]
        [InlineData("10", 10)]
        public void Parse_ShouldParsePrefetch(string input, int output)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?prefetch=" + input;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(output, endpoint.PrefetchCount);
        }

        [Theory]
        [InlineData("", 1)]
        [InlineData("0", 1)]
        [InlineData("-1", 1)]
        [InlineData("10", 10)]
        public void Parse_ShouldParseConsumers(string input, int output)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?consumers=" + input;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(output, endpoint.ConsumersCount);
        }

        [Fact]
        public void Parse_ShouldParseHeartbeat()
        {
            // Arrange
            var heartbeat = TimeSpan.FromSeconds(42);

            var data = "amqp://rabbitmq.com/exchangeName?heartbeat=" + heartbeat;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(heartbeat, endpoint.Heartbeat);
        }

        [Fact]
        public void Parse_ShouldParseDefaultAmqpTcpEndpoints()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName";

            var addresses = new[]
            {
                IPAddress.Parse("104.20.80.112"),
                IPAddress.Parse("172.67.32.161"),
                IPAddress.Parse("104.20.69.112")
            };
            mocker.GetMock<IDnsResolver>()
                .Setup(t => t.GetHostAddresses("rabbitmq.com"))
                .Returns(addresses);

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(addresses.Length, endpoint.AmqpTcpEndpoints.Count);
            foreach (var address in addresses)
            {
                var amqpTcpEndpoint = endpoint.AmqpTcpEndpoints.Single(t => t.HostName == address.ToString());
                Assert.Equal(Protocols.DefaultProtocol.DefaultPort, amqpTcpEndpoint.Port);
            }

            Assert.Equal("rabbitmq.com", endpoint.Host);
        }

        [Fact]
        public void Parse_ShouldParseAmqpTcpEndpoints_CustomHost()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?hosts=customhost";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            var host = endpoint.AmqpTcpEndpoints.Single();
            Assert.Equal("customhost", host.HostName);
            Assert.Equal(endpoint.Port, host.Port);
        }

        [Fact]
        public void Parse_ShouldParseAmqpTcpEndpoints_CustomHostAndPort()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?hosts=customhost:1000";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            var host = endpoint.AmqpTcpEndpoints.Single();
            Assert.Equal("customhost", host.HostName);
            Assert.Equal(1000, host.Port);
        }

        [Fact]
        public void Parse_ShouldParseAmqpTcpEndpoints_ManyHosts()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?hosts=host1:1001,host2:1002";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(2, endpoint.AmqpTcpEndpoints.Count);

            var host1 = endpoint.AmqpTcpEndpoints[0];
            Assert.Equal("host1", host1.HostName);
            Assert.Equal(1001, host1.Port);

            var host2 = endpoint.AmqpTcpEndpoints[1];
            Assert.Equal("host2", host2.HostName);
            Assert.Equal(1002, host2.Port);
        }

        [Fact]
        public void Parse_ShouldParseArguments()
        {
            // Arrange
            var data = "amqp://rabbitmq.com/exchangeName?=1&queue.qdata=qvalue&exchange.edata=evalue";

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.True(endpoint.Queue.Arguments.TryGetValue("qdata", out var qvalue) && (string?)qvalue == "qvalue");
            Assert.True(endpoint.Exchange.Arguments.TryGetValue("edata", out var evalue) && (string?)evalue == "evalue");
        }

        [Theory]
        [InlineData("", ExchangeType.Fanout)]
        [InlineData("any other string", ExchangeType.Fanout)]
        [InlineData("Fanout", ExchangeType.Fanout)]
        [InlineData("DIRECT", ExchangeType.Direct)]
        [InlineData("HeAdErS", ExchangeType.Headers)]
        [InlineData("topic", ExchangeType.Topic)]
        public void Parse_ShouldParseExhangeType(string type, string expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?exchangename=exchange&exchangetype=" + WebUtility.UrlEncode(type);

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(expected, endpoint.Exchange.Type);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsTransactionalQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?tx=" + value;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(expected, endpoint.Queue.IsTransactional);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsHighAvailableQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?ha=" + value;

            var uri = new Uri(data);
            var query = HttpUtility.ParseQueryString(uri.Query);

            // Act
            var result = RabbitMQEndpointParserHelpers.IsHighAvailableQueue(query);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsTemporaryQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?temporary=" + value;

            var uri = new Uri(data);
            var query = HttpUtility.ParseQueryString(uri.Query);

            // Act
            var result = RabbitMQEndpointParserHelpers.IsTemporaryQueue(query);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsDurableQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?durable=" + value;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(expected, endpoint.Queue.Durable);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsExclusiveQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?exclusive=" + value;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(expected, endpoint.Queue.Exclusive);
        }

        [Theory]
        [InlineData("unknown", false)]
        [InlineData("true", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        public void IsAutoDeleteQueue(string value, bool expected)
        {
            // Arrange
            var data = "amqp://rabbitmq.com/queue?autodelete=" + value;

            // Act
            var endpoint = parser.Parse(data);

            // Assert
            Assert.Equal(expected, endpoint.Queue.AutoDelete);
        }
    }
}
