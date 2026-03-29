using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Services.Tests.Persistence;

namespace RabbitMQ.Services.Tests
{
    public sealed class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            var cultureInfo = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
            CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

            services.AddLogging();
            services.AddSingleton(services);

            var config = new ConfigurationBuilder().Build();
            services.AddOutboxPersistence(config);
        }
    }
}
