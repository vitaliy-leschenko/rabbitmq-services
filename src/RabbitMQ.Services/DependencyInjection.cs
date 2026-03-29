using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Services.HostedServices;
using RabbitMQ.Services.Implementations;
using RabbitMQ.Services.Interfaces;
using RabbitMQ.Services.Settings;

namespace RabbitMQ.Services
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddRabbitMQ(this IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddTransient<IActivityBuilder, ActivityBuilder>();
            services.TryAddTransient<IActivityDataProvider, ActivityDataProvider>();
            services.TryAddSingleton<IDnsResolver, DnsResolver>();
            services.TryAddSingleton<IRabbitMQEndpointParser, RabbitMQEndpointParser>();
            services.TryAddSingleton<IConnectionFactoryBuilder, ConnectionFactoryBuilder>();
            services.TryAddSingleton<IConnectionBuilder, ConnectionBuilder>();
            return services;
        }

        public static IServiceCollection AddConsumerHostedService<TMessage, THandler>(
            this IServiceCollection services, IConfiguration configuration, Action<ConsumerConfiguration<TMessage>>? configureOptions = null)
            where TMessage : class
            where THandler : class, IMessageHandler<TMessage>
        {
            services
                .Configure<ConsumerConfiguration<TMessage>>(config =>
                {
                    // default configuration
                    config.ConnectionName = typeof(THandler).FullName!;

                    // try to override options from IConfiguration
                    configuration.GetSection("RabbitMQ:Consumers:Defaults").Bind(config);
                    configuration.GetSection("RabbitMQ:Consumers:" + typeof(TMessage).Name).Bind(config);

                    // custom configuration
                    configureOptions?.Invoke(config);

                    if (string.IsNullOrEmpty(config.Url))
                    {
                        throw new InvalidOperationException(typeof(THandler).Name + ".Url is not configured.");
                    }
                })
                .AddTransient<IMessageHandler<TMessage>, THandler>()
                .AddTransient<IAsyncMessageConsumer<TMessage>, AsyncMessageConsumer<TMessage>>()
                .AddHostedService<ConsumerHostedService<TMessage>>();

            return services;
        }

        public static IServiceCollection AddOutboxServices<T>(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<OutboxOptions>? configureOptions = null) where T : class, IOutboxDbContext
        {
            services.Configure<OutboxOptions>(options =>
            {
                configuration.GetSection("RabbitMQ:Outbox").Bind(options);
                configureOptions?.Invoke(options);
            });
            services.AddScoped<IOutboxDbContext>(provider => provider.GetRequiredService<T>());
            services.AddScoped<IAsyncMessageSender, AsyncMessageSender>();
            services.AddScoped<IMessageDeliveryService, MessageDeliveryService>();

            return services;
        }
    }
}
