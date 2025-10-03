using DatabaseGrinder.Configuration;
using DatabaseGrinder.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DatabaseGrinder.Services;

/// <summary>
/// Background service that handles UI refresh and user input
/// </summary>
public class UIManager(
	ILogger<UIManager> logger,
	IOptions<DatabaseGrinderSettings> settings,
	ConsoleManager consoleManager,
	LeftPane leftPane,
	RightPane rightPane,
	IServiceProvider serviceProvider,
	IHostApplicationLifetime hostLifetime) : BackgroundService
{
	private readonly DatabaseGrinderSettings _settings = settings.Value;
	private int _renderCount = 0;
	private DateTime _lastPerformanceLog = DateTime.Now;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("UI Manager started - refreshing every {IntervalMs}ms with differential rendering",
			_settings.Settings.UIRefreshIntervalMs);

		var refreshInterval = TimeSpan.FromMilliseconds(_settings.Settings.UIRefreshIntervalMs);

		try
		{
			// Initial render
			RenderUI();

			// Check for window too small on startup
			if (consoleManager.Width < consoleManager.MinWidth || consoleManager.Height < consoleManager.MinHeight)
			{
				await HandleWindowTooSmallAsync(stoppingToken);
				if (stoppingToken.IsCancellationRequested) return;
			}

			// Add startup messages only after console is properly sized
			leftPane.AddLogEntry($"UI refresh rate: {_settings.Settings.UIRefreshIntervalMs}ms ({1000.0 / _settings.Settings.UIRefreshIntervalMs:F1} FPS)", LogLevel.Information);
			leftPane.AddLogEntry($"Terminal capabilities: {consoleManager.GetPlatformInfo()}", LogLevel.Information);

			// Start UI refresh loop
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					var renderStart = DateTime.Now;

					// Check for console size changes
					var sizeChanged = consoleManager.CheckForSizeChange();
					if (sizeChanged)
					{
						logger.LogInformation("Console size changed to {Width}x{Height}",
							consoleManager.Width, consoleManager.Height);

						// If window became too small, handle it
						if (consoleManager.Width < consoleManager.MinWidth || consoleManager.Height < consoleManager.MinHeight)
						{
							await HandleWindowTooSmallAsync(stoppingToken);
							if (stoppingToken.IsCancellationRequested) return;
							continue;
						}

						leftPane.AddLogEntry($"Console resized to {consoleManager.Width}x{consoleManager.Height}", LogLevel.Information);
						consoleManager.ForceFullRedraw();
					}

					// Update left pane with writer status
					leftPane.UpdateConnectionStatus("Database Writer Active", true);

					// Render the UI using differential updates
					RenderUI();

					var renderTime = DateTime.Now - renderStart;
					_renderCount++;

					// Log performance stats every 30 seconds for faster refresh rates
					if (DateTime.Now - _lastPerformanceLog > TimeSpan.FromSeconds(30))
					{
						var perfStats = consoleManager.GetPerformanceStats();
						var avgRenderTime = renderTime.TotalMilliseconds;
						leftPane.AddLogEntry($"UI performance: {perfStats}, avg render: {avgRenderTime:F1}ms", LogLevel.Debug);
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
							logger.LogWarning("Render took {ElapsedMs}ms, target is {TargetMs}ms",
								elapsed.TotalMilliseconds, refreshInterval.TotalMilliseconds);
							leftPane.AddLogEntry($"Slow render: {elapsed.TotalMilliseconds:F1}ms (target: {refreshInterval.TotalMilliseconds}ms)", LogLevel.Warning);
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
					logger.LogError(ex, "Error in UI refresh loop");
					leftPane.AddLogEntry($"UI error: {ex.Message}", LogLevel.Error);

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
			logger.LogInformation("UI Manager cancellation requested");
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Unexpected error in UI Manager");
			leftPane.AddLogEntry($"FATAL: UI Manager error - {ex.Message}", LogLevel.Error);
		}
		finally
		{
			logger.LogInformation("UI Manager stopped gracefully after {RenderCount} renders", _renderCount);
			leftPane.AddLogEntry($"UI Manager stopped after {_renderCount} renders", LogLevel.Information);
		}
	}

	/// <summary>
	/// Handle window too small scenario - wait for user to resize
	/// </summary>
	private async Task HandleWindowTooSmallAsync(CancellationToken stoppingToken)
	{
		logger.LogWarning("Console window too small: {Width}x{Height}, minimum: {MinWidth}x{MinHeight}",
			consoleManager.Width, consoleManager.Height, consoleManager.MinWidth, consoleManager.MinHeight);

		while (!stoppingToken.IsCancellationRequested)
		{
			// Show the "window too small" message
			consoleManager.Render();

			// Check for keyboard input (allow exit even when window is too small)
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true);

				// Handle exit keys
				if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
				{
					if (key.KeyChar == 'Q' && (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
					{
						// Ctrl+Q - cleanup and quit
						logger.LogWarning("User pressed Ctrl+Q during window too small - initiating cleanup");
						_ = Task.Run(async () =>
						{
							await Task.Delay(500);
							await Program.HandleCleanupAndQuitAsync();
						}, stoppingToken);
						return;
					}
					else
					{
						// Regular quit
						hostLifetime.StopApplication();
						return;
					}
				}
			}

			// Wait a bit before checking size again
			await Task.Delay(500, stoppingToken);

			// Check if window has been resized to acceptable size
			consoleManager.CheckForSizeChange();
			if (consoleManager.Width >= consoleManager.MinWidth && consoleManager.Height >= consoleManager.MinHeight)
			{
				logger.LogInformation("Console window resized to acceptable size: {Width}x{Height}",
					consoleManager.Width, consoleManager.Height);
				break;
			}
		}
	}

	private void HandleKeyPress(ConsoleKeyInfo key)
	{
		// Add debug logging for key presses
		logger.LogDebug("Key pressed: '{KeyChar}' (0x{KeyCode:X2}), Key: {Key}, Modifiers: {Modifiers}", 
			key.KeyChar, (int)key.KeyChar, key.Key, key.Modifiers);

		switch (key.KeyChar)
		{
			case 'q':
			case 'Q':
				// Check if Ctrl is pressed for cleanup mode
				if ((key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
				{
					// Ctrl+Q - Database cleanup and quit
					logger.LogWarning("User pressed Ctrl+Q - initiating database cleanup");
					leftPane.AddLogEntry("Ctrl+Q detected - Shutting down and cleaning up database...", LogLevel.Warning);
					leftPane.AddLogEntry("This will DELETE the database and read-only user!", LogLevel.Error);

					// Stop the application and trigger cleanup
					_ = Task.Run(async () =>
					{
						await Task.Delay(1000); // Give UI time to display message
						try
						{
							await Program.HandleCleanupAndQuitAsync();
						}
						catch (Exception ex)
						{
							logger.LogError(ex, "Error during cleanup");
							Environment.Exit(1);
						}
					});
				}
				else
				{
					// Regular q - Normal quit
					logger.LogInformation("User requested application shutdown");
					leftPane.AddLogEntry("Shutdown requested by user (q)", LogLevel.Warning);
					hostLifetime.StopApplication();
				}

				break;

			case 'r':
			case 'R':
				leftPane.AddLogEntry("Manual refresh triggered (r)", LogLevel.Information);
				consoleManager.ForceFullRedraw();
				break;

			// Add explicit handling for Ctrl+Q when it comes as a control character
			case '\u0011': // Ctrl+Q (ASCII 17)
				logger.LogWarning("Ctrl+Q detected as control character - initiating database cleanup");
				leftPane.AddLogEntry("Ctrl+Q (control char) - Shutting down and cleaning up database...", LogLevel.Warning);
				leftPane.AddLogEntry("This will DELETE the database and read-only user!", LogLevel.Error);

				// Stop the application and trigger cleanup
				_ = Task.Run(async () =>
				{
					await Task.Delay(1000); // Give UI time to display message
					try
					{
						await Program.HandleCleanupAndQuitAsync();
					}
					catch (Exception ex)
					{
						logger.LogError(ex, "Error during cleanup");
						Environment.Exit(1);
					}
				});
				break;

			default:
				switch (key.Key)
				{
					case ConsoleKey.Escape:
						leftPane.AddLogEntry("ESC - Shutdown requested", LogLevel.Warning);
						hostLifetime.StopApplication();
						break;

					// Handle Ctrl+Q through ConsoleKey detection as backup
					case ConsoleKey.Q when (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control:
						logger.LogWarning("Ctrl+Q detected via ConsoleKey - initiating database cleanup");
						leftPane.AddLogEntry("Ctrl+Q (via key) - Shutting down and cleaning up database...", LogLevel.Warning);
						leftPane.AddLogEntry("This will DELETE the database and read-only user!", LogLevel.Error);

						// Stop the application and trigger cleanup
						_ = Task.Run(async () =>
						{
							await Task.Delay(1000); // Give UI time to display message
							try
							{
								await Program.HandleCleanupAndQuitAsync();
							}
							catch (Exception ex)
							{
								logger.LogError(ex, "Error during cleanup");
								Environment.Exit(1);
							}
						});
						break;

					default:
						// Log unhandled keys for debugging
						if (key.KeyChar != '\0')
						{
							leftPane.AddLogEntry($"Unhandled key: '{key.KeyChar}' ({key.Key}) Modifiers: {key.Modifiers}", LogLevel.Debug);
						}
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
			consoleManager.Clear();

			// Draw vertical separator (ConsoleManager will handle branding area automatically)
			consoleManager.DrawVerticalSeparator();

			// Render left pane to buffer
			leftPane.Render();

			// Render right pane to buffer
			rightPane.Render();

			// Apply all changes to the actual console (differential update)
			// This will also render the branding area
			consoleManager.Render();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to render UI");
		}
	}
}