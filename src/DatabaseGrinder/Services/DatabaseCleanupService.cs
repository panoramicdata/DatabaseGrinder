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
	/// Cleanup database resources including dropping database and read-only user
	/// </summary>
	public async Task CleanupDatabaseResourcesAsync()
	{
		logger.LogWarning("=== DATABASE CLEANUP STARTED ===");
		
		var builder = new NpgsqlConnectionStringBuilder(_settings.PrimaryConnection.ConnectionString);
		var databaseName = builder.Database;
		var readerUsername = _settings.DatabaseManagement.ReaderUsername;

		logger.LogWarning("Cleanup targets - Database: {DatabaseName}, Reader: {ReaderUsername}", 
			databaseName, readerUsername);

		// Build connection string to 'postgres' database for cleanup operations
		builder.Database = "postgres"; // Connect to postgres database to drop target database
		var adminConnectionString = builder.ToString();

		logger.LogInformation("Connecting to postgres database for cleanup operations...");

		await using var connection = new NpgsqlConnection(adminConnectionString);
		try
		{
			await connection.OpenAsync();
			logger.LogInformation("Connected to postgres database successfully");

			// Step 1: Terminate all connections to the target database
			logger.LogWarning("Step 1: Terminating connections to database: {DatabaseName}", databaseName);
			var terminateConnectionsQuery = $@"
                SELECT pg_terminate_backend(pid)
                FROM pg_stat_activity
                WHERE datname = '{databaseName}' AND pid <> pg_backend_pid()" ;

			using (var terminateCommand = new NpgsqlCommand(terminateConnectionsQuery, connection))
			{
				var terminatedCount = await terminateCommand.ExecuteNonQueryAsync();
				logger.LogInformation("Terminated {Count} connections to database {DatabaseName}", 
					terminatedCount, databaseName);
			}

			// Step 2: Drop the read-only user (if exists)
			logger.LogWarning("Step 2: Dropping read-only user: {Username}", readerUsername);
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
				logger.LogWarning("Read-only user drop command executed successfully");
			}

			// Step 3: Drop the database (if exists)
			logger.LogError("Step 3: DROPPING DATABASE: {DatabaseName} - THIS IS PERMANENT!", databaseName);
			var dropDatabaseQuery = $@"
                DROP DATABASE IF EXISTS ""{databaseName}""";

			using (var dropDbCommand = new NpgsqlCommand(dropDatabaseQuery, connection))
			{
				await dropDbCommand.ExecuteNonQueryAsync();
				logger.LogError("DATABASE DROPPED SUCCESSFULLY: {DatabaseName}", databaseName);
			}

			logger.LogWarning("=== DATABASE CLEANUP COMPLETED SUCCESSFULLY ===");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "=== DATABASE CLEANUP FAILED ===");
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