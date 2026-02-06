namespace LongRunningOperations.Core.Models;

/// <summary>
/// Represents the lifecycle states of a long-running operation.
/// </summary>
public enum OperationState
{
    /// <summary>Operation has been accepted but not yet started.</summary>
    Accepted = 0,

    /// <summary>Operation is currently running.</summary>
    Running = 1,

    /// <summary>Operation completed successfully.</summary>
    Succeeded = 2,

    /// <summary>Operation failed with an error.</summary>
    Failed = 3,

    /// <summary>Operation was cancelled by the user.</summary>
    Cancelled = 4,

    /// <summary>Operation exceeded its timeout.</summary>
    TimedOut = 5
}

/// <summary>
/// The persisted record of a long-running operation, stored in the shared database
/// so any API instance can serve status checks.
/// </summary>
public class OperationStatus
{
    /// <summary>Unique operation identifier (GUID).</summary>
    public Guid OperationId { get; set; }

    /// <summary>The operation type name from the attribute.</summary>
    public string OperationName { get; set; } = string.Empty;

    /// <summary>Current state of the operation.</summary>
    public OperationState State { get; set; } = OperationState.Accepted;

    /// <summary>Progress percentage (0-100), updated by the operation.</summary>
    public int PercentComplete { get; set; }

    /// <summary>When the operation was created.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the operation last changed state.</summary>
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the operation completed (succeeded/failed/cancelled).</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>JSON-serialized result data on success.</summary>
    public string? ResultData { get; set; }

    /// <summary>Error message on failure.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Stack trace or detailed error info.</summary>
    public string? ErrorDetails { get; set; }

    /// <summary>The API instance ID that picked up this operation.</summary>
    public string? ProcessingInstanceId { get; set; }

    /// <summary>Timeout configured for this operation (seconds).</summary>
    public int TimeoutSeconds { get; set; } = 3600;

    /// <summary>Whether cancellation was requested.</summary>
    public bool CancellationRequested { get; set; }

    /// <summary>Optional: caller/user identifier for auditing.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Optional: correlation ID for distributed tracing.</summary>
    public string? CorrelationId { get; set; }
}
