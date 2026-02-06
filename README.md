# LongRunningOperations.Core

A reusable .NET 8 library for managing long-running API operations using a simple attribute-based approach. Decorate any controller action with `[LongRunningOperation]` — the framework handles 202 Accepted responses, background execution, progress tracking, cancellation, and multi-instance status polling automatically.

## Features

- **Attribute-based** — Add `[LongRunningOperation("Name")]` to any action method
- **Instant 202 Accepted** — Clients get an immediate response with an operation ID and status URL
- **Progress tracking** — Report percentage progress from your background work
- **Result retrieval** — Store and retrieve operation results (JSON) when complete
- **Cancellation** — Built-in cooperative cancellation support
- **Timeout management** — Automatic detection and marking of timed-out operations
- **Multi-instance safe** — Status can be checked from any API instance (with SQL Server or Redis)
- **Pluggable storage** — Choose InMemory, SQL Server, or Redis via configuration
- **Built-in status API** — Ships with `/api/operations` controller for status/cancel/list
- **One-line registration** — `services.AddLongRunningOperations(configuration)`

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│  Client (Angular App / Any HTTP Client)                             │
│                                                                     │
│  1. POST /api/reports/generate  ──►  202 Accepted + operationId     │
│  2. GET  /api/operations/{id}   ──►  { state, percentComplete }     │
│  3. GET  /api/operations/{id}   ──►  { state: "Succeeded", result } │
│  4. POST /api/operations/{id}/cancel  (optional)                    │
└─────────────────────────────────────────────────────────────────────┘
         │                    ▲
         ▼                    │
┌─────────────────────────────────────────────────────────────────────┐
│  ASP.NET Core API (any number of instances)                         │
│                                                                     │
│  ┌──────────────────────┐    ┌─────────────────────────────┐        │
│  │ [LongRunningOperation] ──► Resource Filter               │        │
│  │  Attribute            │    │  • Captures request body     │        │
│  │                       │    │  • Returns 202 immediately   │        │
│  └──────────────────────┘    │  • Fires background Task     │        │
│                               └─────────────────────────────┘        │
│                                                                      │
│  ┌──────────────────────┐    ┌─────────────────────────────┐        │
│  │ OperationManager     │    │ OperationTimeoutService      │        │
│  │  (Singleton)         │    │  (BackgroundService)          │        │
│  └──────────────────────┘    └─────────────────────────────┘        │
│                  │                        │                          │
│                  ▼                        ▼                          │
│  ┌──────────────────────────────────────────────────────────┐       │
│  │ IOperationStore (pluggable)                               │       │
│  │  ├── InMemoryOperationStore  (dev/testing)                │       │
│  │  ├── EfOperationStore        (SQL Server)                 │       │
│  │  └── RedisOperationStore     (Azure Redis Cache)          │       │
│  └──────────────────────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────────────────────┘
```

## Quick Start

### 1. Add the library reference

```xml
<ProjectReference Include="../LongRunningOperations.Core/LongRunningOperations.Core.csproj" />
```

### 2. Register in Program.cs

```csharp
using LongRunningOperations.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// One line to register everything
builder.Services.AddLongRunningOperations(builder.Configuration);

var app = builder.Build();
app.MapControllers();
app.Run();
```

### 3. Decorate your action

```csharp
[HttpPost("generate")]
[LongRunningOperation("ReportGeneration", TimeoutSeconds = 300)]
public async Task GenerateReport(
    [FromBody] ReportRequest request,
    IOperationContext operationContext)
{
    for (int i = 0; i <= 100; i += 10)
    {
        operationContext.CancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(1000, operationContext.CancellationToken);
        await operationContext.ReportProgressAsync(i);
    }

    await operationContext.SetResultAsync(new
    {
        ReportType = request.ReportType,
        RowCount = 15000,
        GeneratedAt = DateTime.UtcNow
    });
}
```

### 4. Configure storage in appsettings.json

```json
{
  "LongRunningOperations": {
    "StorageProvider": "InMemory"
  }
}
```

That's it. Your endpoint now returns `202 Accepted` with an operation ID, and clients can poll `/api/operations/{id}` for status.

## Storage Providers

### InMemory (Default)

Uses a `ConcurrentDictionary` — no external dependencies.

```json
{
  "LongRunningOperations": {
    "StorageProvider": "InMemory"
  }
}
```

| Aspect | Detail |
|---|---|
| Multi-instance | ❌ Single instance only |
| Persistence | ❌ Lost on restart |
| Dependencies | None |
| Best for | Development, testing, single-instance apps |

### SQL Server

Uses Entity Framework Core with automatic migration support.

```json
{
  "LongRunningOperations": {
    "StorageProvider": "SqlServer",
    "ConnectionString": "Server=localhost;Database=MyApp;Trusted_Connection=true;TrustServerCertificate=true;",
    "AutoMigrate": true
  }
}
```

| Aspect | Detail |
|---|---|
| Multi-instance | ✅ All instances share the database |
| Persistence | ✅ Survives restarts |
| Dependencies | SQL Server / Azure SQL |
| Best for | Production with existing SQL infrastructure |

### Azure Redis Cache

Uses `IDistributedCache` — compatible with Azure Redis, local Redis, and other Redis-compatible services.

```json
{
  "LongRunningOperations": {
    "StorageProvider": "Redis",
    "RedisConnectionString": "your-cache.redis.cache.windows.net:6380,password=your-key,ssl=True,abortConnect=False"
  }
}
```

| Aspect | Detail |
|---|---|
| Multi-instance | ✅ All instances share the cache |
| Persistence | ✅ Depends on Redis AOF/RDB config |
| Dependencies | Azure Redis Cache or any Redis server |
| Best for | High-performance multi-instance deployments |

## Configuration Reference

All options go under the `"LongRunningOperations"` section in `appsettings.json`:

| Property | Type | Default | Description |
|---|---|---|---|
| `StorageProvider` | `string` | `"InMemory"` | Storage backend: `"InMemory"`, `"SqlServer"`, or `"Redis"` |
| `ConnectionString` | `string` | `""` | SQL Server connection string (required for SqlServer provider) |
| `RedisConnectionString` | `string` | `""` | Redis connection string (required for Redis provider) |
| `InstanceId` | `string` | auto-generated | Unique ID for this API instance |
| `DefaultRetryAfterSeconds` | `int` | `5` | Polling interval hint returned to clients |
| `TimeoutCheckIntervalSeconds` | `int` | `60` | How often the background service checks for timed-out operations |
| `BaseUrl` | `string` | `null` | Override base URL for status check URLs (useful behind load balancers) |
| `AutoMigrate` | `bool` | `true` | Auto-apply EF Core migrations at startup (SqlServer only) |

## Attribute Options

```csharp
[LongRunningOperation(
    "DataExport",               // OperationName (required)
    TimeoutSeconds = 600,       // Auto-timeout after 10 minutes (default: 3600)
    AllowCancellation = true,   // Enable cancel endpoint (default: true)
    EstimatedDurationSeconds = 60  // Hint for clients (default: 0)
)]
```

## IOperationContext API

Injected into your action method as a parameter:

| Member | Description |
|---|---|
| `OperationId` | Unique GUID for this operation |
| `CancellationToken` | Triggered when cancellation is requested |
| `ReportProgressAsync(int percent)` | Update progress (0–100) |
| `SetResultAsync(object result)` | Store final result (serialized as JSON) |
| `SetFailedAsync(string message, string? details)` | Mark as failed with error info |

## Built-in API Endpoints

The library automatically registers these endpoints:

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/operations/{id}` | Get operation status, progress, and result |
| `POST` | `/api/operations/{id}/cancel` | Request cancellation |
| `GET` | `/api/operations` | List operations (supports `?operationName=`, `?state=`, `?page=`, `?pageSize=`) |

### Response Examples

**202 Accepted (on initial POST):**
```json
{
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "operationName": "ReportGeneration",
  "status": "Accepted",
  "statusCheckUrl": "http://localhost:5155/api/operations/3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "retryAfterSeconds": 5,
  "estimatedDurationSeconds": 30
}
```

**GET /api/operations/{id} (in progress):**
```json
{
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "operationName": "ReportGeneration",
  "state": "Running",
  "percentComplete": 60,
  "createdAtUtc": "2024-01-15T10:30:00Z",
  "lastUpdatedAtUtc": "2024-01-15T10:30:45Z",
  "resultData": null
}
```

**GET /api/operations/{id} (completed):**
```json
{
  "operationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "operationName": "ReportGeneration",
  "state": "Succeeded",
  "percentComplete": 100,
  "createdAtUtc": "2024-01-15T10:30:00Z",
  "completedAtUtc": "2024-01-15T10:31:30Z",
  "resultData": "{\"reportType\":\"Monthly\",\"rowCount\":15000,\"downloadUrl\":\"/api/reports/download/3fa85f64...\"}"
}
```

## Project Structure

```
LROAPI/
├── LROAPI.sln
├── README.md
├── src/
│   ├── LongRunningOperations.Core/       # The reusable library (DLL)
│   │   ├── Attributes/
│   │   │   └── LongRunningOperationAttribute.cs
│   │   ├── Controllers/
│   │   │   └── OperationsController.cs    # Built-in status/cancel/list API
│   │   ├── Data/
│   │   │   ├── InMemoryOperationStore.cs  # ConcurrentDictionary store
│   │   │   ├── EfOperationStore.cs        # EF Core / SQL Server store
│   │   │   ├── RedisOperationStore.cs     # Redis / IDistributedCache store
│   │   │   └── OperationDbContext.cs      # EF Core DbContext
│   │   ├── Extensions/
│   │   │   └── ServiceCollectionExtensions.cs  # One-line DI registration
│   │   ├── Filters/
│   │   │   └── LongRunningOperationFilter.cs   # IAsyncResourceFilter
│   │   ├── Interfaces/
│   │   │   ├── IOperationContext.cs
│   │   │   └── IOperationStore.cs
│   │   ├── Models/
│   │   │   ├── LroOptions.cs              # Configuration + StorageProviderType enum
│   │   │   ├── OperationResponse.cs       # DTOs
│   │   │   └── OperationStatus.cs         # Entity model
│   │   └── Services/
│   │       ├── OperationContext.cs
│   │       ├── OperationManager.cs
│   │       └── OperationTimeoutService.cs
│   └── SampleApi/                         # Sample API for validation
│       ├── Controllers/
│       │   └── ReportsController.cs       # Example endpoints
│       ├── Program.cs
│       └── appsettings.Development.json
├── tests/
│   └── LongRunningOperations.Tests/       # Integration tests (7 tests)
│       └── LongRunningOperationIntegrationTests.cs
└── lro-client/                            # Angular 21 dashboard app
    └── src/
        ├── app/
        │   ├── dashboard/                 # Main dashboard component
        │   ├── models/                    # TypeScript interfaces
        │   └── services/                  # Angular HTTP service with polling
        └── environments/
```

## Multi-Instance Deployment

For multi-instance deployments (load-balanced APIs), use SQL Server or Redis so all instances share operation state:

```
                    ┌─────────────┐
                    │ Load Balancer│
                    └──────┬──────┘
                   ┌───────┼───────┐
                   ▼       ▼       ▼
              Instance1  Instance2  Instance3
                   │       │       │
                   └───────┼───────┘
                           ▼
                  ┌─────────────────┐
                  │  SQL Server or  │
                  │  Azure Redis    │
                  └─────────────────┘
```

**How it works:**
1. Client sends POST to Instance 1 → gets `202 Accepted` with `operationId`
2. Instance 1 starts processing in the background
3. Client's next poll hits Instance 2 (via load balancer) → Instance 2 reads status from shared store
4. Operation completes on Instance 1, stores result in shared store
5. Client polls Instance 3 → gets `Succeeded` with result data

Each instance automatically gets a unique `InstanceId` so you can track which instance is processing each operation.

## Angular Dashboard

The included Angular app (`lro-client/`) provides a UI for testing the full flow:

```bash
cd lro-client
npm install
ng serve
```

Open http://localhost:4200 to start operations, watch live progress, view results, and cancel operations.

**Make sure the API is running:**
```bash
cd src/SampleApi
dotnet run
```

## Running Tests

```bash
dotnet test --verbosity normal
```

All 7 integration tests use `WebApplicationFactory` — no external database or server needed. Tests automatically use the InMemory storage provider.

## Custom Storage Provider

Implement `IOperationStore` and register it:

```csharp
public class MyCustomStore : IOperationStore
{
    public Task<OperationStatus> CreateAsync(OperationStatus operation, CancellationToken ct = default) { ... }
    public Task<OperationStatus?> GetAsync(Guid operationId, CancellationToken ct = default) { ... }
    public Task UpdateAsync(OperationStatus operation, CancellationToken ct = default) { ... }
    public Task<IReadOnlyList<OperationStatus>> GetTimedOutOperationsAsync(CancellationToken ct = default) { ... }
    public Task<IReadOnlyList<OperationStatus>> ListAsync(string? operationName, OperationState? state, int page, int pageSize, CancellationToken ct = default) { ... }
}

// In Program.cs — register after AddLongRunningOperations
builder.Services.AddLongRunningOperations(builder.Configuration);
builder.Services.AddSingleton<IOperationStore, MyCustomStore>(); // Overrides the default
```

## Tech Stack

- **.NET 8** — ASP.NET Core Web API
- **Entity Framework Core 8** — SQL Server storage provider
- **StackExchange.Redis** — Redis storage provider (via `IDistributedCache`)
- **xUnit + WebApplicationFactory** — Integration testing
- **Angular 21** — Dashboard UI (optional)

## License

MIT
