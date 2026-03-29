using System.Net;

namespace RabbitMQ.Services.Interfaces
{
    public interface IDnsResolver
    {
        IPAddress[] GetHostAddresses(string hostNameOrAddress);
    }
}
