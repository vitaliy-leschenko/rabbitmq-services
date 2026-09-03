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
            Assert.Equal(parentActivity.SpanId, child.ParentSpanId);
            Assert.True(child.Recorded);
            Assert.Equal(Activity.Current, child);
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
            Assert.True(child.Recorded);
            Assert.Equal(Activity.Current, child);
        }

        [Fact]
        public void StartNewChildActivity_UsesTheActivitySource_WhenListened()
        {
            // Arrange: a listener like the one a tracer installs, sampling everything from the source.
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == ActivityBuilder.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            };
            ActivitySource.AddActivityListener(listener);

            var parentTraceId = ActivityTraceId.CreateRandom();
            var parentSpanId = ActivitySpanId.CreateRandom();
            var message = new TestMessage
            {
                TraceId = parentTraceId.ToString(),
                SpanId = parentSpanId.ToString()
            };

            // Act
            using var child = activityBuilder.StartNewChildActivity<ActivityBuilderTests>(message);

            // Assert
            Assert.Equal(ActivityBuilder.ActivitySourceName, child.Source.Name);
            Assert.Equal(ActivityKind.Consumer, child.Kind);
            Assert.Equal(parentTraceId, child.TraceId);
            Assert.Equal(parentSpanId, child.ParentSpanId);
            Assert.True(child.HasRemoteParent);
            Assert.True(child.Recorded);
            Assert.True(child.IsAllDataRequested);
            Assert.Equal(Activity.Current, child);
        }
    }
}
