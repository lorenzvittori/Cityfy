using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceDefault.Messaging;

namespace ServiceDefault.Hosting
{
    public static class MessagingExtensions
    {
        /// <summary>Name of the RabbitMQ resource declared in the AppHost.</summary>
        public const string DefaultConnectionName = "broker";

        /// <summary>
        /// RabbitMQ connection through the Aspire client integration (connection string
        /// "ConnectionStrings:broker", health check and tracing included) and the
        /// <see cref="IMessagePublisher"/>. Consumers are added with <see cref="AddMessageConsumer{TConsumer}"/>.
        /// </summary>
        public static TBuilder AddCityFyMessaging<TBuilder>(this TBuilder builder, string connectionName = DefaultConnectionName)
            where TBuilder : IHostApplicationBuilder
        {
            builder.AddRabbitMQClient(connectionName);
            builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
            return builder;
        }

        /// <summary>Consumer-side deduplication in Mongo ('processed_messages'). Requires AddCityFyMongo.</summary>
        public static IServiceCollection AddMessageDeduplication(this IServiceCollection services)
        {
            services.AddSingleton<IProcessedMessageStore, MongoProcessedMessageStore>();
            return services;
        }

        public static IServiceCollection AddMessageConsumer<TConsumer>(this IServiceCollection services)
            where TConsumer : class, IHostedService
        {
            services.AddHostedService<TConsumer>();
            return services;
        }
    }
}
