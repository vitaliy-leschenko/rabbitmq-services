namespace RabbitMQ.Services.Configurations
{
    public interface IExchangeOptions
    {
        string Type { get; }

        string Name { get; }

        bool Durable { get; }

        bool AutoDelete { get; }

        IReadOnlyDictionary<string, object?> Arguments { get; }
    }
}
