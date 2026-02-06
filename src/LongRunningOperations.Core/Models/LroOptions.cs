namespace LongRunningOperations.Core.Models;

/// <summary>
/// Storage provider options for operation status persistence.
/// </summary>
public enum StorageProviderType
{
    /// <summary>
    /// In-memory dictionary storage (single instance only, data lost on restart).
    /// Best for: development, testing, single-instance deployments.
    /// </summary>
    InMemory,

    /// <summary>
    /// SQL Server via Entity Framework Core (multi-instance safe).
    /// Best for: production with existing SQL Server infrastructure.
    /// </summary>
    SqlServer,

    /// <summary>
    /// Azure Redis Cache / any Redis-compatible server (multi-instance safe).
    /// Best for: high-performance multi-instance deployments.
    /// </summary>
    Redis
}

/// <summary>
/// Configuration options for the long-running operations library.
/// Bind from appsettings.json section "LongRunningOperations".
/// </summary>
public class LroOptions
{
    public const string SectionName = "LongRunningOperations";

    /// <summary>
    /// Which storage provider to use for persisting operation status.
    /// Options: "InMemory", "SqlServer", "Redis" (case-insensitive).
    /// Default: InMemory (for quick local development).
    /// </summary>
    public StorageProviderType StorageProvider { get; set; } = StorageProviderType.InMemory;

    /// <summary>
    /// SQL Server connection string. Required when StorageProvider = SqlServer.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Redis connection string. Required when StorageProvider = Redis.
    /// Example: "your-cache.redis.cache.windows.net:6380,password=xxx,ssl=True,abortConnect=False"
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this API instance (auto-generated if empty).
    /// Used to track which instance is processing an operation.
    /// </summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>
    /// Default polling interval hint (seconds) returned to clients.
    /// </summary>
    public int DefaultRetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// How often (in seconds) the background service checks for timed-out operations.
    /// </summary>
    public int TimeoutCheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Base URL for generating status check URLs. If empty, uses the request's base URL.
    /// Useful when behind a load balancer or API gateway.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// If true, automatically apply EF Core migrations at startup (SqlServer provider only).
    /// </summary>
    public bool AutoMigrate { get; set; } = true;
}
