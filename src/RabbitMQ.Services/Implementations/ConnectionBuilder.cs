using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;
using System.Collections.Concurrent;

namespace RabbitMQ.Services.Implementations
{
    public sealed class ConnectionBuilder(
        IConnectionFactoryBuilder factoryBuilder,
        IUriMasker uriMasker,
        ILogger<ConnectionBuilder> logger) : IConnectionBuilder, IDisposable
    {
        private readonly SemaphoreSlim connectionsSemaphore = new(1, 1);
        private readonly ConcurrentDictionary<string, IConnection> connections = new();
        private readonly ConcurrentDictionary<string, IConnectionFactory> factories = new();
        private readonly IConnectionFactoryBuilder factoryBuilder = factoryBuilder;
        private readonly IUriMasker uriMasker = uriMasker;
        private readonly ILogger<ConnectionBuilder> logger = logger;

        public void Dispose()
        {
            foreach (var connection in connections.Values)
            {
                connection.Dispose();
            }

            connections.Clear();
        }

        public async Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint, string connectionName, ConnectionMode mode, int attempts = 5)
        {
            var maskedUri = uriMasker.Mask(endpoint.Uri);

            logger.LogDebug("[{threadId}] getting a connection for '{uri}'",
                Environment.CurrentManagedThreadId, maskedUri);

            var factoryKey = factoryBuilder.GetFactoryHash(endpoint, mode);
            var connectionKey = connectionName + "|" + factoryKey;

            var attemptCounter = attempts;
            while (attempts == 0 || attemptCounter-- > 0)
            {
                var connection = await GetConnectionAsync();
                if (connection.IsOpen)
                {
                    logger.LogDebug("[{threadId}] the connection for '{uri}' has been created.",
                        Environment.CurrentManagedThreadId, maskedUri);

                    return connection;
                }

                await connectionsSemaphore.WaitAsync();
                try
                {
                    logger.LogWarning("[{threadId}] closing the connection because: {reason}",
                        Environment.CurrentManagedThreadId, connection.CloseReason);

                    // Remove by key and value: when several consumers recover from the same broker
                    // restart at once, a plain remove by key would evict the fresh connection that
                    // another one has just cached, leaking it out of the dictionary.
                    connections.TryRemove(new KeyValuePair<string, IConnection>(connectionKey, connection));
                    await connection.DisposeAsync();

                    logger.LogWarning("[{threadId}] getting a new connection for '{uri}'",
                        Environment.CurrentManagedThreadId, maskedUri);
                }
                finally
                {
                    connectionsSemaphore.Release();
                }
            }

            throw new InvalidOperationException($"Can't open connection to {maskedUri}");

            async Task<IConnection> GetConnectionAsync()
            {
                if (!connections.TryGetValue(connectionKey, out var connection))
                {
                    await connectionsSemaphore.WaitAsync();
                    try
                    {
                        if (!connections.TryGetValue(connectionKey, out connection))
                        {
                            logger.LogDebug("[{threadId}] looking for the connection factory for '{uri}'",
                                Environment.CurrentManagedThreadId, maskedUri);
                            var factory = factories.GetOrAdd(factoryKey, _ =>
                            {
                                logger.LogDebug("[{threadId}] creating a new connection factory for '{uri}'",
                                    Environment.CurrentManagedThreadId, maskedUri);
                                return factoryBuilder.CreateConnectionFactory(endpoint);
                            });

                            logger.LogDebug("[{threadId}] creating a new connection '{connectionName}'",
                                Environment.CurrentManagedThreadId, connectionName);
                            connection = await factory.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, connectionName);

                            connections.TryAdd(connectionKey, connection);
                        }
                    }
                    finally
                    {
                        connectionsSemaphore.Release();
                    }
                }

                return connection;
            }
        }
    }
}
