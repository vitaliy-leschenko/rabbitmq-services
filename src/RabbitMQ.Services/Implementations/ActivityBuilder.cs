using Microsoft.Extensions.Logging;
using RabbitMQ.Services.Interfaces;
using System.Diagnostics;

namespace RabbitMQ.Services.Implementations
{
    public class ActivityBuilder(ILogger<ActivityBuilder> logger) : IActivityBuilder
    {
        private readonly ILogger<ActivityBuilder> logger = logger;

        public Activity StartNewChildActivity<T>(BaseMessage message) where T : class
        {
            var activity = new Activity(typeof(T).Name);

            try
            {
                if (!string.IsNullOrEmpty(message.TraceId) && !string.IsNullOrEmpty(message.SpanId))
                {
                    activity.SetParentId(ActivityTraceId.CreateFromString(message.TraceId.AsSpan()), ActivitySpanId.CreateFromString(message.SpanId.AsSpan()));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Can't parse traceId and spanId");
            }

            return activity.Start();
        }
    }
}
