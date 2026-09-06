using RabbitMQ.Services.Implementations;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    public class UriMaskerTests
    {
        private readonly UriMasker masker = new();

        [Theory]
        [InlineData("amqp://user:pass@localhost/vhost/queue", "amqp://***:***@localhost/vhost/queue")]
        [InlineData("rabbitmq://user:pass@localhost:5672/queue?consumers=4", "rabbitmq://***:***@localhost:5672/queue?consumers=4")]
        [InlineData("amqp://user:p%40ss@localhost/queue", "amqp://***:***@localhost/queue")]
        [InlineData("amqp://user:@localhost/queue", "amqp://***:***@localhost/queue")]
        [InlineData("amqp://user@localhost/queue", "amqp://***@localhost/queue")]
        [InlineData("amqp://user:pass@localhost/queue?route=a@b", "amqp://***:***@localhost/queue?route=a@b")]
        public void Mask_ShouldHideCredentials(string uri, string expected)
        {
            // Act
            var result = masker.Mask(uri);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("amqp://localhost/vhost/queue")]
        [InlineData("amqp://localhost/queue?route=a@b")]
        [InlineData("not a uri")]
        [InlineData("")]
        public void Mask_ShouldReturnInputUnchanged_WhenNoCredentials(string uri)
        {
            // Act
            var result = masker.Mask(uri);

            // Assert
            Assert.Equal(uri, result);
        }

        [Fact]
        public void Mask_ShouldReturnEmptyString_WhenNull()
        {
            // Act
            var result = masker.Mask(null);

            // Assert
            Assert.Equal(string.Empty, result);
        }
    }
}
