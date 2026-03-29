using Microsoft.EntityFrameworkCore;

namespace RabbitMQ.Services.Interfaces
{
    public interface IOutboxDbContext
    {
        DbSet<TEntity> Set<TEntity>() where TEntity : class;

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
