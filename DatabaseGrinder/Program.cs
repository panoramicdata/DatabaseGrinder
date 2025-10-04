using DatabaseGrinder.Configuration;
using DatabaseGrinder.Data;
using DatabaseGrinder.Services;
using DatabaseGrinder.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DatabaseGrinder;

/// <summary>
/// Main entry point for the DatabaseGrinder application
/// </summary>
internal partial class Program
{
	private static ConsoleManager? _consoleManager;
	private static IServiceProvider? _serviceProvider;

	/// <summary>
	/// Application entry point
	/// </summary>
	/// <param name="args">Command line arguments</param>
	/// <returns>Exit code</returns>
	public static async Task<int> Main(string[] args)
	{
		// Set up Ctrl+C handler for graceful shutdown
		Console.CancelKeyPress += OnConsoleCancel;

		try
		{
			// Set console encoding to UTF-8 to handle unicode characters
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			Console.InputEncoding = System.Text.Encoding.UTF8;

			// Set console title if supported by the OS
			if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
			{
				Console.Title = "Database Grinder";
			}

			// Clear console and immediately start UI (no splash screen)
			Console.Clear();
			Console.CursorVisible = false;

			var builder = Host.CreateApplicationBuilder(args);

			// Add configuration
			builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

			// Add services
			ConfigureServices(builder.Services, builder.Configuration);

			using var host = builder.Build();

			// Store service provider for cleanup operations
			_serviceProvider = host.Services;

			// Get logger for startup messages
			var logger = host.Services.GetRequiredService<ILogger<Program>>();
			logger.LogInformation("DatabaseGrinder v1.1.0 - Database Replication Monitor by Panoramic Data Limited");
			logger.LogInformation("Initializing application...");

			// Log console dimensions for debugging
			logger.LogInformation("Initial console size: {Width}x{Height}", Console.WindowWidth, Console.WindowHeight);

			// Validate configuration
			await ValidateConfigurationAsync(host.Services);

			// Setup database, users, and permissions
			await SetupDatabaseInfrastructureAsync(host.Services);

			// Ensure database is created and migrated
			await EnsureDatabaseAsync(host.Services);

			// Truncate table and reset sequence on startup
			await TruncateTableOnStartupAsync(host.Services);

			// Initialize console and start the application immediately
			await StartApplicationAsync(host.Services);

			// Run the hosted services (including DatabaseWriter and ReplicationMonitor)
			await host.RunAsync();

			return 0;
		}
		catch (OperationCanceledException)
		{
			// Expected during Ctrl+C or normal shutdown
			return 0;
		}
		catch (Exception ex)
		{
			// Clean up console first
			CleanupConsole();

			// Use basic console output for fatal errors since logging may not be available
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine("FATAL ERROR:");
			Console.ResetColor();
			Console.WriteLine(ex.Message);
			Console.WriteLine();
			Console.WriteLine("Press any key to exit...");
			Console.ReadKey();
			return 1;
		}
		finally
		{
			// Always clean up console on exit
			CleanupConsole();
		}
	}

	/// <summary>
	/// Handle Ctrl+C gracefully
	/// </summary>
	private static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
	{
		// Prevent immediate termination
		e.Cancel = true;

		// Clean up console
		CleanupConsole();

		// Show a nice exit message
		Console.WriteLine();
		Console.ForegroundColor = ConsoleColor.Yellow;
		Console.WriteLine("DatabaseGrinder shutting down gracefully...");
		Console.ResetColor();
		Console.WriteLine();

		// Allow the application to terminate normally
		Environment.Exit(0);
	}

	/// <summary>
	/// Handle Ctrl+Q for database cleanup and quit
	/// </summary>
	public static async Task HandleCleanupAndQuitAsync()
	{
		if (_serviceProvider == null)
		{
			Console.WriteLine("Service provider not available for cleanup");
			Environment.Exit(1);
			return;
		}

		try
		{
			using var scope = _serviceProvider.CreateScope();
			var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
			var cleanupService = scope.ServiceProvider.GetRequiredService<DatabaseCleanupService>();
			var hostLifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

			// Log shutdown initiation
			logger.LogWarning("Ctrl+Q pressed - Initiating shutdown with DatabaseGrinder cleanup");

			// Clean up console first
			CleanupConsole();

			// Show cleanup information
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("DatabaseGrinder - Shutdown with Cleanup");
			Console.WriteLine("=====================================");
			Console.ResetColor();

			var (SchemaName, ReaderUsername, DatabaseName) = cleanupService.GetCleanupInfo();

			Console.WriteLine("Stopping background services...");
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine("  • Stopping ReplicationMonitor service");
			Console.WriteLine("  • Stopping DatabaseWriter service");
			Console.ResetColor();

			// Stop background services first to prevent connection errors
			logger.LogWarning("Stopping background services before DatabaseGrinder cleanup");
			hostLifetime.StopApplication();

			// Give services time to shut down gracefully
			Console.WriteLine("Waiting for services to shut down...");
			await Task.Delay(2000);

			Console.WriteLine("Cleaning up DatabaseGrinder resources:");
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"  • Dropping schema: {SchemaName} (with all objects)");
			Console.WriteLine($"  • Dropping read-only user: {ReaderUsername}");
			Console.ResetColor();
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"  • Preserving database: {DatabaseName} (SAFE)");
			Console.ResetColor();
			Console.WriteLine();

			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine("Performing cleanup...");
			Console.ResetColor();

			logger.LogWarning("Performing DatabaseGrinder schema cleanup via Ctrl+Q");

			await cleanupService.CleanupDatabaseResourcesAsync();

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("✓ Cleanup completed successfully!");
			Console.ResetColor();
			Console.WriteLine();

			logger.LogInformation("DatabaseGrinder cleanup completed successfully");

			Console.WriteLine("DatabaseGrinder shutdown complete.");
			Console.WriteLine("Exiting in 2 seconds...");

			// Pause for 2 seconds as requested
			await Task.Delay(2000);
		}
		catch (Exception ex)
		{
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Cleanup failed: {ex.Message}");
			Console.ResetColor();
			Console.WriteLine("Exiting in 2 seconds...");
			await Task.Delay(2000);
		}

		Environment.Exit(0);
	}

	/// <summary>
	/// Clean up console and restore normal state
	/// </summary>
	private static void CleanupConsole()
	{
		try
		{
			// Force console manager to clean state if available
			_consoleManager?.ForceFullRedraw();

			// Clear the console
			Console.Clear();

			// Reset console properties
			Console.CursorVisible = true;
			Console.ResetColor();

			// Position cursor at top-left
			Console.SetCursorPosition(0, 0);
		}
		catch
		{
			// Ignore cleanup errors - we're shutting down anyway
		}
	}

	/// <summary>
	/// Configure dependency injection services
	/// </summary>
	/// <param name="services">Service collection</param>
	/// <param name="configuration">Application configuration</param>
	private static void ConfigureServices(IServiceCollection services, ConfigurationManager configuration)
	{
		// Add logging with custom configuration to prevent console interference
		services.AddLogging(builder =>
		{
			builder.ClearProviders(); // Clear default providers
			builder.AddConfiguration(configuration.GetSection("Logging"));
			// We'll handle console output through our UI system
		});

		// Add configuration
		services.Configure<DatabaseGrinderSettings>(configuration.GetSection("DatabaseGrinder"));

		// Add Entity Framework
		var primaryConnectionString = configuration["DatabaseGrinder:PrimaryConnection:ConnectionString"];
		if (string.IsNullOrEmpty(primaryConnectionString))
		{
			throw new InvalidOperationException("Primary connection string not found in configuration");
		}

		services.AddDbContext<DatabaseContext>(options =>
		{
			options.UseNpgsql(primaryConnectionString);
		});

		// Get console size from configuration
		var minWidth = configuration.GetValue<int>("DatabaseGrinder:Settings:MinConsoleWidth", 80);
		var minHeight = configuration.GetValue<int>("DatabaseGrinder:Settings:MinConsoleHeight", 25);

		// Add application services
		services.AddSingleton<ConsoleManager>(provider => new ConsoleManager(minWidth, minHeight));
		services.AddSingleton<LeftPane>();
		services.AddSingleton<RightPane>();
		services.AddSingleton<ReplicationStatsPane>(); // Add PostgreSQL replication stats pane
		services.AddScoped<DatabaseSetupService>();
		services.AddScoped<DatabaseCleanupService>();
		services.AddScoped<PostgreSQLReplicationStatsService>(); // Add PostgreSQL replication stats service

		// Add background services (order matters for dependencies)
		services.AddHostedService<DatabaseWriter>();
		services.AddHostedService<ReplicationMonitor>();
		services.AddHostedService<PostgreSQLReplicationMonitor>(); // Add PostgreSQL native replication monitoring
		services.AddHostedService<UIManager>();
	}

	/// <summary>
	/// Validate application configuration
	/// </summary>
	/// <param name="services">Service provider</param>
	private static async Task ValidateConfigurationAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

		// Load and validate configuration
		var settings = new DatabaseGrinderSettings();
		configuration.GetSection("DatabaseGrinder").Bind(settings);

		var validationErrors = ConfigurationValidator.Validate(settings);
		if (validationErrors.Count > 0)
		{
			logger.LogError("Configuration validation failed:");
			foreach (var error in validationErrors)
			{
				logger.LogError("  - {Error}", error);
			}

			throw new InvalidOperationException("Configuration validation failed. See log for details.");
		}

		logger.LogInformation("Configuration validated successfully");
		logger.LogInformation("Primary database: {PrimaryDb}", MaskConnectionString(settings.PrimaryConnection.ConnectionString));
		logger.LogInformation("Monitoring {ReplicaCount} replica(s)", settings.ReplicaConnections.Count);
		logger.LogInformation("Write interval: {WriteInterval}ms, UI refresh: {UIRefresh}ms",
			settings.Settings.WriteIntervalMs, settings.Settings.UIRefreshIntervalMs);

		await Task.CompletedTask;
	}

	/// <summary>
	/// Setup database infrastructure including database creation and user management
	/// </summary>
	/// <param name="services">Service provider</param>
	private static async Task SetupDatabaseInfrastructureAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var setupService = scope.ServiceProvider.GetRequiredService<DatabaseSetupService>();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

		try
		{
			logger.LogInformation("Setting up database infrastructure...");

			var success = await setupService.SetupDatabaseAsync();
			if (!success)
			{
				throw new InvalidOperationException("Database infrastructure setup failed. Check logs for details.");
			}

			logger.LogInformation("Database infrastructure setup completed successfully");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to setup database infrastructure");
			throw;
		}
	}

	/// <summary>
	/// Ensure database exists and is migrated to latest version
	/// </summary>
	/// <param name="services">Service provider</param>
	private static async Task EnsureDatabaseAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
		var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

		try
		{
			// Configure schema name in the context
			var schemaName = configuration.GetValue<string>("DatabaseGrinder:DatabaseManagement:SchemaName", "databasegrinder");
			context.SetSchemaName(schemaName);

			logger.LogInformation("Ensuring database schema '{SchemaName}' is up to date...", schemaName);
			await context.Database.MigrateAsync();
			logger.LogInformation("Database migrations applied successfully for schema '{SchemaName}'", schemaName);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to ensure database schema is ready");
			throw;
		}
	}

	/// <summary>
	/// Truncate the test_records table and reset the primary key sequence on startup
	/// </summary>
	/// <param name="services">Service provider</param>
	private static async Task TruncateTableOnStartupAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var cleanupService = scope.ServiceProvider.GetRequiredService<DatabaseCleanupService>();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

		try
		{
			logger.LogInformation("Truncating test_records table on startup...");
			await cleanupService.TruncateTableAsync();
			logger.LogInformation("Table truncation completed successfully");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to truncate table on startup");
			throw;
		}
	}

	/// <summary>
	/// Start the main application with console UI immediately
	/// </summary>
	/// <param name="services">Service provider</param>
	private static async Task StartApplicationAsync(IServiceProvider services)
	{
		using var scope = services.CreateScope();
		var consoleManager = scope.ServiceProvider.GetRequiredService<ConsoleManager>();
		var leftPane = scope.ServiceProvider.GetRequiredService<LeftPane>();
		var rightPane = scope.ServiceProvider.GetRequiredService<RightPane>();
		var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
		var setupService = scope.ServiceProvider.GetRequiredService<DatabaseSetupService>();

		try
		{
			// Store console manager for cleanup
			_consoleManager = consoleManager;

			// Initialize console immediately - no delay
			consoleManager.Initialize();

			// Get schema information for display
			var (schemaName, readerUsername, databaseName) = setupService.GetSchemaInfo();

			// Log detailed layout information
			logger.LogInformation("Console initialized. Size: {Width}x{Height}", consoleManager.Width, consoleManager.Height);
			logger.LogInformation("Layout: Left pane width: {LeftWidth}, Separator at X: {SepX}, Right pane start: {RightStart}, Right pane width: {RightWidth}",
				consoleManager.LeftPaneWidth, consoleManager.SeparatorX, consoleManager.RightPaneStartX, consoleManager.RightPaneWidth);
			logger.LogInformation("Platform: {Platform}", consoleManager.GetPlatformInfo());
			logger.LogInformation("Using schema '{SchemaName}' in database '{DatabaseName}'", schemaName, databaseName);

			// Initialize UI with startup messages including schema information
			leftPane.AddLogEntry("DatabaseGrinder v1.3.0 started");
			leftPane.AddLogEntry("Schema-based isolation for production database safety");
			leftPane.AddLogEntry("Enhanced with sequence tracking and missing row detection");
			leftPane.AddLogEntry($"Using schema: {schemaName} in database: {databaseName}");
			leftPane.AddLogEntry("Press Ctrl+C to exit gracefully");
			leftPane.AddLogEntry("Press Ctrl+Q for schema cleanup and quit");
			leftPane.AddLogEntry($"Console layout: {consoleManager.LeftPaneWidth} | {consoleManager.RightPaneWidth} chars");
			leftPane.AddLogEntry("Console UI initialized");
			leftPane.AddLogEntry("Schema setup completed");
			leftPane.AddLogEntry("Table truncated and sequence reset");
			leftPane.UpdateConnectionStatus("Ready - Starting services...", true);

			// Initialize replica placeholders (ReplicationMonitor will update these with real data)
			var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
			var settings = new DatabaseGrinderSettings();
			configuration.GetSection("DatabaseGrinder").Bind(settings);

			foreach (var replica in settings.ReplicaConnections)
			{
				rightPane.UpdateReplica(new ReplicaInfo
				{
					Name = replica.Name,
					Status = ConnectionStatus.Unknown,
					LastChecked = null
				});
			}

			logger.LogInformation("UI initialized successfully. Background services will start automatically.");
			leftPane.AddLogEntry("Starting database writer and replication monitor...");

			await Task.CompletedTask;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to start application");
			throw;
		}
	}

	/// <summary>
	/// Mask sensitive information in connection strings for logging
	/// </summary>
	/// <param name="connectionString">Connection string to mask</param>
	/// <returns>Masked connection string</returns>
	private static string MaskConnectionString(string connectionString) =>
		// Simple masking - replace password values
		ConnectionStringMaskRegex().Replace(connectionString, "$1***");

	[GeneratedRegex(@"(password=)[^;]+", RegexOptions.IgnoreCase, "en-GB")]
	private static partial Regex ConnectionStringMaskRegex();
}