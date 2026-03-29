using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Services.HostedServices;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace RabbitMQ.Services.Tests.Consumer
{
    public class DependencyInjectionTests
    {
        [Fact]
        public void AddConsumerHostedService_ShouldRegisterIHostedService()
        {
            // Arrange
            var configuration = new Mock<IConfiguration>();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRabbitMQ();
            services.AddConsumerHostedService<TestMessage, TestMessageHandler>(configuration.Object);
            var provider = services.BuildServiceProvider();

            // Act
            var items = provider.GetServices<IHostedService>();

            // Assert
            Assert.NotNull(items);
            Assert.Contains(items, t => t is ConsumerHostedService<TestMessage>);
        }

        [Fact]
        public void AddConsumerHostedService_ShouldRegisterIAsyncMessageConsumer()
        {
            // Arrange
            var configuration = new Mock<IConfiguration>();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRabbitMQ();
            services.AddConsumerHostedService<TestMessage, TestMessageHandler>(configuration.Object);
            var provider = services.BuildServiceProvider();

            // Act
            var consumer = provider.GetRequiredService<IAsyncMessageConsumer<TestMessage>>();

            // Assert
            Assert.NotNull(consumer);
        }

        [Fact]
        public void ConfigureConsumerConfiguration_ShouldInvokeConfigureOptions()
        {
            // Arrange
            var customConfigurationCalled = false;

            var data = new Dictionary<string, string?>
                {
                    { "RabbitMQ:Consumers:" + nameof(TestMessage) + ":Url", "amqp://localhost/vhost/queue" },
                    { "RabbitMQ:Consumers:" + nameof(TestMessage) + ":MaxRetryCount", "42" },
                };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(data)
                .Build();

            var services = new ServiceCollection();
            services.AddRabbitMQ().AddConsumerHostedService<TestMessage, TestMessageHandler>(configuration, config =>
            {
                customConfigurationCalled = true;
            });
            var provider = services.BuildServiceProvider();

            // Act
            var options = provider.GetRequiredService<IOptions<ConsumerConfiguration<TestMessage>>>().Value;

            // Assert
            Assert.Equal(typeof(TestMessageHandler).FullName, options.ConnectionName);
            Assert.Equal("amqp://localhost/vhost/queue", options.Url);
            Assert.Equal(42, options.MaxRetryCount);
            Assert.True(customConfigurationCalled);
        }

        [Fact]
        public void ConfigureConsumerConfiguration_ShouldThrowInvalidOperationException_WhenUrlIsNotConfigured()
        {
            // Arrange
            var configuration = new ConfigurationBuilder().Build();

            var services = new ServiceCollection();
            services.AddRabbitMQ().AddConsumerHostedService<TestMessage, TestMessageHandler>(configuration);
            var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptions<ConsumerConfiguration<TestMessage>>>();

            // Act
            var act = () => options.Value;

            // Assert
            var ex = Assert.Throws<InvalidOperationException>(act);
            Assert.Equal(nameof(TestMessageHandler) + ".Url is not configured.", ex.Message);
        }
    }

    [ExcludeFromCodeCoverage]
    public class TestMessage
    {
    }

    [ExcludeFromCodeCoverage]
    public class TestMessageHandler : BaseMessageHandler<TestMessage>
    {
        public TestMessageHandler()
        {
        }

        public override Task HandleAsync(TestMessage message, CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}
