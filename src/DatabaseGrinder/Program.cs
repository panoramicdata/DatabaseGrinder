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
            Console.WriteLine("DatabaseGrinder v1.0 - Database Replication Monitor");
            Console.WriteLine("Initializing...");

            var builder = Host.CreateApplicationBuilder(args);

            // Add configuration
            builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            // Add services
            ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();

            // Validate configuration
            await ValidateConfigurationAsync(host.Services);

            // Setup database, users, and permissions
            await SetupDatabaseInfrastructureAsync(host.Services);

            // Ensure database is created and migrated
            await EnsureDatabaseAsync(host.Services);

            // Initialize console and start the application
            await StartApplicationAsync(host.Services);

            // Run the hosted services (including DatabaseWriter and ReplicationMonitor)
            await host.RunAsync();

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
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
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddConfiguration(configuration.GetSection("Logging"));
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
            logger.LogInformation("Database ready");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure database is ready");
            throw;
        }
    }

    /// <summary>
    /// Start the main application with console UI
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
            // Initialize console
            consoleManager.Initialize();
            
            logger.LogInformation("Console initialized. Size: {Width}x{Height}", consoleManager.Width, consoleManager.Height);
            logger.LogInformation("Platform: {Platform}", consoleManager.GetPlatformInfo());

            // Initialize UI with startup messages
            leftPane.AddLogEntry("DatabaseGrinder started", LogLevel.Information);
            leftPane.AddLogEntry("Console UI initialized", LogLevel.Information);
            leftPane.AddLogEntry("Database setup completed", LogLevel.Information);
            leftPane.UpdateConnectionStatus("Ready - Starting database writer...", true);

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