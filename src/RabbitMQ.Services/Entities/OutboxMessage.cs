namespace RabbitMQ.Services.Entities
{
    public class OutboxMessage
    {
        public long MessageId { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public string Uri { get; set; } = string.Empty;

        public bool BindQueue { get; set; }

        public byte[] Body { get; set; } = default!;

        public string ContentType { get; set; } = string.Empty;

        public string Namespace { get; set; } = string.Empty;
    }
}
