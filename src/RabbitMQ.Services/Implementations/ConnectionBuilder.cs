using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace RabbitMQ.Services.Implementations
{
    public sealed class ConnectionBuilder(
        IConnectionFactoryBuilder factoryBuilder,
        ILogger<ConnectionBuilder> logger) : IConnectionBuilder, IDisposable
    {
        private readonly SemaphoreSlim connectionsSemaphore = new(1, 1);
        private readonly ConcurrentDictionary<string, IConnection> connections = new();
        private readonly ConcurrentDictionary<string, IConnectionFactory> factories = new();
        private readonly IConnectionFactoryBuilder factoryBuilder = factoryBuilder;
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
            logger.LogDebug("[{threadId}] getting a connection for '{uri}'",
                Environment.CurrentManagedThreadId, endpoint.Uri);

            var factoryKey = factoryBuilder.GetFactoryHash(endpoint, mode);
            var connectionKey = connectionName + "|" + factoryKey;

            var attemptCounter = attempts;
            while (attempts == 0 || attemptCounter-- > 0)
            {
                var connection = await GetConnectionAsync();
                if (connection.IsOpen)
                {
                    logger.LogDebug("[{threadId}] the connection for '{uri}' has been created.",
                        Environment.CurrentManagedThreadId, endpoint.Uri);

                    return connection;
                }

                await connectionsSemaphore.WaitAsync();
                try
                {
                    logger.LogWarning("[{threadId}] closing the connection because: {reason}",
                        Environment.CurrentManagedThreadId, connection.CloseReason);

                    connections.Remove(connectionKey, out var _);
                    await connection.DisposeAsync();

                    logger.LogWarning("[{threadId}] getting a new connection for '{uri}'",
                        Environment.CurrentManagedThreadId, endpoint.Uri);
                }
                finally
                {
                    connectionsSemaphore.Release();
                }
            }

            throw new InvalidOperationException($"Can't open connection to {endpoint.Uri}");

            async Task<IConnection> GetConnectionAsync()
            {
                if (!connections.TryGetValue(connectionKey, out var connection))
                {
                    await connectionsSemaphore.WaitAsync();
                    try
                    {
                        if (!connections.TryGetValue(connectionKey, out connection))
                        {
                            logger.LogDebug("[{threadId}] looking for the connection factory '{factoryKey}'",
                                Environment.CurrentManagedThreadId, factoryKey);
                            var factory = factories.GetOrAdd(factoryKey, _ =>
                            {
                                logger.LogDebug("[{threadId}] creating a new connection factory '{factoryKey}'",
                                    Environment.CurrentManagedThreadId, factoryKey);
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
