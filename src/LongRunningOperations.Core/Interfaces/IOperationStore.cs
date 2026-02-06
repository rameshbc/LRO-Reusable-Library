using LongRunningOperations.Core.Models;

namespace LongRunningOperations.Core.Interfaces;

/// <summary>
/// Abstraction for persisting operation status.
/// The default implementation uses EF Core with SQL Server,
/// but consumers can swap in Redis, CosmosDB, etc.
/// </summary>
public interface IOperationStore
{
    Task<OperationStatus> CreateAsync(OperationStatus operation, CancellationToken ct = default);
    Task<OperationStatus?> GetAsync(Guid operationId, CancellationToken ct = default);
    Task UpdateAsync(OperationStatus operation, CancellationToken ct = default);
    Task<IReadOnlyList<OperationStatus>> GetTimedOutOperationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OperationStatus>> ListAsync(string? operationName = null, OperationState? state = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default);
}
