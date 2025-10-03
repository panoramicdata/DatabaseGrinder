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
    private int _renderCount = 0;
    private DateTime _lastPerformanceLog = DateTime.Now;

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
        _logger.LogInformation("UI Manager started - refreshing every {IntervalMs}ms with differential rendering", 
            _settings.Settings.UIRefreshIntervalMs);
        
        var refreshInterval = TimeSpan.FromMilliseconds(_settings.Settings.UIRefreshIntervalMs);
        
        // Initial render
        RenderUI();
        
        // Start UI refresh loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var renderStart = DateTime.Now;
                
                // Check for console size changes
                var sizeChanged = _consoleManager.CheckForSizeChange();
                if (sizeChanged)
                {
                    _logger.LogInformation("Console size changed to {Width}x{Height}", 
                        _consoleManager.Width, _consoleManager.Height);
                    _consoleManager.ForceFullRedraw();
                }
                
                // Update left pane with writer status
                _leftPane.UpdateConnectionStatus("Database Writer Active", true);
                
                // Render the UI using differential updates
                RenderUI();
                
                var renderTime = DateTime.Now - renderStart;
                _renderCount++;
                
                // Log performance stats every 10 seconds
                if (DateTime.Now - _lastPerformanceLog > TimeSpan.FromSeconds(10))
                {
                    var perfStats = _consoleManager.GetPerformanceStats();
                    _leftPane.AddLogEntry($"Render performance: {perfStats}", LogLevel.Debug);
                    _lastPerformanceLog = DateTime.Now;
                }
                
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
                        _consoleManager.ForceFullRedraw();
                    }
                    else if (key.Key == ConsoleKey.F12)
                    {
                        // Debug: Force full redraw and show performance stats
                        _consoleManager.ForceFullRedraw();
                        var stats = _consoleManager.GetPerformanceStats();
                        _leftPane.AddLogEntry($"Full redraw - {stats}", LogLevel.Information);
                    }
                    else if (key.Key == ConsoleKey.F1)
                    {
                        // Show help
                        _leftPane.AddLogEntry("Keys: q=quit, F5=refresh, F12=stats, F1=help", LogLevel.Information);
                    }
                }
                
                // Calculate remaining sleep time
                var elapsed = DateTime.Now - renderStart;
                var remainingTime = refreshInterval - elapsed;
                
                if (remainingTime > TimeSpan.Zero)
                {
                    await Task.Delay(remainingTime, stoppingToken);
                }
                else if (elapsed.TotalMilliseconds > refreshInterval.TotalMilliseconds * 1.5)
                {
                    // Log if rendering is taking too long
                    _logger.LogWarning("Render took {ElapsedMs}ms, target is {TargetMs}ms", 
                        elapsed.TotalMilliseconds, refreshInterval.TotalMilliseconds);
                }
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
        
        _logger.LogInformation("UI Manager stopped after {RenderCount} renders", _renderCount);
    }

    private void RenderUI()
    {
        try
        {
            // Clear the buffer
            _consoleManager.Clear();
            
            // Draw vertical separator
            _consoleManager.DrawVerticalSeparator();
            
            // Render left pane to buffer
            _leftPane.Render();
            
            // Render right pane to buffer
            _rightPane.Render();
            
            // Show controls at the bottom
            var controlsText = "q=quit | F5=refresh | F12=perf | F1=help";
            var y = _consoleManager.Height - 1;
            var x = (_consoleManager.Width - controlsText.Length) / 2;
            if (x >= 0) // Only show if there's enough space
            {
                _consoleManager.WriteAt(x, y, controlsText, ConsoleColor.DarkGray);
            }
            
            // Apply all changes to the actual console (differential update)
            _consoleManager.Render();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render UI");
        }
    }
}