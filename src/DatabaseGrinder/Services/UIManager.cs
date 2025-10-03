using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using DatabaseGrinder.Configuration;
using DatabaseGrinder.Services;
using DatabaseGrinder.UI;

namespace DatabaseGrinder.Services;

/// <summary>
/// Background service that handles UI refresh and user input
/// </summary>
public class UIManager : BackgroundService
{
    private readonly ILogger<UIManager> _logger;
    private readonly DatabaseGrinderSettings _settings;
    private readonly ConsoleManager _consoleManager;
    private readonly LeftPane _leftPane;
    private readonly RightPane _rightPane;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _hostLifetime;

    public UIManager(
        ILogger<UIManager> logger,
        IOptions<DatabaseGrinderSettings> settings,
        ConsoleManager consoleManager,
        LeftPane leftPane,
        RightPane rightPane,
        IServiceProvider serviceProvider,
        IHostApplicationLifetime hostLifetime)
    {
        _logger = logger;
        _settings = settings.Value;
        _consoleManager = consoleManager;
        _leftPane = leftPane;
        _rightPane = rightPane;
        _serviceProvider = serviceProvider;
        _hostLifetime = hostLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("UI Manager started - refreshing every {IntervalMs}ms", _settings.Settings.UIRefreshIntervalMs);
        
        var refreshInterval = TimeSpan.FromMilliseconds(_settings.Settings.UIRefreshIntervalMs);
        
        // Initial render
        RenderUI();
        
        // Start UI refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check for console size changes
                _consoleManager.CheckForSizeChange();
                
                // Update left pane with writer status (we'll get this from a shared service later)
                _leftPane.UpdateConnectionStatus("Database Writer Active", true);
                
                // Render the UI
                RenderUI();
                
                // Check for keyboard input (non-blocking)
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        _logger.LogInformation("User requested application shutdown");
                        _leftPane.AddLogEntry("Shutdown requested by user", LogLevel.Warning);
                        
                        // Trigger application shutdown
                        _hostLifetime.StopApplication();
                        break;
                    }
                    else if (key.Key == ConsoleKey.F5)
                    {
                        _leftPane.AddLogEntry("Manual refresh triggered", LogLevel.Information);
                    }
                }
                
                await Task.Delay(refreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in UI refresh loop");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        
        _logger.LogInformation("UI Manager stopped");
    }

    private void RenderUI()
    {
        try
        {
            _consoleManager.Clear();
            _consoleManager.DrawVerticalSeparator();
            _leftPane.Render();
            _rightPane.Render();
            
            // Show controls at the bottom
            var controlsText = "Press 'q' to quit | F5 to refresh";
            var y = _consoleManager.Height - 1;
            var x = (_consoleManager.Width - controlsText.Length) / 2;
            _consoleManager.WriteAt(x, y, controlsText, ConsoleColor.DarkGray);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render UI");
        }
    }
}