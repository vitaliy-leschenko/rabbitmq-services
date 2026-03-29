using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Moq;
using Moq.AutoMock;
using RabbitMQ.Services.Settings;
using Xunit;

namespace RabbitMQ.Services.Tests.Consumer
{
    public class BaseMessageHandlerTests
    {
        public class TestMessage
        {
            [Required]
            public int Value { get; set; }
        }

        private readonly AutoMocker mocker = new();
        private readonly Mock<BaseMessageHandler<TestMessage>> mock;
        private readonly BaseMessageHandler<TestMessage> handler;
        private readonly ConsumerConfiguration<TestMessage> options;

        public BaseMessageHandlerTests()
        {
            options = new ConsumerConfiguration<TestMessage>();
            mocker.Use(Options.Create(options));

            mock = mocker.GetMock<BaseMessageHandler<TestMessage>>();
            mock.CallBase = true;

            handler = mock.Object;
        }

        [Fact]
        public void GetMessage()
        {
            // Arrange
            var data = @"{ ""value"": 42 }";

            // Act
            var result = handler.GetMessage(data);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public void GetMessage2()
        {
            // Act && Assert
            var result = Assert.Throws<JsonException>(() => handler.GetMessage("null"));

            // Assert
            Assert.Equal("Can't deserialize object", result.Message);
        }

        [Fact]
        public async Task HandleAsync_ShouldHaveDefualtImplementation()
        {
            // Arrange
            var message = new TestMessage
            {
                Value = 42
            };

            // Act
            await handler.HandleAsync(message, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task HandleAsyncWithCount_ShouldHaveDefualtImplementation()
        {
            // Arrange
            var message = new TestMessage
            {
                Value = 42
            };

            // Act
            await handler.HandleAsync(message, 42, TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task HandleAsyncWithDelayCount_ShouldHaveDefualtImplementation()
        {
            // Arrange
            var message = new TestMessage
            {
                Value = 42
            };

            // Act
            await handler.HandleAsync(message, 420, 69, TestContext.Current.CancellationToken);
        }
    }
}
