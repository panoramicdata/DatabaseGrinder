using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text;
using DatabaseGrinder.Configuration;

namespace DatabaseGrinder.Services;

/// <summary>
/// Service responsible for setting up the database, users, and permissions
/// </summary>
public class DatabaseSetupService
{
    private readonly ILogger<DatabaseSetupService> _logger;
    private readonly DatabaseGrinderSettings _settings;

    public DatabaseSetupService(ILogger<DatabaseSetupService> logger, IOptions<DatabaseGrinderSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    /// <summary>
    /// Perform complete database setup including database creation, user management, and permissions
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if setup was successful</returns>
    public async Task<bool> SetupDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database setup process...");

            // Step 1: Validate superuser connection
            if (!await ValidateConnectionAsync(_settings.PrimaryConnection.ConnectionString, cancellationToken))
            {
                _logger.LogError("Failed to validate superuser connection");
                return false;
            }

            // Step 2: Create database if needed
            if (_settings.DatabaseManagement.AutoCreateDatabase)
            {
                if (!await EnsureDatabaseExistsAsync(cancellationToken))
                {
                    _logger.LogError("Failed to ensure database exists");
                    return false;
                }
            }

            // Step 3: Create reader user if needed
            if (_settings.DatabaseManagement.AutoCreateUsers)
            {
                if (!await EnsureReaderUserExistsAsync(cancellationToken))
                {
                    _logger.LogError("Failed to ensure reader user exists");
                    return false;
                }
            }

            // Step 4: Verify reader connection if requested
            if (_settings.DatabaseManagement.VerifyReaderConnection && _settings.ReplicaConnections.Count > 0)
            {
                if (!await VerifyReaderConnectionsAsync(cancellationToken))
                {
                    _logger.LogError("Reader connection verification failed");
                    return false;
                }
            }

            _logger.LogInformation("Database setup completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database setup failed with exception");
            return false;
        }
    }

    /// <summary>
    /// Validate that we can connect with the primary (superuser) connection
    /// </summary>
    private async Task<bool> ValidateConnectionAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Validating superuser connection...");
            
            // Use postgres database for initial validation to avoid database not found errors
            var postgresConnectionString = ModifyConnectionStringDatabase(connectionString, "postgres");
            
            using var connection = new NpgsqlConnection(postgresConnectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
            using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await connection.OpenAsync(combinedToken.Token);
            
            // Check if user has superuser privileges
            const string checkSuperUserQuery = "SELECT current_setting('is_superuser') = 'on' AS is_superuser;";
            using var command = new NpgsqlCommand(checkSuperUserQuery, connection);
            var isSuperUser = await command.ExecuteScalarAsync(combinedToken.Token) as bool?;
            
            if (isSuperUser != true)
            {
                _logger.LogError("Primary connection user does not have superuser privileges");
                return false;
            }

            _logger.LogInformation("Superuser connection validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate superuser connection");
            return false;
        }
    }

    /// <summary>
    /// Ensure the target database exists, create it if it doesn't
    /// </summary>
    private async Task<bool> EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var databaseName = ExtractDatabaseName(_settings.PrimaryConnection.ConnectionString);
            _logger.LogDebug("Checking if database '{DatabaseName}' exists...", databaseName);

            // Connect to postgres database to check if target database exists
            var postgresConnectionString = ModifyConnectionStringDatabase(_settings.PrimaryConnection.ConnectionString, "postgres");
            
            using var connection = new NpgsqlConnection(postgresConnectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
            using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await connection.OpenAsync(combinedToken.Token);

            // Check if database exists
            const string checkDbQuery = "SELECT 1 FROM pg_database WHERE datname = @dbname;";
            using var checkCommand = new NpgsqlCommand(checkDbQuery, connection);
            checkCommand.Parameters.AddWithValue("dbname", databaseName);
            
            var exists = await checkCommand.ExecuteScalarAsync(combinedToken.Token);
            
            if (exists == null)
            {
                _logger.LogInformation("Database '{DatabaseName}' does not exist, creating...", databaseName);
                
                // Create the database
                var createDbQuery = $"CREATE DATABASE \"{databaseName}\";";
                using var createCommand = new NpgsqlCommand(createDbQuery, connection);
                await createCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogInformation("Database '{DatabaseName}' created successfully", databaseName);
            }
            else
            {
                _logger.LogDebug("Database '{DatabaseName}' already exists", databaseName);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure database exists");
            return false;
        }
    }

    /// <summary>
    /// Ensure the reader user exists with appropriate permissions
    /// </summary>
    private async Task<bool> EnsureReaderUserExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var readerUsername = _settings.DatabaseManagement.ReaderUsername;
            var readerPassword = _settings.DatabaseManagement.ReaderPassword;
            var databaseName = ExtractDatabaseName(_settings.PrimaryConnection.ConnectionString);
            
            _logger.LogDebug("Ensuring reader user '{ReaderUsername}' exists...", readerUsername);

            using var connection = new NpgsqlConnection(_settings.PrimaryConnection.ConnectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
            using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await connection.OpenAsync(combinedToken.Token);

            // Check if role exists
            const string checkRoleQuery = "SELECT 1 FROM pg_roles WHERE rolname = @username;";
            using var checkCommand = new NpgsqlCommand(checkRoleQuery, connection);
            checkCommand.Parameters.AddWithValue("username", readerUsername);
            
            var roleExists = await checkCommand.ExecuteScalarAsync(combinedToken.Token);
            
            if (roleExists == null)
            {
                _logger.LogInformation("Creating reader user '{ReaderUsername}'...", readerUsername);
                
                // Create the role (password must be quoted in SQL)
                var createRoleQuery = $"CREATE ROLE \"{readerUsername}\" WITH LOGIN PASSWORD '{readerPassword}';";
                using var createRoleCommand = new NpgsqlCommand(createRoleQuery, connection);
                await createRoleCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogInformation("Reader user '{ReaderUsername}' created successfully", readerUsername);
            }
            else
            {
                _logger.LogDebug("Reader user '{ReaderUsername}' already exists", readerUsername);
                
                // Update password in case it changed (password must be quoted in SQL)
                var alterRoleQuery = $"ALTER ROLE \"{readerUsername}\" WITH PASSWORD '{readerPassword}';";
                using var alterRoleCommand = new NpgsqlCommand(alterRoleQuery, connection);
                await alterRoleCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogDebug("Reader user password updated");
            }

            // Grant necessary permissions
            await GrantReaderPermissionsAsync(connection, readerUsername, databaseName, combinedToken.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure reader user exists");
            return false;
        }
    }

    /// <summary>
    /// Grant appropriate permissions to the reader user
    /// </summary>
    private async Task GrantReaderPermissionsAsync(NpgsqlConnection connection, string readerUsername, string databaseName, CancellationToken cancellationToken)
    {
        var permissions = new[]
        {
            $"GRANT CONNECT ON DATABASE \"{databaseName}\" TO \"{readerUsername}\";",
            $"GRANT USAGE ON SCHEMA public TO \"{readerUsername}\";",
            $"GRANT SELECT ON ALL TABLES IN SCHEMA public TO \"{readerUsername}\";",
            $"GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO \"{readerUsername}\";",
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO \"{readerUsername}\";"
        };

        foreach (var permission in permissions)
        {
            try
            {
                using var command = new NpgsqlCommand(permission, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogDebug("Granted permission: {Permission}", permission);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to grant permission: {Permission}", permission);
                // Continue with other permissions
            }
        }

        _logger.LogInformation("Reader permissions granted to '{ReaderUsername}'", readerUsername);
    }

    /// <summary>
    /// Verify that reader connections work properly
    /// </summary>
    private async Task<bool> VerifyReaderConnectionsAsync(CancellationToken cancellationToken)
    {
        var allSuccessful = true;

        foreach (var replica in _settings.ReplicaConnections)
        {
            try
            {
                _logger.LogDebug("Verifying reader connection for '{ReplicaName}'...", replica.Name);
                
                using var connection = new NpgsqlConnection(replica.ConnectionString);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
                using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

                await connection.OpenAsync(combinedToken.Token);
                
                // Test a simple query
                const string testQuery = "SELECT 1;";
                using var command = new NpgsqlCommand(testQuery, connection);
                await command.ExecuteScalarAsync(combinedToken.Token);
                
                _logger.LogInformation("Reader connection verified for '{ReplicaName}'", replica.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to verify reader connection for '{ReplicaName}'", replica.Name);
                allSuccessful = false;
            }
        }

        return allSuccessful;
    }

    /// <summary>
    /// Extract database name from connection string
    /// </summary>
    private static string ExtractDatabaseName(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return builder.Database ?? "grinder_primary";
    }

    /// <summary>
    /// Modify connection string to use a different database
    /// </summary>
    private static string ModifyConnectionStringDatabase(string connectionString, string newDatabase)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = newDatabase
        };
        return builder.ToString();
    }
}