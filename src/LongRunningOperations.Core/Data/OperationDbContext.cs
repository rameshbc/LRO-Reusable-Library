using LongRunningOperations.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LongRunningOperations.Core.Data;

/// <summary>
/// EF Core DbContext for the shared operation status table.
/// All API instances point to the same database for consistent status checks.
/// </summary>
public class OperationDbContext : DbContext
{
    public DbSet<OperationStatus> Operations => Set<OperationStatus>();

    public OperationDbContext(DbContextOptions<OperationDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OperationStatus>(entity =>
        {
            entity.ToTable("LroOperations");
            entity.HasKey(e => e.OperationId);

            entity.Property(e => e.OperationId)
                .ValueGeneratedNever();

            entity.Property(e => e.OperationName)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.State)
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(2000);

            entity.Property(e => e.ProcessingInstanceId)
                .HasMaxLength(256);

            entity.Property(e => e.CreatedBy)
                .HasMaxLength(256);

            entity.Property(e => e.CorrelationId)
                .HasMaxLength(256);

            // Indexes for common queries
            entity.HasIndex(e => e.State);
            entity.HasIndex(e => e.OperationName);
            entity.HasIndex(e => new { e.State, e.CreatedAtUtc });
        });
    }
}
