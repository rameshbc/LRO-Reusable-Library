using System.Collections.Concurrent;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;

namespace LongRunningOperations.Core.Data;

/// <summary>
/// Pure in-memory implementation of IOperationStore using ConcurrentDictionary.
/// 
/// Characteristics:
///   - No database dependency — zero configuration needed
///   - Data lives in process memory, lost on app restart
///   - NOT shared across multiple API instances (single-instance only)
///   - Ideal for local development, unit testing, and prototyping
///   
/// ⚠️ Do NOT use in production with multiple instances behind a load balancer.
///    Use SqlServer or Redis for multi-instance deployments.
/// </summary>
public class InMemoryOperationStore : IOperationStore
{
    private readonly ConcurrentDictionary<Guid, OperationStatus> _store = new();

    public Task<OperationStatus> CreateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        var clone = CloneOperation(operation);
        _store[operation.OperationId] = clone;
        return Task.FromResult(clone);
    }

    public Task<OperationStatus?> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        _store.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation is not null ? CloneOperation(operation) : null);
    }

    public Task UpdateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        operation.LastUpdatedAtUtc = DateTime.UtcNow;
        _store[operation.OperationId] = CloneOperation(operation);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OperationStatus>> GetTimedOutOperationsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var timedOut = _store.Values
            .Where(o => (o.State == OperationState.Accepted || o.State == OperationState.Running)
                        && o.CreatedAtUtc.AddSeconds(o.TimeoutSeconds) < now)
            .Select(CloneOperation)
            .ToList();

        return Task.FromResult<IReadOnlyList<OperationStatus>>(timedOut);
    }

    public Task<IReadOnlyList<OperationStatus>> ListAsync(
        string? operationName = null,
        OperationState? state = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _store.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(operationName))
            query = query.Where(o => o.OperationName == operationName);

        if (state.HasValue)
            query = query.Where(o => o.State == state.Value);

        var result = query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(CloneOperation)
            .ToList();

        return Task.FromResult<IReadOnlyList<OperationStatus>>(result);
    }

    /// <summary>
    /// Clone to prevent external mutations from affecting stored state.
    /// </summary>
    private static OperationStatus CloneOperation(OperationStatus source) => new()
    {
        OperationId = source.OperationId,
        OperationName = source.OperationName,
        State = source.State,
        PercentComplete = source.PercentComplete,
        CreatedAtUtc = source.CreatedAtUtc,
        LastUpdatedAtUtc = source.LastUpdatedAtUtc,
        CompletedAtUtc = source.CompletedAtUtc,
        ResultData = source.ResultData,
        ErrorMessage = source.ErrorMessage,
        ErrorDetails = source.ErrorDetails,
        ProcessingInstanceId = source.ProcessingInstanceId,
        TimeoutSeconds = source.TimeoutSeconds,
        CancellationRequested = source.CancellationRequested,
        CreatedBy = source.CreatedBy,
        CorrelationId = source.CorrelationId,
    };
}
