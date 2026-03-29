using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Services.Entities;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Tests.Persistence;
using Xunit;

namespace RabbitMQ.Services.Tests
{
    public class ModelBuilderHelperTests
    {
        [Fact]
        public void AddOutboxEntities_ShouldAddModel()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build();

            var services = new ServiceCollection().AddOutboxPersistence(config);

            var provider = services.BuildServiceProvider();

            var db = provider.GetRequiredService<IOutboxDbContext>();

            // Act
            var items = db.Set<OutboxMessage>().ToList();

            // Assert
            Assert.Empty(items);
        }
    }
}
