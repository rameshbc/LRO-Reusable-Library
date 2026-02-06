using System.Text.Json;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace LongRunningOperations.Core.Data;

/// <summary>
/// Azure Redis Cache / Redis-compatible implementation of IOperationStore.
/// Uses IDistributedCache for broad compatibility (Azure Redis, local Redis, NCache, etc.).
///
/// Characteristics:
///   - Shared across all API instances (multi-instance safe ✅)
///   - Very fast reads/writes (sub-millisecond)
///   - Data persists across app restarts (depends on Redis persistence config)
///   - Requires Redis connection string in LroOptions.RedisConnectionString
///
/// Storage design:
///   - Each operation stored as JSON at key "lro:op:{id}"
///   - A secondary index at key "lro:all_ids" tracks all operation IDs for listing
///   - Active operations have a 2-day TTL, completed operations 7-day TTL
///   - Index capped at 1000 most recent operations
///
/// For very high-volume scenarios (>1000 concurrent operations), consider implementing
/// a custom IOperationStore using StackExchange.Redis directly with SCAN/sorted sets.
/// </summary>
public class RedisOperationStore : IOperationStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisOperationStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private const string KeyPrefix = "lro:op:";
    private const string AllIdsKey = "lro:all_ids";
    private static readonly TimeSpan CompletedTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan ActiveTtl = TimeSpan.FromDays(2);

    public RedisOperationStore(IDistributedCache cache, ILogger<RedisOperationStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<OperationStatus> CreateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        var key = GetKey(operation.OperationId);
        var json = JsonSerializer.Serialize(operation, JsonOptions);

        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ActiveTtl
        }, ct);

        await AddToIndexAsync(operation.OperationId, ct);

        _logger.LogDebug("Redis: Created operation {OperationId}", operation.OperationId);
        return operation;
    }

    public async Task<OperationStatus?> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        var key = GetKey(operationId);
        var json = await _cache.GetStringAsync(key, ct);
        if (string.IsNullOrEmpty(json)) return null;
        return JsonSerializer.Deserialize<OperationStatus>(json, JsonOptions);
    }

    public async Task UpdateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        operation.LastUpdatedAtUtc = DateTime.UtcNow;
        var key = GetKey(operation.OperationId);
        var json = JsonSerializer.Serialize(operation, JsonOptions);

        var isTerminal = operation.State is OperationState.Succeeded
            or OperationState.Failed or OperationState.Cancelled or OperationState.TimedOut;

        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = isTerminal ? CompletedTtl : ActiveTtl
        }, ct);

        _logger.LogDebug("Redis: Updated operation {OperationId} → {State}",
            operation.OperationId, operation.State);
    }

    public async Task<IReadOnlyList<OperationStatus>> GetTimedOutOperationsAsync(CancellationToken ct = default)
    {
        var allOps = await GetAllOperationsAsync(ct);
        var now = DateTime.UtcNow;
        return allOps
            .Where(o => (o.State == OperationState.Accepted || o.State == OperationState.Running)
                        && o.CreatedAtUtc.AddSeconds(o.TimeoutSeconds) < now)
            .ToList();
    }

    public async Task<IReadOnlyList<OperationStatus>> ListAsync(
        string? operationName = null, OperationState? state = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var allOps = await GetAllOperationsAsync(ct);
        var query = allOps.AsEnumerable();

        if (!string.IsNullOrEmpty(operationName))
            query = query.Where(o => o.OperationName == operationName);
        if (state.HasValue)
            query = query.Where(o => o.State == state.Value);

        return query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
    }

    private async Task<List<OperationStatus>> GetAllOperationsAsync(CancellationToken ct)
    {
        var results = new List<OperationStatus>();
        var allIdsJson = await _cache.GetStringAsync(AllIdsKey, ct);
        if (string.IsNullOrEmpty(allIdsJson)) return results;

        var allIds = JsonSerializer.Deserialize<List<Guid>>(allIdsJson, JsonOptions)
                     ?? new List<Guid>();

        foreach (var id in allIds)
        {
            var op = await GetAsync(id, ct);
            if (op != null) results.Add(op);
        }
        return results;
    }

    private async Task AddToIndexAsync(Guid operationId, CancellationToken ct)
    {
        var allIdsJson = await _cache.GetStringAsync(AllIdsKey, ct);
        var allIds = string.IsNullOrEmpty(allIdsJson)
            ? new List<Guid>()
            : JsonSerializer.Deserialize<List<Guid>>(allIdsJson, JsonOptions) ?? new List<Guid>();

        if (!allIds.Contains(operationId))
        {
            allIds.Add(operationId);
            if (allIds.Count > 1000)
                allIds = allIds.Skip(allIds.Count - 1000).ToList();

            await _cache.SetStringAsync(AllIdsKey,
                JsonSerializer.Serialize(allIds, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CompletedTtl }, ct);
        }
    }

    private static string GetKey(Guid operationId) => $"{KeyPrefix}{operationId}";
}
