namespace RabbitMQ.Services.Settings
{
    public class OutboxOptions
    {
        public string ConnectionName { get; set; } = string.Empty;

        public string Namespace { get; set; } = string.Empty;
    }
}
