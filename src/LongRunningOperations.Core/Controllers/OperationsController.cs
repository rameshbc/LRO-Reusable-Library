using LongRunningOperations.Core.Models;
using LongRunningOperations.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LongRunningOperations.Core.Controllers;

/// <summary>
/// Built-in controller for operation status checks, cancellation, and listing.
/// Automatically registered so ANY API instance can serve status requests.
/// This decouples status-checking from the instance that started the operation.
/// </summary>
[ApiController]
[Route("api/operations")]
public class OperationsController : ControllerBase
{
    private readonly OperationManager _manager;
    private readonly LroOptions _options;

    public OperationsController(OperationManager manager, IOptions<LroOptions> options)
    {
        _manager = manager;
        _options = options.Value;
    }

    /// <summary>
    /// Get the current status of an operation by ID.
    /// Returns 200 with result if completed, or 200 with progress if still running.
    /// </summary>
    [HttpGet("{operationId:guid}")]
    [ProducesResponseType(typeof(OperationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(Guid operationId)
    {
        var op = await _manager.GetStatusAsync(operationId);
        if (op == null)
            return NotFound(new { error = "Operation not found", operationId });

        var response = new OperationStatusResponse
        {
            OperationId = op.OperationId,
            OperationName = op.OperationName,
            Status = op.State,
            PercentComplete = op.PercentComplete,
            CreatedAtUtc = op.CreatedAtUtc,
            LastUpdatedAtUtc = op.LastUpdatedAtUtc,
            CompletedAtUtc = op.CompletedAtUtc,
            ResultData = op.State == OperationState.Succeeded ? op.ResultData : null,
            ErrorMessage = op.State == OperationState.Failed || op.State == OperationState.TimedOut
                ? op.ErrorMessage : null,
            RetryAfterSeconds = op.State is OperationState.Accepted or OperationState.Running
                ? _options.DefaultRetryAfterSeconds : null
        };

        // If still in progress, add Retry-After header
        if (op.State is OperationState.Accepted or OperationState.Running)
        {
            Response.Headers["Retry-After"] = _options.DefaultRetryAfterSeconds.ToString();
        }

        return Ok(response);
    }

    /// <summary>
    /// Cancel a running operation.
    /// </summary>
    [HttpPost("{operationId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid operationId)
    {
        var op = await _manager.GetStatusAsync(operationId);
        if (op == null)
            return NotFound(new { error = "Operation not found", operationId });

        var cancelled = await _manager.RequestCancellationAsync(operationId);
        if (!cancelled)
            return Conflict(new { error = "Operation cannot be cancelled in its current state", state = op.State.ToString() });

        return Ok(new { message = "Cancellation requested", operationId });
    }

    /// <summary>
    /// List operations with optional filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OperationStatusResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? operationName = null,
        [FromQuery] OperationState? state = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var operations = await _manager.ListAsync(operationName, state, page, pageSize);

        var response = operations.Select(op => new OperationStatusResponse
        {
            OperationId = op.OperationId,
            OperationName = op.OperationName,
            Status = op.State,
            PercentComplete = op.PercentComplete,
            CreatedAtUtc = op.CreatedAtUtc,
            LastUpdatedAtUtc = op.LastUpdatedAtUtc,
            CompletedAtUtc = op.CompletedAtUtc,
            ErrorMessage = op.State is OperationState.Failed or OperationState.TimedOut
                ? op.ErrorMessage : null
        }).ToList();

        return Ok(response);
    }
}
