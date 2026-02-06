using LongRunningOperations.Core.Attributes;
using LongRunningOperations.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace SampleApi.Controllers;

/// <summary>
/// Sample controller demonstrating how to use [LongRunningOperation] attribute.
/// Any action decorated with the attribute will automatically:
///   - Return 202 Accepted immediately
///   - Execute the actual work in the background
///   - Status can be polled from ANY instance via /api/operations/{id}
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(ILogger<ReportsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generate a large report — long-running operation.
    /// The [LongRunningOperation] attribute makes this return 202 immediately.
    /// The actual work runs in the background.
    /// </summary>
    [HttpPost("generate")]
    [LongRunningOperation("ReportGeneration", TimeoutSeconds = 300, EstimatedDurationSeconds = 30)]
    public async Task GenerateReport(
        [FromBody] ReportRequest request,
        IOperationContext operationContext)
    {
        _logger.LogInformation("Starting report generation for: {ReportType}", request.ReportType);

        // Simulate phased work with progress updates
        for (int i = 0; i <= 100; i += 10)
        {
            operationContext.CancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(1000, operationContext.CancellationToken); // Simulate work
            await operationContext.ReportProgressAsync(i);

            _logger.LogInformation("Report progress: {Percent}%", i);
        }

        // Set the final result
        await operationContext.SetResultAsync(new
        {
            ReportType = request.ReportType,
            RowCount = 15000,
            DownloadUrl = $"/api/reports/download/{operationContext.OperationId}",
            GeneratedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Export data — another long-running operation with cancellation support.
    /// </summary>
    [HttpPost("export")]
    [LongRunningOperation("DataExport", TimeoutSeconds = 600, AllowCancellation = true, EstimatedDurationSeconds = 60)]
    public async Task ExportData(
        [FromBody] ExportRequest request,
        IOperationContext operationContext)
    {
        _logger.LogInformation("Starting data export: {Format}", request.Format);

        var totalBatches = 20;
        for (int batch = 1; batch <= totalBatches; batch++)
        {
            operationContext.CancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(500, operationContext.CancellationToken); // Simulate batch processing
            var progress = (int)((double)batch / totalBatches * 100);
            await operationContext.ReportProgressAsync(progress);
        }

        await operationContext.SetResultAsync(new
        {
            Format = request.Format,
            RecordsExported = 50000,
            FileSize = "25MB",
            DownloadUrl = $"/api/exports/download/{operationContext.OperationId}"
        });
    }

    /// <summary>
    /// Quick operation — NOT decorated with [LongRunningOperation],
    /// so it behaves as a normal synchronous endpoint.
    /// </summary>
    [HttpGet("quick-summary")]
    public IActionResult GetQuickSummary()
    {
        return Ok(new { message = "This is a normal synchronous endpoint", timestamp = DateTime.UtcNow });
    }
}

public class ReportRequest
{
    public string ReportType { get; set; } = "Monthly";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class ExportRequest
{
    public string Format { get; set; } = "CSV";
    public string? Filter { get; set; }
}
