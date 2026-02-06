using System.Text.Json;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LongRunningOperations.Core.Services;

/// <summary>
/// Default implementation of IOperationContext.
/// Provides the background operation with progress reporting, result storage, and cancellation.
/// Uses IServiceScopeFactory to create short-lived scopes for DB access (safe for background tasks).
/// </summary>
public class OperationContext : IOperationContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CancellationTokenSource _cts;

    public Guid OperationId { get; }

    public CancellationToken CancellationToken => _cts.Token;

    public OperationContext(Guid operationId, IServiceScopeFactory scopeFactory, CancellationTokenSource cts)
    {
        OperationId = operationId;
        _scopeFactory = scopeFactory;
        _cts = cts;
    }

    public async Task ReportProgressAsync(int percentComplete)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var op = await store.GetAsync(OperationId);
        if (op == null) return;

        op.PercentComplete = Math.Clamp(percentComplete, 0, 100);
        op.State = OperationState.Running;
        await store.UpdateAsync(op);
    }

    public async Task SetResultAsync(object result)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var op = await store.GetAsync(OperationId);
        if (op == null) return;

        op.ResultData = JsonSerializer.Serialize(result);
        op.State = OperationState.Succeeded;
        op.PercentComplete = 100;
        op.CompletedAtUtc = DateTime.UtcNow;
        await store.UpdateAsync(op);
    }

    public async Task SetFailedAsync(string errorMessage, string? errorDetails = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var op = await store.GetAsync(OperationId);
        if (op == null) return;

        op.State = OperationState.Failed;
        op.ErrorMessage = errorMessage;
        op.ErrorDetails = errorDetails;
        op.CompletedAtUtc = DateTime.UtcNow;
        await store.UpdateAsync(op);
    }

    /// <summary>
    /// Called internally when a cancel request comes in.
    /// </summary>
    public void RequestCancellation() => _cts.Cancel();
}
