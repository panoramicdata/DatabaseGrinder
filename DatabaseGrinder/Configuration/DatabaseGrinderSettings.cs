namespace DatabaseGrinder.Configuration;

/// <summary>
/// Configuration settings for DatabaseGrinder application
/// </summary>
public class DatabaseGrinderSettings
{
	/// <summary>
	/// Primary database connection configuration
	/// </summary>
	public PrimaryConnectionSettings PrimaryConnection { get; set; } = new();

	/// <summary>
	/// List of replica database connections to monitor
	/// </summary>
	public List<ReplicaConnectionSettings> ReplicaConnections { get; set; } = [];

	/// <summary>
	/// Database management and setup configuration
	/// </summary>
	public DatabaseManagementSettings DatabaseManagement { get; set; } = new();

	/// <summary>
	/// Application runtime settings
	/// </summary>
	public RuntimeSettings Settings { get; set; } = new();
}

/// <summary>
/// Primary database connection settings
/// </summary>
public class PrimaryConnectionSettings
{
	/// <summary>
	/// Connection string for the primary database (read/write access)
	/// </summary>
	public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Replica database connection settings
/// </summary>
public class ReplicaConnectionSettings
{
	/// <summary>
	/// Display name for the replica connection
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Connection string for the replica database (read-only access)
	/// </summary>
	public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Database management and setup settings
/// </summary>
public class DatabaseManagementSettings
{
	/// <summary>
	/// Schema name to use for DatabaseGrinder objects (default: databasegrinder)
	/// Using a dedicated schema allows safe usage on existing production databases
	/// </summary>
	public string SchemaName { get; set; } = "databasegrinder";

	/// <summary>
	/// Whether to automatically create the schema if it doesn't exist (default: true)
	/// </summary>
	public bool AutoCreateSchema { get; set; } = true;

	/// <summary>
	/// Whether to automatically create database users if they don't exist (default: true)
	/// </summary>
	public bool AutoCreateUsers { get; set; } = true;

	/// <summary>
	/// Username for the reader role (default: DatabaseGrinderReader)
	/// </summary>
	public string ReaderUsername { get; set; } = "DatabaseGrinderReader";

	/// <summary>
	/// Password for the reader role (default: readpass)
	/// </summary>
	public string ReaderPassword { get; set; } = "readpass";

	/// <summary>
	/// Whether to verify reader connection after setup (default: true)
	/// </summary>
	public bool VerifyReaderConnection { get; set; } = true;

	/// <summary>
	/// Maximum time to wait for database operations in seconds (default: 30)
	/// </summary>
	public int SetupTimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Runtime configuration settings
/// </summary>
public class RuntimeSettings
{
	/// <summary>
	/// Interval between database writes in milliseconds (default: 100ms)
	/// </summary>
	public int WriteIntervalMs { get; set; } = 100;

	/// <summary>
	/// How long to retain test data in minutes (default: 5 minutes)
	/// </summary>
	public int DataRetentionMinutes { get; set; } = 5;

	/// <summary>
	/// UI refresh interval in milliseconds (default: 800ms)
	/// </summary>
	public int UIRefreshIntervalMs { get; set; } = 800;

	/// <summary>
	/// Minimum console width to support (default: 20)
	/// </summary>
	public int MinConsoleWidth { get; set; } = 20;

	/// <summary>
	/// Minimum console height to support (default: 20)
	/// </summary>
	public int MinConsoleHeight { get; set; } = 20;

	/// <summary>
	/// Connection timeout in seconds (default: 30)
	/// </summary>
	public int ConnectionTimeoutSeconds { get; set; } = 30;

	/// <summary>
	/// Query timeout in seconds (default: 10)
	/// </summary>
	public int QueryTimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// Configuration validator to ensure settings are valid
/// </summary>
public static class ConfigurationValidator
{
	/// <summary>
	/// Validate the DatabaseGrinder configuration
	/// </summary>
	/// <param name="settings">Settings to validate</param>
	/// <returns>List of validation errors (empty if valid)</returns>
	public static List<string> Validate(DatabaseGrinderSettings settings)
	{
		var errors = new List<string>();

		// Validate primary connection
		if (string.IsNullOrWhiteSpace(settings.PrimaryConnection.ConnectionString))
		{
			errors.Add("Primary connection string is required");
		}

		// Validate replica connections
		if (settings.ReplicaConnections.Count == 0)
		{
			errors.Add("At least one replica connection must be configured");
		}

		for (int i = 0; i < settings.ReplicaConnections.Count; i++)
		{
			var replica = settings.ReplicaConnections[i];

			if (string.IsNullOrWhiteSpace(replica.Name))
			{
				errors.Add($"Replica connection {i + 1} must have a name");
			}

			if (string.IsNullOrWhiteSpace(replica.ConnectionString))
			{
				errors.Add($"Replica connection '{replica.Name}' must have a connection string");
			}
		}

		// Validate duplicate replica names
		var duplicateNames = settings.ReplicaConnections
			.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key);

		foreach (var duplicateName in duplicateNames)
		{
			errors.Add($"Duplicate replica name found: '{duplicateName}'");
		}

		// Validate runtime settings
		if (settings.Settings.WriteIntervalMs < 10)
		{
			errors.Add("WriteIntervalMs must be at least 10 milliseconds");
		}

		if (settings.Settings.DataRetentionMinutes < 1)
		{
			errors.Add("DataRetentionMinutes must be at least 1 minute");
		}

		if (settings.Settings.UIRefreshIntervalMs < 100)
		{
			errors.Add("UIRefreshIntervalMs must be at least 100 milliseconds");
		}

		if (settings.Settings.MinConsoleWidth < 20)
		{
			errors.Add("MinConsoleWidth must be at least 20");
		}

		if (settings.Settings.MinConsoleHeight < 10)
		{
			errors.Add("MinConsoleHeight must be at least 10");
		}

		if (settings.Settings.ConnectionTimeoutSeconds < 1)
		{
			errors.Add("ConnectionTimeoutSeconds must be at least 1 second");
		}

		if (settings.Settings.QueryTimeoutSeconds < 1)
		{
			errors.Add("QueryTimeoutSeconds must be at least 1 second");
		}

		// Validate database management settings
		if (string.IsNullOrWhiteSpace(settings.DatabaseManagement.SchemaName))
		{
			errors.Add("SchemaName is required");
		}
		else if (!IsValidSchemaName(settings.DatabaseManagement.SchemaName))
		{
			errors.Add("SchemaName must be a valid PostgreSQL identifier");
		}

		if (settings.DatabaseManagement.AutoCreateUsers)
		{
			if (string.IsNullOrWhiteSpace(settings.DatabaseManagement.ReaderUsername))
			{
				errors.Add("ReaderUsername is required when AutoCreateUsers is enabled");
			}

			if (string.IsNullOrWhiteSpace(settings.DatabaseManagement.ReaderPassword))
			{
				errors.Add("ReaderPassword is required when AutoCreateUsers is enabled");
			}
		}

		if (settings.DatabaseManagement.SetupTimeoutSeconds < 1)
		{
			errors.Add("SetupTimeoutSeconds must be at least 1 second");
		}

		return errors;
	}

	/// <summary>
	/// Validate PostgreSQL schema name
	/// </summary>
	/// <param name="schemaName">Schema name to validate</param>
	/// <returns>True if valid</returns>
	private static bool IsValidSchemaName(string schemaName)
	{
		// PostgreSQL identifier rules: start with letter or underscore, followed by letters, digits, underscores, or dollar signs
		// Length limit of 63 bytes (we'll be conservative and use characters)
		if (string.IsNullOrWhiteSpace(schemaName) || schemaName.Length > 63)
		{
			return false;
		}

		if (!char.IsLetter(schemaName[0]) && schemaName[0] != '_')
		{
			return false;
		}

		return schemaName.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '$');
	}
}