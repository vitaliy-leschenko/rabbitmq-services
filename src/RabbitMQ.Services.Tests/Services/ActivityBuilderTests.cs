using System.Diagnostics;
using Moq.AutoMock;
using RabbitMQ.Services.Implementations;
using Xunit;

namespace RabbitMQ.Services.Tests.Services
{
    file class TestMessage : BaseMessage
    {
    }

    public class ActivityBuilderTests
    {
        private readonly AutoMocker mocker = new();
        private readonly ActivityBuilder activityBuilder;

        public ActivityBuilderTests()
        {
            activityBuilder = mocker.CreateInstance<ActivityBuilder>();
        }

        [Fact]
        public void StartNewChildActivity_CreatesChildActivity()
        {
            // Arrange
            using var parentActivity = new Activity("parent").Start();
            var message = new TestMessage
            {
                TraceId = parentActivity.TraceId.ToString(),
                SpanId = parentActivity.SpanId.ToString()
            };

            // Act
            using var child = activityBuilder.StartNewChildActivity<ActivityBuilderTests>(message);

            // Assert
            Assert.Equal(parentActivity.TraceId, child.TraceId);
            Assert.NotEqual(parentActivity.SpanId, child.SpanId);
        }

        [Fact]
        public void StartNewChildActivity_CreatesNewActivity()
        {
            // Arrange
            var message = new TestMessage
            {
                TraceId = Guid.NewGuid().ToString(),
                SpanId = Guid.NewGuid().ToString()
            };

            // Act
            using var child = activityBuilder.StartNewChildActivity<ActivityBuilderTests>(message);

            // Assert
            Assert.NotNull(child);
            Assert.Equal(Activity.Current, child);
        }
    }
}
