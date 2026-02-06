namespace LongRunningOperations.Core.Attributes;

/// <summary>
/// Marks a controller action as a long-running operation.
/// When applied, the framework will:
/// 1. Accept the request and return 202 Accepted with an operation ID
/// 2. Execute the actual work in the background
/// 3. Provide status polling and result retrieval endpoints
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class LongRunningOperationAttribute : Attribute
{
    /// <summary>
    /// A friendly name for the operation type (e.g., "ReportGeneration", "DataExport").
    /// Used for categorization and filtering.
    /// </summary>
    public string OperationName { get; }

    /// <summary>
    /// Optional timeout in seconds. After this, the operation is marked as TimedOut.
    /// Default is 3600 (1 hour).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 3600;

    /// <summary>
    /// If true, the framework allows cancellation requests for this operation.
    /// Default is true.
    /// </summary>
    public bool AllowCancellation { get; set; } = true;

    /// <summary>
    /// Optional: estimated duration hint for clients (in seconds).
    /// Returned in the 202 response to help clients set polling intervals.
    /// </summary>
    public int EstimatedDurationSeconds { get; set; } = 0;

    public LongRunningOperationAttribute(string operationName)
    {
        OperationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
    }
}
