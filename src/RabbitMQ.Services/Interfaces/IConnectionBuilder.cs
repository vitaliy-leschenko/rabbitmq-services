using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;

namespace RabbitMQ.Services.Interfaces
{
    public interface IConnectionBuilder
    {
        /// <summary>
        /// Returns an open connection for the endpoint, creating and caching one per
        /// <paramref name="connectionName"/>. Concurrent callers share a single connect attempt.
        /// The <paramref name="token"/> stops this caller from waiting; it does not abort the
        /// attempt for the others, which is bounded by its own timeout instead.
        /// </summary>
        Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint, string connectionName, ConnectionMode mode, int attempts = 5, CancellationToken token = default);
    }
}
