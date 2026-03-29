using Microsoft.EntityFrameworkCore;
using RabbitMQ.Services.Configurations;

namespace RabbitMQ.Services
{
    public static class ModelBuilderHelper
    {
        public static void AddOutboxEntities(this ModelBuilder builder, string tableName, string? schema = null)
        {
            builder.ApplyConfiguration(new OutboxMessageConfiguration(tableName, schema));
        }
    }
}
