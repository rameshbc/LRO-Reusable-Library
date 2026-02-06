using System.Reflection;
using System.Text.Json;
using LongRunningOperations.Core.Attributes;
using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using LongRunningOperations.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LongRunningOperations.Core.Filters;

/// <summary>
/// Resource filter that intercepts methods decorated with [LongRunningOperation].
/// Uses IAsyncResourceFilter so it runs BEFORE model binding — avoiding issues
/// with parameters like IOperationContext that MVC can't bind.
///
/// Flow:
///   1. Reads the raw request body
///   2. Creates an operation record in the shared store
///   3. Returns 202 Accepted immediately
///   4. Fires the actual action in a background Task, injecting IOperationContext
/// </summary>
public class LongRunningOperationFilter : IAsyncResourceFilter
{
    private readonly OperationManager _operationManager;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LroOptions _options;
    private readonly ILogger<LongRunningOperationFilter> _logger;

    public LongRunningOperationFilter(
        OperationManager operationManager,
        IServiceScopeFactory scopeFactory,
        IOptions<LroOptions> options,
        ILogger<LongRunningOperationFilter> logger)
    {
        _operationManager = operationManager;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Check if the action has [LongRunningOperation] attribute
        var lroAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<LongRunningOperationAttribute>()
            .FirstOrDefault();

        if (lroAttribute == null)
        {
            // Not a long-running operation — pass through normally
            await next();
            return;
        }

        var httpContext = context.HttpContext;
        var createdBy = httpContext.User?.Identity?.Name;
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();

        // Read the raw request body before short-circuiting (we'll need it for background execution)
        string? requestBody = null;
        if (httpContext.Request.ContentLength > 0 || httpContext.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            httpContext.Request.Body.Position = 0;
        }

        // Capture query string parameters
        var queryParams = httpContext.Request.Query.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToString());

        // Create operation record
        var (operation, opContext) = await _operationManager.StartOperationAsync(
            lroAttribute.OperationName,
            lroAttribute.TimeoutSeconds,
            createdBy,
            correlationId);

        // Build status check URL
        var baseUrl = _options.BaseUrl?.TrimEnd('/')
                      ?? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
        var statusUrl = $"{baseUrl}/api/operations/{operation.OperationId}";
        var cancelUrl = lroAttribute.AllowCancellation
            ? $"{baseUrl}/api/operations/{operation.OperationId}/cancel"
            : null;

        // Get the controller/action descriptor for background execution
        var controllerActionDescriptor = context.ActionDescriptor as ControllerActionDescriptor;

        // Fire the actual action in the background
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                _logger.LogInformation("Starting background execution of operation {OperationId} ({Name})",
                    operation.OperationId, lroAttribute.OperationName);

                // Update state to Running
                var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
                var op = await store.GetAsync(operation.OperationId);
                if (op != null)
                {
                    op.State = OperationState.Running;
                    await store.UpdateAsync(op);
                }

                // Re-invoke the action method with the operation context injected
                await ExecuteActionInBackground(controllerActionDescriptor!, opContext, scope,
                    requestBody, queryParams);

                // If the action didn't explicitly set result/fail, mark as succeeded
                var finalOp = await store.GetAsync(operation.OperationId);
                if (finalOp != null && finalOp.State == OperationState.Running)
                {
                    finalOp.State = OperationState.Succeeded;
                    finalOp.PercentComplete = 100;
                    finalOp.CompletedAtUtc = DateTime.UtcNow;
                    await store.UpdateAsync(finalOp);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Operation {OperationId} was cancelled.", operation.OperationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Operation {OperationId} failed.", operation.OperationId);

                var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
                var failedOp = await store.GetAsync(operation.OperationId);
                if (failedOp != null && failedOp.State != OperationState.Failed)
                {
                    failedOp.State = OperationState.Failed;
                    failedOp.ErrorMessage = ex.Message;
                    failedOp.ErrorDetails = ex.ToString();
                    failedOp.CompletedAtUtc = DateTime.UtcNow;
                    await store.UpdateAsync(failedOp);
                }
            }
            finally
            {
                _operationManager.CompleteTracking(operation.OperationId);
            }
        });

        // Immediately return 202 Accepted (short-circuits the pipeline)
        var response = new OperationAcceptedResponse
        {
            OperationId = operation.OperationId,
            OperationName = lroAttribute.OperationName,
            Status = OperationState.Accepted,
            StatusCheckUrl = statusUrl,
            CancelUrl = cancelUrl,
            EstimatedDurationSeconds = lroAttribute.EstimatedDurationSeconds,
            CreatedAtUtc = operation.CreatedAtUtc
        };

        httpContext.Response.Headers["Location"] = statusUrl;
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;

        context.Result = new AcceptedResult(statusUrl, response);
        // Short-circuit: do NOT call next() — we already set the result
    }

    /// <summary>
    /// Executes the controller action method in the background, injecting IOperationContext.
    /// Deserializes the captured request body to reconstruct parameters.
    /// </summary>
    private async Task ExecuteActionInBackground(
        ControllerActionDescriptor descriptor,
        OperationContext opContext,
        IServiceScope scope,
        string? requestBody,
        Dictionary<string, string> queryParams)
    {
        var controllerType = descriptor.ControllerTypeInfo.AsType();
        var controller = ActivatorUtilities.CreateInstance(scope.ServiceProvider, controllerType);

        var method = descriptor.MethodInfo;

        // Build parameters, injecting IOperationContext where requested
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];

            if (param.ParameterType == typeof(IOperationContext) ||
                param.ParameterType.IsAssignableFrom(typeof(OperationContext)))
            {
                args[i] = opContext;
            }
            else if (param.ParameterType == typeof(CancellationToken))
            {
                args[i] = opContext.CancellationToken;
            }
            else if (!string.IsNullOrEmpty(requestBody) && IsComplexType(param.ParameterType))
            {
                // Deserialize the request body into this parameter type
                try
                {
                    args[i] = JsonSerializer.Deserialize(requestBody, param.ParameterType,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch
                {
                    args[i] = param.HasDefaultValue ? param.DefaultValue : null;
                }
            }
            else if (queryParams.TryGetValue(param.Name!, out var qVal))
            {
                args[i] = Convert.ChangeType(qVal, param.ParameterType);
            }
            else
            {
                args[i] = param.HasDefaultValue ? param.DefaultValue : null;
            }
        }

        var result = method.Invoke(controller, args);

        if (result is Task task)
        {
            await task;
        }
    }

    private static bool IsComplexType(Type type)
    {
        return !type.IsPrimitive && type != typeof(string) && type != typeof(decimal)
               && type != typeof(DateTime) && type != typeof(Guid)
               && !type.IsEnum && type != typeof(CancellationToken);
    }
}
