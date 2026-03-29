using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RabbitMQ.Services.Entities;

namespace RabbitMQ.Services.Configurations
{
    public class OutboxMessageConfiguration(string tableName, string? schema) : IEntityTypeConfiguration<OutboxMessage>
    {
        private readonly string tableName = tableName;
        private readonly string? schema = schema;

        public void Configure(EntityTypeBuilder<OutboxMessage> builder)
        {
            builder.ToTable(tableName, schema);
            builder.HasKey(t => t.MessageId);
            builder.Property(t => t.MessageId).ValueGeneratedOnAdd();
            builder.Property(t => t.Namespace).HasMaxLength(128);
            builder.HasIndex(t => t.Namespace);
        }
    }
}
