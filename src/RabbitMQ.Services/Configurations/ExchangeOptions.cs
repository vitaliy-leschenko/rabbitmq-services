using RabbitMQ.Client;
using System.Collections.ObjectModel;

namespace RabbitMQ.Services.Configurations
{
    internal sealed class ExchangeOptions : IExchangeOptions
    {
        public string Type { get; set; } = ExchangeType.Fanout;

        public string Name { get; set; } = string.Empty;

        public bool Durable { get; set; } = true;

        public bool AutoDelete { get; set; } = false;

        public IDictionary<string, object?> Arguments { get; set; } = new Dictionary<string, object?>();

        IReadOnlyDictionary<string, object?> IExchangeOptions.Arguments => new ReadOnlyDictionary<string, object?>(Arguments);
    }
}
