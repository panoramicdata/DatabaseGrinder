using DatabaseGrinder.Configuration;
using DatabaseGrinder.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

		try
		{
			// Initial render
			RenderUI();

			// Check for window too small on startup
			if (_consoleManager.Width < _consoleManager.MinWidth || _consoleManager.Height < _consoleManager.MinHeight)
			{
				await HandleWindowTooSmallAsync(stoppingToken);
				if (stoppingToken.IsCancellationRequested) return;
			}

			// Add startup messages only after console is properly sized
			_leftPane.AddLogEntry($"UI refresh rate: {_settings.Settings.UIRefreshIntervalMs}ms ({1000.0 / _settings.Settings.UIRefreshIntervalMs:F1} FPS)", LogLevel.Information);
			_leftPane.AddLogEntry($"Terminal capabilities: {_consoleManager.GetPlatformInfo()}", LogLevel.Information);

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

						// If window became too small, handle it
						if (_consoleManager.Width < _consoleManager.MinWidth || _consoleManager.Height < _consoleManager.MinHeight)
						{
							await HandleWindowTooSmallAsync(stoppingToken);
							if (stoppingToken.IsCancellationRequested) return;
							continue;
						}

						_leftPane.AddLogEntry($"Console resized to {_consoleManager.Width}x{_consoleManager.Height}", LogLevel.Information);
						_consoleManager.ForceFullRedraw();
					}

					// Update left pane with writer status
					_leftPane.UpdateConnectionStatus("Database Writer Active", true);

					// Render the UI using differential updates
					RenderUI();

					var renderTime = DateTime.Now - renderStart;
					_renderCount++;

					// Log performance stats every 30 seconds for faster refresh rates
					if (DateTime.Now - _lastPerformanceLog > TimeSpan.FromSeconds(30))
					{
						var perfStats = _consoleManager.GetPerformanceStats();
						var avgRenderTime = renderTime.TotalMilliseconds;
						_leftPane.AddLogEntry($"UI performance: {perfStats}, avg render: {avgRenderTime:F1}ms", LogLevel.Debug);
						_lastPerformanceLog = DateTime.Now;
					}

					// Check for keyboard input (non-blocking)
					if (Console.KeyAvailable)
					{
						var key = Console.ReadKey(true);
						HandleKeyPress(key);
					}

					// Calculate remaining sleep time
					var elapsed = DateTime.Now - renderStart;
					var remainingTime = refreshInterval - elapsed;

					if (remainingTime > TimeSpan.Zero)
					{
						try
						{
							await Task.Delay(remainingTime, stoppingToken);
						}
						catch (OperationCanceledException)
						{
							// Expected when stopping - break out of the loop
							break;
						}
					}
					else if (elapsed.TotalMilliseconds > refreshInterval.TotalMilliseconds * 1.5)
					{
						// Log if rendering is taking too long (only occasionally to avoid spam)
						if (_renderCount % 100 == 0)
						{
							_logger.LogWarning("Render took {ElapsedMs}ms, target is {TargetMs}ms",
								elapsed.TotalMilliseconds, refreshInterval.TotalMilliseconds);
							_leftPane.AddLogEntry($"Slow render: {elapsed.TotalMilliseconds:F1}ms (target: {refreshInterval.TotalMilliseconds}ms)", LogLevel.Warning);
						}
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
					_leftPane.AddLogEntry($"UI error: {ex.Message}", LogLevel.Error);

					try
					{
						await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
					}
					catch (OperationCanceledException)
					{
						break;
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected during shutdown
			_logger.LogInformation("UI Manager cancellation requested");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error in UI Manager");
			_leftPane.AddLogEntry($"FATAL: UI Manager error - {ex.Message}", LogLevel.Error);
		}
		finally
		{
			_logger.LogInformation("UI Manager stopped gracefully after {RenderCount} renders", _renderCount);
			_leftPane.AddLogEntry($"UI Manager stopped after {_renderCount} renders", LogLevel.Information);
		}
	}

	/// <summary>
	/// Handle window too small scenario - wait for user to resize
	/// </summary>
	private async Task HandleWindowTooSmallAsync(CancellationToken stoppingToken)
	{
		_logger.LogWarning("Console window too small: {Width}x{Height}, minimum: {MinWidth}x{MinHeight}",
			_consoleManager.Width, _consoleManager.Height, _consoleManager.MinWidth, _consoleManager.MinHeight);

		while (!stoppingToken.IsCancellationRequested)
		{
			// Show the "window too small" message
			_consoleManager.Render();

			// Check for keyboard input (allow exit even when window is too small)
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);
				if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					_hostLifetime.StopApplication();
					return;
				}
			}

			// Wait a bit before checking size again
			await Task.Delay(500, stoppingToken);

			// Check if window has been resized to acceptable size
			_consoleManager.CheckForSizeChange();
			if (_consoleManager.Width >= _consoleManager.MinWidth && _consoleManager.Height >= _consoleManager.MinHeight)
			{
				_logger.LogInformation("Console window resized to acceptable size: {Width}x{Height}",
					_consoleManager.Width, _consoleManager.Height);
				break;
			}
		}
	}

	private void HandleKeyPress(ConsoleKeyInfo key)
	{
		switch (key.KeyChar)
		{
			case 'q':
			case 'Q':
				// Check if Ctrl is pressed for cleanup mode
				if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
				{
					// Ctrl+Q - Database cleanup and quit
					_logger.LogWarning("User pressed Ctrl+Q - initiating database cleanup");
					_leftPane.AddLogEntry("Ctrl+Q - Initiating database cleanup mode...");

					// Stop the application and trigger cleanup
					_ = Task.Run(async () =>
					{
						await Task.Delay(500); // Give UI time to display message
						await Program.HandleCleanupAndQuitAsync();
					});
				}
				else
				{
					// Regular q - Normal quit
					_logger.LogInformation("User requested application shutdown");
					_leftPane.AddLogEntry("Shutdown requested by user (q)");
					_hostLifetime.StopApplication();
				}

				break;

			case 'r':
			case 'R':
				_leftPane.AddLogEntry("Manual refresh triggered (r)");
				_consoleManager.ForceFullRedraw();
				break;

			case 'h':
			case 'H':
				// Show help in log
				_leftPane.AddLogEntry("Keys: q=quit, Ctrl+Q=cleanup&quit, r=refresh, h=help, ESC=exit");
				break;

			default:
				switch (key.Key)
				{
					case ConsoleKey.F5:
						_leftPane.AddLogEntry("F5 - Manual refresh triggered");
						_consoleManager.ForceFullRedraw();
						break;

					case ConsoleKey.F12:
						// Debug: Force full redraw and show performance stats
						_consoleManager.ForceFullRedraw();
						var stats = _consoleManager.GetPerformanceStats();
						_leftPane.AddLogEntry($"F12 - Performance: {stats}");
						_leftPane.AddLogEntry($"Layout: {_consoleManager.GetLayoutInfo()}");
						break;

					case ConsoleKey.F1:
						// Show help
						_leftPane.AddLogEntry("=== HELP ===");
						_leftPane.AddLogEntry("q/ESC=quit | Ctrl+Q=cleanup&quit | r/F5=refresh | F12=stats | h/F1=help");
						break;

					case ConsoleKey.Escape:
						_leftPane.AddLogEntry("ESC - Shutdown requested");
						_hostLifetime.StopApplication();
						break;
				}
				break;
		}
	}

	private void RenderUI()
	{
		try
		{
			// Clear the buffer
			_consoleManager.Clear();

			// Draw vertical separator (ConsoleManager will handle branding area automatically)
			_consoleManager.DrawVerticalSeparator();

			// Render left pane to buffer
			_leftPane.Render();

			// Render right pane to buffer
			_rightPane.Render();

			// Apply all changes to the actual console (differential update)
			// This will also render the branding area
			_consoleManager.Render();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to render UI");
		}
	}
}