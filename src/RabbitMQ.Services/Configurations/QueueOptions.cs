using System.Collections.ObjectModel;

namespace RabbitMQ.Services.Configurations
{
    internal sealed class QueueOptions : IQueueOptions
    {
        public string Name { get; set; } = string.Empty;

        public string Routing { get; set; } = string.Empty;

        public bool Exclusive { get; set; } = false;

        public bool Durable { get; set; } = true;

        public bool AutoDelete { get; set; } = false;

        public bool IsTransactional { get; set; } = false;

        public IDictionary<string, object?> Arguments { get; set; } = new Dictionary<string, object?>();

        IReadOnlyDictionary<string, object?> IQueueOptions.Arguments => new ReadOnlyDictionary<string, object?>(Arguments);
    }
}
