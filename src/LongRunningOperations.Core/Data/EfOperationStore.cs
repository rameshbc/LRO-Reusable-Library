using LongRunningOperations.Core.Interfaces;
using LongRunningOperations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LongRunningOperations.Core.Data;

/// <summary>
/// EF Core implementation of IOperationStore.
/// Uses the shared database so any API instance can read/write status.
/// </summary>
public class EfOperationStore : IOperationStore
{
    private readonly OperationDbContext _db;

    public EfOperationStore(OperationDbContext db)
    {
        _db = db;
    }

    public async Task<OperationStatus> CreateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        _db.Operations.Add(operation);
        await _db.SaveChangesAsync(ct);
        return operation;
    }

    public async Task<OperationStatus?> GetAsync(Guid operationId, CancellationToken ct = default)
    {
        return await _db.Operations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.OperationId == operationId, ct);
    }

    public async Task UpdateAsync(OperationStatus operation, CancellationToken ct = default)
    {
        operation.LastUpdatedAtUtc = DateTime.UtcNow;
        _db.Operations.Update(operation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OperationStatus>> GetTimedOutOperationsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Operations
            .Where(o => (o.State == OperationState.Accepted || o.State == OperationState.Running)
                        && o.CreatedAtUtc.AddSeconds(o.TimeoutSeconds) < now)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OperationStatus>> ListAsync(
        string? operationName = null,
        OperationState? state = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.Operations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(operationName))
            query = query.Where(o => o.OperationName == operationName);

        if (state.HasValue)
            query = query.Where(o => o.State == state.Value);

        return await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
