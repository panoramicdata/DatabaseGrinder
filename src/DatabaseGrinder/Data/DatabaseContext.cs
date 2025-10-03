using DatabaseGrinder.Models;
using Microsoft.EntityFrameworkCore;

namespace DatabaseGrinder.Data;

/// <summary>
/// Entity Framework Database Context for DatabaseGrinder
/// </summary>
/// <remarks>
/// Initializes a new instance of the DatabaseContext
/// </remarks>
/// <param name="options">The options for this context</param>
public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
	/// <summary>
	/// Test records table for replication monitoring
	/// </summary>
	public DbSet<TestRecord> TestRecords { get; set; } = null!;

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