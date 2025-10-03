using Microsoft.EntityFrameworkCore;
using DatabaseGrinder.Models;

namespace DatabaseGrinder.Data;

/// <summary>
/// Entity Framework Database Context for DatabaseGrinder
/// </summary>
public class DatabaseContext : DbContext
{
    /// <summary>
    /// Test records table for replication monitoring
    /// </summary>
    public DbSet<TestRecord> TestRecords { get; set; } = null!;

    /// <summary>
    /// Initializes a new instance of the DatabaseContext
    /// </summary>
    /// <param name="options">The options for this context</param>
    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options)
    {
    }

    /// <summary>
    /// Configures the model relationships and constraints
    /// </summary>
    /// <param name="modelBuilder">The builder being used to construct the model for this context</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure TestRecord entity
        modelBuilder.Entity<TestRecord>(entity =>
        {
            entity.ToTable("test_records");
            
            entity.HasKey(e => e.Id);
            
            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();
                
            entity.Property(e => e.Timestamp)
                .HasColumnName("timestamp")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            // Index on timestamp for efficient cleanup queries
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("ix_test_records_timestamp");
        });
    }
}