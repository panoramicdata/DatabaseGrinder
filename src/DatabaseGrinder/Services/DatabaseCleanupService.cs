using DatabaseGrinder.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DatabaseGrinder.Services;

/// <summary>
/// Service for cleaning up database resources
/// </summary>
public class DatabaseCleanupService(
	ILogger<DatabaseCleanupService> logger,
	IOptions<DatabaseGrinderSettings> settings)
{
	private readonly DatabaseGrinderSettings _settings = settings.Value;

	/// <summary>
	/// Truncate the test_records table and reset the primary key sequence
	/// </summary>
	public async Task TruncateTableAsync()
	{
		try
		{
			using var connection = new NpgsqlConnection(_settings.PrimaryConnection.ConnectionString);
			await connection.OpenAsync();

			logger.LogInformation("Truncating test_records table and resetting sequence...");

			// Truncate table and restart identity (reset PK sequence)
			using var command = new NpgsqlCommand("TRUNCATE TABLE test_records RESTART IDENTITY", connection);
			await command.ExecuteNonQueryAsync();

			logger.LogInformation("Table truncated and sequence reset successfully");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to truncate table");
			throw;
		}
	}

	/// <summary>
	/// Clean up database resources: drop read-only user and database
	/// Note: Does NOT remove the main superuser
	/// </summary>
	public async Task CleanupDatabaseResourcesAsync()
	{
		try
		{
			logger.LogInformation("Starting database cleanup...");

			// Extract database name from connection string
			var builder = new NpgsqlConnectionStringBuilder(_settings.PrimaryConnection.ConnectionString);
			var databaseName = builder.Database;
			var readerUsername = _settings.DatabaseManagement.ReaderUsername;

			// Create connection to 'postgres' database (not the target database)
			builder.Database = "postgres";
			var masterConnectionString = builder.ToString();

			using var connection = new NpgsqlConnection(masterConnectionString);
			await connection.OpenAsync();

			// Step 1: Terminate all connections to the target database
			logger.LogInformation("Terminating connections to database: {DatabaseName}", databaseName);
			var terminateConnectionsQuery = $@"
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()";

			using (var terminateCommand = new NpgsqlCommand(terminateConnectionsQuery, connection))
			{
				await terminateCommand.ExecuteNonQueryAsync();
			}

			// Step 2: Drop the read-only user (if exists)
			logger.LogInformation("Dropping read-only user: {Username}", readerUsername);
			var dropUserQuery = $@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{readerUsername}') THEN
                        DROP ROLE ""{readerUsername}"";
                        RAISE NOTICE 'Role {readerUsername} dropped successfully';
                    ELSE
                        RAISE NOTICE 'Role {readerUsername} does not exist';
                    END IF;
                END
                $$;";

			using (var dropUserCommand = new NpgsqlCommand(dropUserQuery, connection))
			{
				await dropUserCommand.ExecuteNonQueryAsync();
			}

			// Step 3: Drop the database (if exists)
			logger.LogInformation("Dropping database: {DatabaseName}", databaseName);
			var dropDatabaseQuery = $@"
                DROP DATABASE IF EXISTS ""{databaseName}""";

			using (var dropDbCommand = new NpgsqlCommand(dropDatabaseQuery, connection))
			{
				await dropDbCommand.ExecuteNonQueryAsync();
			}

			logger.LogInformation("Database cleanup completed successfully");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to cleanup database resources");
			throw;
		}
	}

	/// <summary>
	/// Get cleanup summary information
	/// </summary>
	public (string DatabaseName, string ReaderUsername, string MainUsername) GetCleanupInfo()
	{
		var builder = new NpgsqlConnectionStringBuilder(_settings.PrimaryConnection.ConnectionString);
		return (
			DatabaseName: builder.Database ?? "unknown",
			_settings.DatabaseManagement.ReaderUsername,
			MainUsername: builder.Username ?? "unknown"
		);
	}
}