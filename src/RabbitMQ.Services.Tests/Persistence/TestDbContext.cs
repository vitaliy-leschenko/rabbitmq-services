using Microsoft.EntityFrameworkCore;
using RabbitMQ.Services.Interfaces;

namespace RabbitMQ.Services.Tests.Persistence
{
    public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options), IOutboxDbContext
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.AddOutboxEntities("Messages", "dbo");
        }
    }
}
