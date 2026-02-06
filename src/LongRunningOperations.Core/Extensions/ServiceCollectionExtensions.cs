using LongRunningOperations.Core.Data;
using LongRunningOperations.Core.Filters;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using LongRunningOperations.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LongRunningOperations.Core.Extensions;

/// <summary>
/// Extension methods to register the LRO library into any ASP.NET Core API.
/// Usage:
///     builder.Services.AddLongRunningOperations(builder.Configuration);
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all LRO services, the chosen storage provider, action filter,
    /// background timeout service, and the built-in operations controller.
    /// 
    /// Storage providers (set via "LongRunningOperations:StorageProvider"):
    ///   - "InMemory"   → ConcurrentDictionary-based (single instance, dev/test)
    ///   - "SqlServer"  → EF Core + SQL Server (multi-instance, production)
    ///   - "Redis"      → Azure Redis Cache / IDistributedCache (multi-instance, high perf)
    /// </summary>
    public static IServiceCollection AddLongRunningOperations(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<LroOptions>? configureOptions = null)
    {
        // Bind options from configuration
        var optionsSection = configuration.GetSection(LroOptions.SectionName);
        services.Configure<LroOptions>(optionsSection);

        // Apply overrides if provided
        if (configureOptions != null)
        {
            services.PostConfigure(configureOptions);
        }

        // Ensure instance ID is set
        services.PostConfigure<LroOptions>(opt =>
        {
            if (string.IsNullOrEmpty(opt.InstanceId))
                opt.InstanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}"[..32];
        });

        // Determine storage provider
        var options = new LroOptions();
        optionsSection.Bind(options);
        configureOptions?.Invoke(options);

        switch (options.StorageProvider)
        {
            case StorageProviderType.SqlServer:
                RegisterSqlServer(services, options);
                break;

            case StorageProviderType.Redis:
                RegisterRedis(services, options);
                break;

            case StorageProviderType.InMemory:
            default:
                RegisterInMemory(services);
                break;
        }

        // Register core services
        services.AddSingleton<OperationManager>();
        services.AddHostedService<OperationTimeoutService>();

        // Register the resource filter globally
        services.AddScoped<LongRunningOperationFilter>();
        services.AddMvc(o => o.Filters.AddService<LongRunningOperationFilter>());

        // Register the built-in OperationsController from this assembly
        services.AddControllers()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }

    /// <summary>
    /// Registers SQL Server storage via EF Core.
    /// Multi-instance safe — all instances share the same database.
    /// </summary>
    private static void RegisterSqlServer(IServiceCollection services, LroOptions options)
    {
        if (string.IsNullOrEmpty(options.ConnectionString))
            throw new InvalidOperationException(
                "ConnectionString is required when StorageProvider is SqlServer. " +
                "Set 'LongRunningOperations:ConnectionString' in your configuration.");

        services.AddDbContext<OperationDbContext>(dbOptions =>
            dbOptions.UseSqlServer(options.ConnectionString));

        services.AddScoped<IOperationStore, EfOperationStore>();
    }

    /// <summary>
    /// Registers Azure Redis Cache storage via IDistributedCache.
    /// Multi-instance safe — all instances share the same Redis.
    /// </summary>
    private static void RegisterRedis(IServiceCollection services, LroOptions options)
    {
        if (string.IsNullOrEmpty(options.RedisConnectionString))
            throw new InvalidOperationException(
                "RedisConnectionString is required when StorageProvider is Redis. " +
                "Set 'LongRunningOperations:RedisConnectionString' in your configuration.");

        services.AddStackExchangeRedisCache(redisOptions =>
        {
            redisOptions.Configuration = options.RedisConnectionString;
            redisOptions.InstanceName = "LRO:";
        });

        services.AddSingleton<IOperationStore, RedisOperationStore>();
    }

    /// <summary>
    /// Registers pure in-memory storage using ConcurrentDictionary.
    /// Single instance only — data is lost on restart.
    /// Best for development and testing.
    /// </summary>
    private static void RegisterInMemory(IServiceCollection services)
    {
        services.AddSingleton<IOperationStore, InMemoryOperationStore>();
    }
}
