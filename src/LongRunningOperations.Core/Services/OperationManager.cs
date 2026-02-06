using System.Collections.Concurrent;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LongRunningOperations.Core.Services;

/// <summary>
/// Manages the lifecycle of long-running operations.
/// Tracks in-flight operations on this instance and coordinates with the shared store.
/// Registered as Singleton — uses IServiceScopeFactory for DB access.
/// </summary>
public class OperationManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LroOptions _options;
    private readonly ILogger<OperationManager> _logger;

    // Tracks operations running on THIS instance for cancellation support
    private readonly ConcurrentDictionary<Guid, OperationContext> _activeContexts = new();

    public OperationManager(
        IServiceScopeFactory scopeFactory,
        IOptions<LroOptions> options,
        ILogger<OperationManager> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new operation record and returns a context for the background work.
    /// </summary>
    public async Task<(OperationStatus Operation, OperationContext Context)> StartOperationAsync(
        string operationName, int timeoutSeconds, string? createdBy = null, string? correlationId = null)
    {
        var operationId = Guid.NewGuid();
        var cts = new CancellationTokenSource();

        var operation = new OperationStatus
        {
            OperationId = operationId,
            OperationName = operationName,
            State = OperationState.Accepted,
            CreatedAtUtc = DateTime.UtcNow,
            LastUpdatedAtUtc = DateTime.UtcNow,
            TimeoutSeconds = timeoutSeconds,
            ProcessingInstanceId = _options.InstanceId,
            CreatedBy = createdBy,
            CorrelationId = correlationId
        };

        using (var scope = _scopeFactory.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
            await store.CreateAsync(operation);
        }

        var context = new OperationContext(operationId, _scopeFactory, cts);
        _activeContexts[operationId] = context;

        _logger.LogInformation("Operation {OperationId} ({OperationName}) created on instance {InstanceId}",
            operationId, operationName, _options.InstanceId);

        return (operation, context);
    }

    /// <summary>
    /// Gets the status of any operation from the shared store.
    /// Any API instance can call this.
    /// </summary>
    public async Task<OperationStatus?> GetStatusAsync(Guid operationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        return await store.GetAsync(operationId);
    }

    /// <summary>
    /// Request cancellation. If the operation is running on this instance, cancel directly.
    /// Otherwise, set the flag in the DB so the owning instance picks it up.
    /// </summary>
    public async Task<bool> RequestCancellationAsync(Guid operationId)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var op = await store.GetAsync(operationId);
        if (op == null) return false;

        if (op.State != OperationState.Accepted && op.State != OperationState.Running)
            return false;

        // If running on this instance, cancel the token directly
        if (_activeContexts.TryGetValue(operationId, out var context))
        {
            context.RequestCancellation();
        }

        // Always set the DB flag so other instances can see it
        op.CancellationRequested = true;
        op.State = OperationState.Cancelled;
        op.CompletedAtUtc = DateTime.UtcNow;
        await store.UpdateAsync(op);

        _logger.LogInformation("Cancellation requested for operation {OperationId}", operationId);
        return true;
    }

    /// <summary>
    /// Mark that an operation's background work is done (cleanup tracking).
    /// </summary>
    public void CompleteTracking(Guid operationId)
    {
        _activeContexts.TryRemove(operationId, out _);
    }

    /// <summary>
    /// List operations with optional filters.
    /// </summary>
    public async Task<IReadOnlyList<OperationStatus>> ListAsync(
        string? operationName = null, OperationState? state = null, int page = 1, int pageSize = 20)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        return await store.ListAsync(operationName, state, page, pageSize);
    }
}
