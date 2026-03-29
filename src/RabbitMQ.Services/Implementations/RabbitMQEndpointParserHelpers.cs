using RabbitMQ.Client;
using RabbitMQ.Services.Configurations;
using RabbitMQ.Services.Interfaces;
using System.Collections.Specialized;

namespace RabbitMQ.Services.Implementations
{
    internal static class RabbitMQEndpointParserHelpers
    {
        public static void AddArguments(RabbitMQEndpoint endpoint, NameValueCollection query, bool isHighAvailable)
        {
            if (isHighAvailable)
            {
                endpoint.Queue.Arguments.Add("x-ha-policy", "all");
            }

            var ttl = GetTtl(query);
            if (ttl > 0)
            {
                endpoint.Queue.Arguments.Add("x-message-ttl", ttl);
            }

            foreach (var key in query.AllKeys)
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                var value = query[key]!;

                if (key.StartsWith("queue."))
                {
                    endpoint.Queue.Arguments.Add(key.Replace("queue.", ""), value);
                }

                if (key.StartsWith("exchange."))
                {
                    endpoint.Exchange.Arguments.Add(key.Replace("exchange.", ""), value);
                }
            }
        }

        public static List<AmqpTcpEndpoint> GetAmqpTcpEndpoints(IDnsResolver dnsResolver, NameValueCollection query, string defaultHost, int defaultPort)
        {
            if (query.Get("hosts") is string value)
            {
                var hosts = (
                    from item in value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    let parts = item.Split(':')
                    let host = parts[0]
                    let port = parts.Length > 1 ? int.Parse(parts[1]) : defaultPort
                    select new AmqpTcpEndpoint(host, port)).ToList();

                if (hosts.Count > 0)
                {
                    return hosts;
                }
            }

            var addresses = dnsResolver.GetHostAddresses(defaultHost);
            return addresses.Select(a => new AmqpTcpEndpoint(a.ToString(), defaultPort)).ToList();
        }

        public static int GetConsumersCount(NameValueCollection query)
        {
            if (query.Get("consumers") is string value && int.TryParse(value, out var prefetch))
            {
                return Math.Max(1, prefetch);
            }

            return 1;
        }

        public static string GetRouting(NameValueCollection query) => query.Get("route") ?? string.Empty;

        public static string GetExchangeName(string name, NameValueCollection query)
        {
            var result = query.Get("exchangename");
            return string.IsNullOrEmpty(result) ? name : result;
        }

        public static string GetExchangeType(NameValueCollection query)
        {
            var result = query.Get("exchangetype")?.ToLower() ?? ExchangeType.Fanout;
            if (!ExchangeType.All().Contains(result))
            {
                return ExchangeType.Fanout;
            }

            return result;
        }

        public static TimeSpan? GetHeartbeat(NameValueCollection query)
        {
            if (query.Get("heartbeat") is string value && TimeSpan.TryParse(value, out var heartbeat))
            {
                return heartbeat;
            }

            return null;
        }

        public static ushort GetPrefetchCount(NameValueCollection query)
        {
            if (query.Get("prefetch") is string value && ushort.TryParse(value, out var prefetch))
            {
                return Math.Max((ushort)1, prefetch);
            }

            return 1;
        }

        public static int GetTtl(NameValueCollection query) =>
            query.Get("ttl") is string value && int.TryParse(value, out var ttl) ? ttl : 0;

        public static (string UserName, string Password) GetUserInfo(Uri uri)
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                if (uri.UserInfo.Contains(':'))
                {
                    var parts = uri.UserInfo.Split(':');
                    return (parts[0], parts[1]);
                }
                else
                {
                    return (uri.UserInfo, string.Empty);
                }
            }

            return (string.Empty, string.Empty);
        }

        public static bool IsAutoDeleteQueue(NameValueCollection query, bool isTemporary)
        {
            if (query.Get("autodelete") is string value)
            {
                return (bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0);
            }
            else
            {
                return isTemporary;
            }
        }

        public static bool IsDurableQueue(NameValueCollection query, bool isTemporary)
        {
            if (query.Get("durable") is string value)
            {
                return (bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0);
            }
            else
            {
                return !isTemporary;
            }
        }

        public static bool IsExclusiveQueue(NameValueCollection query, bool isTemporary)
        {
            if (query.Get("exclusive") is string value)
            {
                return (bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0);
            }
            else
            {
                return isTemporary;
            }
        }

        public static bool IsHighAvailableQueue(NameValueCollection query) =>
            query.Get("ha") is string value &&
            ((bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0));

        public static bool IsTemporaryQueue(NameValueCollection query) =>
            query.Get("temporary") is string value &&
            ((bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0));

        public static bool IsTransactionalQueue(NameValueCollection query) =>
            query.Get("tx") is string value &&
            ((bool.TryParse(value, out var boolValue) && boolValue) || (int.TryParse(value, out var intValue) && intValue != 0));
    }
}
