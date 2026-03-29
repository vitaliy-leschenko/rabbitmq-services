namespace RabbitMQ.Services.Settings
{
    public class ConsumerConfiguration<T> where T : class
    {
        public string ConnectionName { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public int MaxRetryCount { get; set; } = 3;

        public int? MaxDelays { get; set; }

        public int? DelayInSeconds { get; set; }

        public bool BindQueue { get; set; } = true;
    }
}
