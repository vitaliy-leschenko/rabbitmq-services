using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Services.Interfaces;

namespace RabbitMQ.Services.Tests.Persistence
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddOutboxPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<TestDbContext>(options =>
            {
                options.EnableSensitiveDataLogging();
                options.UseSqlite(CreateInMemoryDatabase());
            });
            services.AddRabbitMQ().AddOutboxServices<TestDbContext>(configuration);

            services.AddScoped<IOutboxDbContext>(provider =>
            {
                var db = provider.GetRequiredService<TestDbContext>();
                db.Database.EnsureCreated();
                return db;
            });

            return services;
        }

        private static SqliteConnection CreateInMemoryDatabase()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            connection.Open();
            return connection;
        }
    }
}
