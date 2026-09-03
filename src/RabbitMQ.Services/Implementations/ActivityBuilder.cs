using Microsoft.Extensions.Logging;
using RabbitMQ.Services.Interfaces;
using System.Diagnostics;

namespace RabbitMQ.Services.Implementations
{
    /// <summary>
    /// Opens the consumer span of a message. The span is created through the <see cref="ActivitySource"/>
    /// named <see cref="ActivitySourceName"/>, with the publisher's trace context restored from the message
    /// as a remote, recorded parent, so that a tracer subscribed to the source (for example OpenTelemetry via
    /// <c>AddSource(ActivityBuilder.ActivitySourceName)</c>) exports it as a continuation of the publisher's
    /// trace and samples everything created under it.
    /// </summary>
    public class ActivityBuilder(ILogger<ActivityBuilder> logger) : IActivityBuilder
    {
        /// <summary>
        /// The name of the <see cref="System.Diagnostics.ActivitySource"/> the consumer spans are created from.
        /// </summary>
        public const string ActivitySourceName = "RabbitMQ.Services";

        /// <summary>
        /// The <see cref="System.Diagnostics.ActivitySource"/> the consumer spans are created from.
        /// </summary>
        public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

        private readonly ILogger<ActivityBuilder> logger = logger;

        public Activity StartNewChildActivity<T>(BaseMessage message) where T : class
        {
            var name = typeof(T).Name;
            var parent = GetParentContext(message);

            var activity = parent == default
                ? ActivitySource.StartActivity(name, ActivityKind.Consumer)
                : ActivitySource.StartActivity(name, ActivityKind.Consumer, parent);

            if (activity is not null)
            {
                return activity;
            }

            // Nobody listens to the source (no tracer, or the source is not subscribed): keep an Activity
            // current anyway so that logs can be correlated, and mark it recorded so that a tracer which
            // listens to other sources (HttpClient, database drivers) still samples the spans under it.
            activity = new Activity(name);
            if (parent == default)
            {
                activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
            }
            else
            {
                activity.SetParentId(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded);
            }

            return activity.Start();
        }

        private ActivityContext GetParentContext(BaseMessage message)
        {
            if (string.IsNullOrEmpty(message.TraceId) || string.IsNullOrEmpty(message.SpanId))
            {
                return default;
            }

            try
            {
                return new ActivityContext(
                    ActivityTraceId.CreateFromString(message.TraceId.AsSpan()),
                    ActivitySpanId.CreateFromString(message.SpanId.AsSpan()),
                    ActivityTraceFlags.Recorded,
                    isRemote: true);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                logger.LogWarning(ex, "Can't parse traceId and spanId");
                return default;
            }
        }
    }
}
