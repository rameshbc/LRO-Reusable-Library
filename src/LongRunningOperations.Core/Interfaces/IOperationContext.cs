using LongRunningOperations.Core.Models;

namespace LongRunningOperations.Core.Interfaces;

/// <summary>
/// Provides an operation context to the user's background work.
/// Injected into the action method as a parameter, or available via DI.
/// </summary>
public interface IOperationContext
{
    /// <summary>The unique ID for this operation.</summary>
    Guid OperationId { get; }

    /// <summary>Token that is triggered when cancellation is requested.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Update progress percentage (0–100).</summary>
    Task ReportProgressAsync(int percentComplete);

    /// <summary>Store intermediate or final result data.</summary>
    Task SetResultAsync(object result);

    /// <summary>Mark the operation as failed with an error.</summary>
    Task SetFailedAsync(string errorMessage, string? errorDetails = null);
}
