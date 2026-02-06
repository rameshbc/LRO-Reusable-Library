using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LongRunningOperations.Core.Services;

/// <summary>
/// Background service that periodically checks for timed-out operations
/// and marks them accordingly. Runs on every instance.
/// </summary>
public class OperationTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LroOptions _options;
    private readonly ILogger<OperationTimeoutService> _logger;

    public OperationTimeoutService(
        IServiceScopeFactory scopeFactory,
        IOptions<LroOptions> options,
        ILogger<OperationTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Operation timeout watcher started (interval: {Interval}s)",
            _options.TimeoutCheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.TimeoutCheckIntervalSeconds), stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

                var timedOut = await store.GetTimedOutOperationsAsync(stoppingToken);
                foreach (var op in timedOut)
                {
                    op.State = OperationState.TimedOut;
                    op.CompletedAtUtc = DateTime.UtcNow;
                    op.ErrorMessage = $"Operation timed out after {op.TimeoutSeconds} seconds.";
                    await store.UpdateAsync(op, stoppingToken);

                    _logger.LogWarning("Operation {OperationId} ({Name}) timed out.",
                        op.OperationId, op.OperationName);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for timed-out operations");
            }
        }
    }
}
