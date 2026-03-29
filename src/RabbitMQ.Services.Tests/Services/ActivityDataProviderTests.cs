using System.Diagnostics;
using RabbitMQ.Services.Implementations;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    file class TestMessage : BaseMessage
    {
    }

    public class ActivityDataProviderTests
    {
        private readonly ActivityDataProvider provider = new();

        [Fact]
        public void SetActivityData_SetTraceIdAndSpanId_WhenActivityExists()
        {
            // Arrange
            using var activity = new Activity("").Start();
            var message = new TestMessage();

            // Act
            provider.SetActivityData(message);

            // Assert
            Assert.NotEmpty(message.TraceId);
            Assert.NotEmpty(message.SpanId);
        }

        [Fact]
        public void SetActivityData_SetTraceIdAndSpanId_WhenActivityDoesNotExist()
        {
            // Arrange
            Activity.Current = null;
            var message = new TestMessage();

            // Act
            provider.SetActivityData(message);

            // Assert
            Assert.Empty(message.TraceId);
            Assert.Empty(message.SpanId);
        }
    }
}
