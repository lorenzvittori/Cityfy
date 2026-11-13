using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using ServiceDefault.Listening;
using ServiceDefault.Models;

namespace ServiceDefault.Hosting;

public static class MongoExtensions
{
    /// <summary>Name of the Mongo database resource declared in the AppHost.</summary>
    public const string DefaultConnectionName = "cityfy";

    /// <summary>
    /// Shared <see cref="IMongoClient"/> / <see cref="IMongoDatabase"/> through the Aspire
    /// client integration: connection string "ConnectionStrings:{connectionName}" (injected by
    /// the AppHost), health check and tracing included. The "Mongo" section only carries
    /// collection names (<see cref="MongoOptions"/>).
    /// </summary>
    public static TBuilder AddCityFyMongo<TBuilder>(this TBuilder builder, string connectionName = DefaultConnectionName)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection("Mongo"));
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MongoOptions>>().Value);

        builder.AddMongoDBClient(connectionName);

        // The database comes from the connection string (AppHost: AddDatabase("cityfy"))
        var connectionString = builder.Configuration.GetConnectionString(connectionName);
        var databaseName = string.IsNullOrEmpty(connectionString) ? null : MongoUrl.Create(connectionString).DatabaseName;
        builder.Services.TryAddSingleton(sp => sp.GetRequiredService<IMongoClient>()
            .GetDatabase(databaseName ?? connectionName));

        return builder;
    }

    /// <summary>Shared writer for the 'listening_events' collection. Requires <see cref="AddCityFyMongo{TBuilder}"/>.</summary>
    public static IServiceCollection AddListeningEventStore(this IServiceCollection services)
    {
        services.AddSingleton<IListeningEventStore, MongoListeningEventStore>();
        return services;
    }

    /// <summary>Change of owner of the plays (only for the service that owns their deletion: Catalog).</summary>
    public static IServiceCollection AddListeningEventTransfer(this IServiceCollection services)
    {
        services.AddSingleton<IListeningEventTransfer, MongoListeningEventTransfer>();
        return services;
    }
}
