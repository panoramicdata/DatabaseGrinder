using DatabaseGrinder.Configuration;
using DatabaseGrinder.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DatabaseGrinder.Services;

/// <summary>
/// Service responsible for cleaning up database resources and test data
/// </summary>
public class DatabaseCleanupService(
    IServiceProvider serviceProvider,
    ILogger<DatabaseCleanupService> logger,
    IOptions<DatabaseGrinderSettings> settings)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<DatabaseCleanupService> _logger = logger;
    private readonly DatabaseGrinderSettings _settings = settings.Value;

    /// <summary>
    /// Truncate the test_records table and reset sequences
    /// </summary>
    public async Task TruncateTableAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            
            // Set the schema name in the context
            context.SetSchemaName(_settings.DatabaseManagement.SchemaName);

            _logger.LogInformation("Truncating test_records table in schema '{SchemaName}'...", _settings.DatabaseManagement.SchemaName);

            // Use raw SQL to truncate and reset sequences
            var schemaName = _settings.DatabaseManagement.SchemaName;
            var truncateSql = $"TRUNCATE TABLE \"{schemaName}\".\"test_records\" RESTART IDENTITY CASCADE;";
            
            await context.Database.ExecuteSqlRawAsync(truncateSql);
            
            _logger.LogInformation("Table truncation completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to truncate table");
            throw;
        }
    }

    /// <summary>
    /// Clean up old test records based on retention policy
    /// </summary>
    public async Task CleanupOldRecordsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            
            // Set the schema name in the context
            context.SetSchemaName(_settings.DatabaseManagement.SchemaName);

            var cutoffTime = DateTime.UtcNow.AddMinutes(-_settings.Settings.DataRetentionMinutes);

            var oldRecords = await context.TestRecords
                .Where(r => r.Timestamp < cutoffTime)
                .ToListAsync();

            if (oldRecords.Count > 0)
            {
                context.TestRecords.RemoveRange(oldRecords);
                await context.SaveChangesAsync();

                _logger.LogInformation("Cleaned up {Count} old records older than {CutoffTime}", oldRecords.Count, cutoffTime);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old records");
            throw;
        }
    }

    /// <summary>
    /// Clean up DatabaseGrinder schema and reader user (destructive operation)
    /// This removes all DatabaseGrinder objects but preserves the main database
    /// </summary>
    public async Task CleanupDatabaseResourcesAsync()
    {
        try
        {
            var schemaName = _settings.DatabaseManagement.SchemaName;
            var readerUsername = _settings.DatabaseManagement.ReaderUsername;
            
            _logger.LogWarning("Starting destructive cleanup of DatabaseGrinder resources...");
            _logger.LogWarning("Schema to drop: {SchemaName}", schemaName);
            _logger.LogWarning("User to drop: {ReaderUsername}", readerUsername);

            using var connection = new NpgsqlConnection(_settings.PrimaryConnection.ConnectionString);
            await connection.OpenAsync();

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Step 1: Drop all objects in the schema
                _logger.LogInformation("Dropping schema '{SchemaName}' with all objects...", schemaName);
                var dropSchemaCommand = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;", connection, transaction);
                await dropSchemaCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Schema '{SchemaName}' dropped successfully", schemaName);

                // Step 2: Revoke permissions and drop reader user
                _logger.LogInformation("Cleaning up reader user '{ReaderUsername}'...", readerUsername);
                
                // First revoke all permissions (ignore errors if user doesn't exist)
                try
                {
                    var revokeCommand = new NpgsqlCommand($"REVOKE ALL PRIVILEGES ON DATABASE {connection.Database} FROM \"{readerUsername}\";", connection, transaction);
                    await revokeCommand.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error revoking database privileges (user may not exist)");
                }

                // Drop the reader user
                var dropUserCommand = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{readerUsername}\";", connection, transaction);
                await dropUserCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Reader user '{ReaderUsername}' removed successfully", readerUsername);

                // Commit the transaction
                await transaction.CommitAsync();
                
                _logger.LogInformation("DatabaseGrinder cleanup completed successfully");
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup DatabaseGrinder resources");
            throw;
        }
    }

    /// <summary>
    /// Get cleanup information for display purposes
    /// </summary>
    /// <returns>Cleanup information tuple (SchemaName, ReaderUsername, DatabaseName)</returns>
    public (string SchemaName, string ReaderUsername, string DatabaseName) GetCleanupInfo()
    {
        try
        {
            var databaseName = ExtractDatabaseName(_settings.PrimaryConnection.ConnectionString);
            return (_settings.DatabaseManagement.SchemaName, _settings.DatabaseManagement.ReaderUsername, databaseName);
        }
        catch
        {
            return (_settings.DatabaseManagement.SchemaName, _settings.DatabaseManagement.ReaderUsername, "unknown");
        }
    }

    /// <summary>
    /// Extract database name from connection string
    /// </summary>
    private static string ExtractDatabaseName(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return builder.Database ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}