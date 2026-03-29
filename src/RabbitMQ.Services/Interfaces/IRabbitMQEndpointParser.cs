using RabbitMQ.Services.Configurations;

namespace RabbitMQ.Services.Interfaces
{
    public interface IRabbitMQEndpointParser
    {
        IRabbitMQEndpoint Parse(string uri);
    }
}
