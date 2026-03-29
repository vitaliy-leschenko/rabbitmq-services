using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Services.Settings;
using RabbitMQ.Services.Tests.Persistence;
using Xunit;

namespace RabbitMQ.Services.Tests
{
    public class DependencyInjectionTests
    {
        [Fact]
        public void SetupOutboxOptions_GetValuesFromCustomCallback()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();

            var services = new ServiceCollection();
            services.AddRabbitMQ().AddOutboxServices<TestDbContext>(config, options =>
            {
                options.Namespace = "ns";
                options.ConnectionName = "cn";
            });

            var provider = services.BuildServiceProvider();
            var opt = provider.GetRequiredService<IOptions<OutboxOptions>>();

            // Act
            Assert.Equal("ns", opt.Value.Namespace);
            Assert.Equal("cn", opt.Value.ConnectionName);
        }

        [Fact]
        public void SetupOutboxOptions_GetValuesFromConfig()
        {
            // Arrange
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "RabbitMQ:Outbox:Namespace", "ns" },
                    { "RabbitMQ:Outbox:ConnectionName", "cn" },
                })
                .Build();

            var services = new ServiceCollection();
            services.AddRabbitMQ().AddOutboxServices<TestDbContext>(config);

            var provider = services.BuildServiceProvider();
            var opt = provider.GetRequiredService<IOptions<OutboxOptions>>();

            // Act
            Assert.Equal("ns", opt.Value.Namespace);
            Assert.Equal("cn", opt.Value.ConnectionName);
        }
    }
}
