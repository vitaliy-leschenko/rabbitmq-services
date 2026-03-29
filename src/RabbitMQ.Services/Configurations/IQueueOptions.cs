namespace RabbitMQ.Services.Configurations
{
    public interface IQueueOptions
    {
        bool Exclusive { get; }

        string Name { get; }

        string Routing { get; }

        bool Durable { get; }

        bool AutoDelete { get; }

        bool IsTransactional { get; }

        IReadOnlyDictionary<string, object?> Arguments { get; }
    }
}
