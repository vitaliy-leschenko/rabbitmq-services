namespace RabbitMQ.Services
{
    public class BaseMessage
    {
        public string TraceId { get; set; } = string.Empty;

        public string SpanId { get; set; } = string.Empty;
    }
}
