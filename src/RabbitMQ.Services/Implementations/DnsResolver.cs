using RabbitMQ.Services.Interfaces;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace RabbitMQ.Services.Implementations
{
    [ExcludeFromCodeCoverage]
    public sealed class DnsResolver : IDnsResolver
    {
        public IPAddress[] GetHostAddresses(string hostNameOrAddress)
        {
            return Dns.GetHostAddresses(hostNameOrAddress);
        }
    }
}
