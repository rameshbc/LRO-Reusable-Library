using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LongRunningOperations.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LongRunningOperations.Tests;

/// <summary>
/// Integration tests that validate the full LRO library flow using the SampleApi.
/// Uses WebApplicationFactory for in-process testing (no real server needed).
/// </summary>
public class LongRunningOperationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LongRunningOperationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostLongRunningAction_Returns202Accepted_WithOperationId()
    {
        // Arrange
        var request = new { ReportType = "Monthly" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/reports/generate", request);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<OperationAcceptedResponse>(_jsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.OperationId);
        Assert.Equal("ReportGeneration", body.OperationName);
        Assert.Equal(OperationState.Accepted, body.Status);
        Assert.Contains("/api/operations/", body.StatusCheckUrl);

        // Location header should be set
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task GetOperationStatus_ReturnsOperationDetails()
    {
        // Arrange: start an operation
        var request = new { ReportType = "Weekly" };
        var createResponse = await _client.PostAsJsonAsync("/api/reports/generate", request);
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>(_jsonOptions);

        // Act: check status
        var statusResponse = await _client.GetAsync($"/api/operations/{created!.OperationId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        var status = await statusResponse.Content.ReadFromJsonAsync<OperationStatusResponse>(_jsonOptions);
        Assert.NotNull(status);
        Assert.Equal(created.OperationId, status!.OperationId);
        Assert.Equal("ReportGeneration", status.OperationName);
    }

    [Fact]
    public async Task GetOperationStatus_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/api/operations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelOperation_ReturnsSuccess()
    {
        // Arrange: start an operation
        var request = new { Format = "CSV" };
        var createResponse = await _client.PostAsJsonAsync("/api/reports/export", request);
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>(_jsonOptions);

        // Act: cancel it
        var cancelResponse = await _client.PostAsync($"/api/operations/{created!.OperationId}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Verify status is Cancelled
        var statusResponse = await _client.GetAsync($"/api/operations/{created.OperationId}");
        var status = await statusResponse.Content.ReadFromJsonAsync<OperationStatusResponse>(_jsonOptions);
        Assert.Equal(OperationState.Cancelled, status!.Status);
    }

    [Fact]
    public async Task ListOperations_ReturnsResults()
    {
        // Arrange: create a couple of operations
        await _client.PostAsJsonAsync("/api/reports/generate", new { ReportType = "Test1" });
        await _client.PostAsJsonAsync("/api/reports/generate", new { ReportType = "Test2" });

        // Act
        var response = await _client.GetAsync("/api/operations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var operations = await response.Content.ReadFromJsonAsync<List<OperationStatusResponse>>(_jsonOptions);
        Assert.NotNull(operations);
        Assert.True(operations!.Count >= 2);
    }

    [Fact]
    public async Task NormalEndpoint_ReturnsDirectly_NotWrapped()
    {
        // This endpoint does NOT have [LongRunningOperation], should return 200 OK directly
        var response = await _client.GetAsync("/api/reports/quick-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OperationEventuallyCompletes_WithResult()
    {
        // Arrange
        var request = new { ReportType = "Quick" };
        var createResponse = await _client.PostAsJsonAsync("/api/reports/generate", request);
        var created = await createResponse.Content.ReadFromJsonAsync<OperationAcceptedResponse>(_jsonOptions);

        // Poll until complete (max 30 seconds)
        OperationStatusResponse? status = null;
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            var statusResponse = await _client.GetAsync($"/api/operations/{created!.OperationId}");
            status = await statusResponse.Content.ReadFromJsonAsync<OperationStatusResponse>(_jsonOptions);

            if (status!.Status is OperationState.Succeeded or OperationState.Failed)
                break;
        }

        Assert.NotNull(status);
        Assert.Equal(OperationState.Succeeded, status!.Status);
        Assert.Equal(100, status.PercentComplete);
        Assert.NotNull(status.ResultData);
    }
}
