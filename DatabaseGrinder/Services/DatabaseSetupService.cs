using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using DatabaseGrinder.Configuration;

namespace DatabaseGrinder.Services;

/// <summary>
/// Service responsible for setting up the database schema, users, and permissions
/// </summary>
public class DatabaseSetupService(ILogger<DatabaseSetupService> logger, IOptions<DatabaseGrinderSettings> settings)
{
    private readonly ILogger<DatabaseSetupService> _logger = logger;
    private readonly DatabaseGrinderSettings _settings = settings.Value;

	/// <summary>
	/// Perform complete database setup including schema creation, user management, and permissions
	/// </summary>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>True if setup was successful</returns>
	public async Task<bool> SetupDatabaseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database setup process for schema '{SchemaName}'...", _settings.DatabaseManagement.SchemaName);

            // Step 1: Validate connection to target database
            if (!await ValidateConnectionAsync(_settings.PrimaryConnection.ConnectionString, cancellationToken))
            {
                _logger.LogError("Failed to validate database connection");
                return false;
            }

            // Step 2: Create schema if needed
            if (_settings.DatabaseManagement.AutoCreateSchema)
            {
                if (!await EnsureSchemaExistsAsync(cancellationToken))
                {
                    _logger.LogError("Failed to ensure schema exists");
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

            _logger.LogInformation("Database setup completed successfully for schema '{SchemaName}'", _settings.DatabaseManagement.SchemaName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database setup failed with exception");
            return false;
        }
    }

    /// <summary>
    /// Validate that we can connect to the target database
    /// </summary>
    private async Task<bool> ValidateConnectionAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Validating database connection...");
            
            using var connection = new NpgsqlConnection(connectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
            using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await connection.OpenAsync(combinedToken.Token);
            
            // Get database name and check basic connectivity
            const string checkDbQuery = "SELECT current_database(), current_user, version();";
            using var command = new NpgsqlCommand(checkDbQuery, connection);
            using var reader = await command.ExecuteReaderAsync(combinedToken.Token);
            
            if (await reader.ReadAsync(combinedToken.Token))
            {
                var dbName = reader.GetString(0);
                var userName = reader.GetString(1);
                var version = reader.GetString(2);
                
                _logger.LogInformation("Connected to database '{Database}' as user '{User}'", dbName, userName);
                _logger.LogDebug("PostgreSQL version: {Version}", version);
            }

            _logger.LogInformation("Database connection validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate database connection");
            return false;
        }
    }

    /// <summary>
    /// Ensure the target schema exists, create it if it doesn't
    /// </summary>
    private async Task<bool> EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var schemaName = _settings.DatabaseManagement.SchemaName;
            _logger.LogDebug("Checking if schema '{SchemaName}' exists...", schemaName);
            
            using var connection = new NpgsqlConnection(_settings.PrimaryConnection.ConnectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.DatabaseManagement.SetupTimeoutSeconds));
            using var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            await connection.OpenAsync(combinedToken.Token);

            // Check if schema exists
            const string checkSchemaQuery = "SELECT 1 FROM information_schema.schemata WHERE schema_name = @schemaname;";
            using var checkCommand = new NpgsqlCommand(checkSchemaQuery, connection);
            checkCommand.Parameters.AddWithValue("schemaname", schemaName);
            
            var exists = await checkCommand.ExecuteScalarAsync(combinedToken.Token);
            
            if (exists == null)
            {
                _logger.LogInformation("Schema '{SchemaName}' does not exist, creating...", schemaName);
                
                // Create the schema
                var createSchemaQuery = $"CREATE SCHEMA \"{schemaName}\";";
                using var createCommand = new NpgsqlCommand(createSchemaQuery, connection);
                await createCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogInformation("Schema '{SchemaName}' created successfully", schemaName);
            }
            else
            {
                _logger.LogDebug("Schema '{SchemaName}' already exists", schemaName);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure schema exists");
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
            var schemaName = _settings.DatabaseManagement.SchemaName;
            
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
                
                // Create the role with minimal permissions
                var createRoleQuery = $"CREATE ROLE \"{readerUsername}\" WITH LOGIN PASSWORD '{readerPassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE;";
                using var createRoleCommand = new NpgsqlCommand(createRoleQuery, connection);
                await createRoleCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogInformation("Reader user '{ReaderUsername}' created successfully", readerUsername);
            }
            else
            {
                _logger.LogDebug("Reader user '{ReaderUsername}' already exists", readerUsername);
                
                // Update password in case it changed
                var alterRoleQuery = $"ALTER ROLE \"{readerUsername}\" WITH PASSWORD '{readerPassword}';";
                using var alterRoleCommand = new NpgsqlCommand(alterRoleQuery, connection);
                await alterRoleCommand.ExecuteNonQueryAsync(combinedToken.Token);
                
                _logger.LogDebug("Reader user password updated");
            }

            // Grant necessary permissions for the schema
            await GrantReaderPermissionsAsync(connection, readerUsername, schemaName, combinedToken.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure reader user exists");
            return false;
        }
    }

    /// <summary>
    /// Grant appropriate permissions to the reader user for the DatabaseGrinder schema
    /// </summary>
    private async Task GrantReaderPermissionsAsync(NpgsqlConnection connection, string readerUsername, string schemaName, CancellationToken cancellationToken)
    {
        var permissions = new[]
        {
            // Basic database connection
            $"GRANT CONNECT ON DATABASE {connection.Database} TO \"{readerUsername}\";",
            
            // Schema usage permissions
            $"GRANT USAGE ON SCHEMA \"{schemaName}\" TO \"{readerUsername}\";",
            
            // Current tables and sequences in the schema
            $"GRANT SELECT ON ALL TABLES IN SCHEMA \"{schemaName}\" TO \"{readerUsername}\";",
            $"GRANT SELECT ON ALL SEQUENCES IN SCHEMA \"{schemaName}\" TO \"{readerUsername}\";",
            
            // Future tables and sequences in the schema
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schemaName}\" GRANT SELECT ON TABLES TO \"{readerUsername}\";",
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA \"{schemaName}\" GRANT SELECT ON SEQUENCES TO \"{readerUsername}\";"
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
                // Continue with other permissions - some may not be needed if objects don't exist yet
            }
        }

        _logger.LogInformation("Schema permissions granted to '{ReaderUsername}' for schema '{SchemaName}'", readerUsername, schemaName);
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
                
                // Test a simple query and schema access
                const string testQuery = "SELECT 1;";
                using var command = new NpgsqlCommand(testQuery, connection);
                await command.ExecuteScalarAsync(combinedToken.Token);
                
                // Test schema access if schema exists
                var schemaTestQuery = $"SELECT 1 FROM information_schema.schemata WHERE schema_name = @schemaname;";
                using var schemaCommand = new NpgsqlCommand(schemaTestQuery, connection);
                schemaCommand.Parameters.AddWithValue("schemaname", _settings.DatabaseManagement.SchemaName);
                var schemaExists = await schemaCommand.ExecuteScalarAsync(combinedToken.Token);
                
                if (schemaExists != null)
                {
                    _logger.LogDebug("Reader can access schema '{SchemaName}' on '{ReplicaName}'", _settings.DatabaseManagement.SchemaName, replica.Name);
                }
                
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
    /// Get information about the schema setup for display purposes
    /// </summary>
    /// <returns>Schema information tuple</returns>
    public (string SchemaName, string ReaderUsername, string DatabaseName) GetSchemaInfo()
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