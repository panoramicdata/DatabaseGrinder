using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DatabaseGrinder.Data;
using DatabaseGrinder.Configuration;
using DatabaseGrinder.Services;
using DatabaseGrinder.UI;

namespace DatabaseGrinder;

/// <summary>
/// Main entry point for the DatabaseGrinder application
/// </summary>
internal class Program
{
    /// <summary>
    /// Application entry point
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Set console encoding to UTF-8 to handle unicode characters
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;

            // Clear console and immediately start UI (no splash screen)
            Console.Clear();
            Console.CursorVisible = false;

            var builder = Host.CreateApplicationBuilder(args);

            // Add configuration
            builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            // Add services
            ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();

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

            // Initialize console and start the application immediately
            await StartApplicationAsync(host.Services);

            // Run the hosted services (including DatabaseWriter and ReplicationMonitor)
            await host.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            // Use basic console output for fatal errors since logging may not be available
            Console.WriteLine();
            Console.WriteLine("FATAL ERROR:");
            Console.WriteLine(ex.Message);
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            return 1;
        }
    }

    /// <summary>
    /// Configure dependency injection services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
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

        // Add application services
        services.AddSingleton<ConsoleManager>();
        services.AddSingleton<LeftPane>();
        services.AddSingleton<RightPane>();
        services.AddScoped<DatabaseSetupService>();
        
        // Add background services (order matters for dependencies)
        services.AddHostedService<DatabaseWriter>();
        services.AddHostedService<ReplicationMonitor>();
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
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Ensuring database exists and is up to date...");
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure database is ready");
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

        try
        {
            // Initialize console immediately - no delay
            consoleManager.Initialize();
            
            // Log detailed layout information
            logger.LogInformation("Console initialized. Size: {Width}x{Height}", consoleManager.Width, consoleManager.Height);
            logger.LogInformation("Layout: Left pane width: {LeftWidth}, Separator at X: {SepX}, Right pane start: {RightStart}, Right pane width: {RightWidth}",
                consoleManager.LeftPaneWidth, consoleManager.SeparatorX, consoleManager.RightPaneStartX, consoleManager.RightPaneWidth);
            logger.LogInformation("Platform: {Platform}", consoleManager.GetPlatformInfo());

            // Initialize UI with startup messages
            leftPane.AddLogEntry("DatabaseGrinder v1.1.0 started", LogLevel.Information);
            leftPane.AddLogEntry("Enhanced with sequence tracking and missing row detection", LogLevel.Information);
            leftPane.AddLogEntry($"Console layout: {consoleManager.LeftPaneWidth} | {consoleManager.RightPaneWidth} chars", LogLevel.Information);
            leftPane.AddLogEntry("Console UI initialized", LogLevel.Information);
            leftPane.AddLogEntry("Database setup completed", LogLevel.Information);
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
            leftPane.AddLogEntry("Starting database writer and replication monitor...", LogLevel.Information);
            
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
    private static string MaskConnectionString(string connectionString)
    {
        // Simple masking - replace password values
        return System.Text.RegularExpressions.Regex.Replace(
            connectionString, 
            @"(password=)[^;]+", 
            "$1***", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}