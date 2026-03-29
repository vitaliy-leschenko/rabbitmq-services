using RabbitMQ.Services.Interfaces;
using System.Diagnostics;

namespace RabbitMQ.Services.Implementations
{
    internal class ActivityDataProvider : IActivityDataProvider
    {
        public void SetActivityData(BaseMessage message)
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                message.TraceId = activity.TraceId.ToString();
                message.SpanId = activity.SpanId.ToString();
            }
        }
    }
}
