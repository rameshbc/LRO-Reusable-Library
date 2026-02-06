namespace LongRunningOperations.Core.Models;

/// <summary>
/// The DTO returned to API callers when they initiate or check on an operation.
/// </summary>
public class OperationAcceptedResponse
{
    public Guid OperationId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public OperationState Status { get; set; }
    public string StatusCheckUrl { get; set; } = string.Empty;
    public string? CancelUrl { get; set; }
    public int EstimatedDurationSeconds { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The DTO returned when polling for operation status.
/// </summary>
public class OperationStatusResponse
{
    public Guid OperationId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public OperationState Status { get; set; }
    public int PercentComplete { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ResultData { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RetryAfterSeconds { get; set; }
}
