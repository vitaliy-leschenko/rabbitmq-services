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
        TimeProvider timeProvider,
        ILogger<ConnectionBuilder> logger) : IConnectionBuilder, IDisposable
    {
        /// <summary>
        /// Upper bound for one connect attempt. The client caps the TCP connect at 30 seconds per
        /// endpoint but not the AMQP handshake: a broker that already accepts TCP while still
        /// starting up leaves CreateConnectionAsync waiting for connection.start until cancelled.
        /// </summary>
        internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Upper bound for disposing a dead connection; the client aborts an open one within 5 seconds.
        /// </summary>
        internal static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(10);

        internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

        // One connect task per key: concurrent callers wait for its result instead of queueing
        // behind a lock that would be held for the whole network round trip.
        private readonly ConcurrentDictionary<string, Lazy<Task<IConnection>>> connections = new();
        private readonly ConcurrentDictionary<string, IConnectionFactory> factories = new();
        private readonly IConnectionFactoryBuilder factoryBuilder = factoryBuilder;
        private readonly IUriMasker uriMasker = uriMasker;
        private readonly TimeProvider timeProvider = timeProvider;
        private readonly ILogger<ConnectionBuilder> logger = logger;

        public void Dispose()
        {
            foreach (var entry in connections.Values)
            {
                if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
                {
                    entry.Value.Result.Dispose();
                }
            }

            connections.Clear();
        }

        public async Task<IConnection> GetConnectionAsync(IRabbitMQEndpoint endpoint, string connectionName, ConnectionMode mode, int attempts = 5, CancellationToken token = default)
        {
            var maskedUri = uriMasker.Mask(endpoint.Uri);

            logger.LogDebug("[{threadId}] getting a connection for '{uri}'",
                Environment.CurrentManagedThreadId, maskedUri);

            var factoryKey = factoryBuilder.GetFactoryHash(endpoint, mode);
            var connectionKey = connectionName + "|" + factoryKey;

            var attemptCounter = attempts;
            while (attempts == 0 || attemptCounter-- > 0)
            {
                token.ThrowIfCancellationRequested();

                Lazy<Task<IConnection>> mine = null!;
                mine = new Lazy<Task<IConnection>>(() => ConnectAsync(endpoint, connectionName, connectionKey, factoryKey, maskedUri, mine));
                var entry = connections.GetOrAdd(connectionKey, mine);

                var connection = await entry.Value.WaitAsync(token);
                if (connection.IsOpen)
                {
                    logger.LogDebug("[{threadId}] the connection for '{uri}' has been created.",
                        Environment.CurrentManagedThreadId, maskedUri);

                    return connection;
                }

                // Remove by key and value: when several consumers recover from the same broker
                // restart at once, only the first one evicts and disposes the dead connection; the
                // others just loop and pick up the replacement.
                if (connections.TryRemove(new KeyValuePair<string, Lazy<Task<IConnection>>>(connectionKey, entry)))
                {
                    logger.LogWarning("[{threadId}] closing the connection because: {reason}",
                        Environment.CurrentManagedThreadId, connection.CloseReason);

                    await DisposeConnectionAsync(connection, maskedUri);

                    logger.LogWarning("[{threadId}] getting a new connection for '{uri}'",
                        Environment.CurrentManagedThreadId, maskedUri);
                }

                if (ReferenceEquals(entry, mine))
                {
                    // Our own fresh connection came back closed: give the broker a moment instead
                    // of spinning on it, which matters when attempts is unlimited.
                    await Task.Delay(RetryDelay, timeProvider, token);
                }
            }

            throw new InvalidOperationException($"Can't open connection to {maskedUri}");
        }

        private async Task<IConnection> ConnectAsync(
            IRabbitMQEndpoint endpoint,
            string connectionName,
            string connectionKey,
            string factoryKey,
            string maskedUri,
            Lazy<Task<IConnection>> self)
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

            // The attempt runs on its own deadline rather than on the token of whoever asked first:
            // that caller may give up while the others still need the result.
            using var timeout = new CancellationTokenSource(ConnectTimeout, timeProvider);
            try
            {
                return await factory.CreateConnectionAsync(endpoint.AmqpTcpEndpoints, connectionName, timeout.Token);
            }
            catch (Exception ex)
            {
                // Evict here rather than in the caller: every waiter may have been cancelled already,
                // and a cached failure would be handed out forever.
                connections.TryRemove(new KeyValuePair<string, Lazy<Task<IConnection>>>(connectionKey, self));

                if (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException($"Connecting to {maskedUri} did not complete within {ConnectTimeout}", ex);
                }

                throw;
            }
        }

        private async Task DisposeConnectionAsync(IConnection connection, string maskedUri)
        {
            try
            {
                await connection.DisposeAsync().AsTask().WaitAsync(DisposeTimeout, timeProvider);
            }
            catch (Exception ex)
            {
                // A dead connection that will not close is abandoned, not waited for.
                logger.LogWarning(ex, "[{threadId}] disposing the closed connection for '{uri}' failed: {message}",
                    Environment.CurrentManagedThreadId, maskedUri, ex.Message);
            }
        }
    }
}
