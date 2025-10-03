using DatabaseGrinder.Services;
using Microsoft.Extensions.Logging;

namespace DatabaseGrinder.UI;

/// <summary>
/// Manages the left pane display showing database write operations
/// </summary>
public class LeftPane
{
	private readonly ConsoleManager _consoleManager;
	private readonly ILogger<LeftPane> _logger;
	private readonly List<string> _logLines = new();
	private readonly object _lockObject = new object();
	private int _maxLines;

	/// <summary>
	/// Initializes a new instance of LeftPane
	/// </summary>
	/// <param name="consoleManager">Console manager for display operations</param>
	/// <param name="logger">Logger instance</param>
	public LeftPane(ConsoleManager consoleManager, ILogger<LeftPane> logger)
	{
		_consoleManager = consoleManager;
		_logger = logger;
		_consoleManager.SizeChanged += OnSizeChanged;
		UpdateMaxLines();
	}

	/// <summary>
	/// Current connection status for display
	/// </summary>
	private string _connectionStatus = "Initializing...";
	private ConsoleColor _statusColor = ConsoleColor.Yellow;

	/// <summary>
	/// Add a log entry to display
	/// </summary>
	/// <param name="message">Message to display</param>
	/// <param name="level">Log level for color coding</param>
	public void AddLogEntry(string message, LogLevel level = LogLevel.Information)
	{
		lock (_lockObject)
		{
			var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
			var logEntry = $"[{timestamp}] {message}";

			_logLines.Add(logEntry);

			// Keep only the most recent lines that fit on screen
			while (_logLines.Count > _maxLines)
			{
				_logLines.RemoveAt(0);
			}
		}
	}

	/// <summary>
	/// Update the connection status display
	/// </summary>
	/// <param name="status">Status message</param>
	/// <param name="isConnected">Whether the connection is active</param>
	public void UpdateConnectionStatus(string status, bool isConnected)
	{
		lock (_lockObject)
		{
			_connectionStatus = status;
			_statusColor = isConnected ? ConsoleColor.Green : ConsoleColor.Red;
		}
	}

	/// <summary>
	/// Render the left pane content
	/// </summary>
	public void Render()
	{
		lock (_lockObject)
		{
			var paneWidth = _consoleManager.LeftPaneWidth;
			var contentHeight = _consoleManager.ContentHeight;
			var startY = _consoleManager.ContentStartY;

			// Ensure we have valid dimensions
			if (paneWidth <= 0 || contentHeight <= 0)
				return;

			// Clear left pane content area only
			for (int y = startY; y < _consoleManager.Height; y++)
			{
				var clearLine = new string(' ', paneWidth);
				_consoleManager.WriteAt(0, y, clearLine);
			}

			// Draw header with DatabaseGrinder branding
			DrawHeader(0, startY, paneWidth);

			// Draw main title
			var titleY = startY + 2;
			var header = "DATABASE WRITER";
			var headerX = Math.Max(0, (paneWidth - header.Length) / 2);
			if (header.Length <= paneWidth)
			{
				_consoleManager.WriteAt(headerX, titleY, header, ConsoleColor.White, ConsoleColor.Blue);
			}
			else
			{
				// Truncate header if too long
				var truncatedHeader = header.Length > paneWidth - 3 ? header.Substring(0, paneWidth - 3) + "..." : header;
				_consoleManager.WriteAt(0, titleY, truncatedHeader, ConsoleColor.White, ConsoleColor.Blue);
			}

			// Draw separator line using proper line drawing character
			var separatorY = titleY + 1;
			var separator = new string(_consoleManager.HorizontalLineChar, paneWidth);
			_consoleManager.WriteAt(0, separatorY, separator, ConsoleColor.DarkGray);

			// Calculate available space for log entries (accounting for header and footer)
			var headerHeight = 4; // Header (2) + title (1) + separator (1)
			var footerHeight = 4; // Connection status + stats + separator + shortcuts
			var logStartY = separatorY + 1;
			var availableLines = Math.Max(0, contentHeight - headerHeight - footerHeight);
			var linesToShow = Math.Min(_logLines.Count, availableLines);

			// Draw log entries
			for (int i = 0; i < linesToShow; i++)
			{
				var logLineIndex = _logLines.Count - linesToShow + i;
				if (logLineIndex >= 0 && logLineIndex < _logLines.Count)
				{
					var logLine = _logLines[logLineIndex];

					// Ensure text fits within pane boundary
					var displayText = TruncateText(logLine, paneWidth);
					var color = GetLogColor(logLine);
					_consoleManager.WriteAt(0, logStartY + i, displayText, color);
				}
			}

			// Draw footer with status and shortcuts
			DrawFooter(0, paneWidth);
		}
	}

	/// <summary>
	/// Draw the header with DatabaseGrinder branding
	/// </summary>
	private void DrawHeader(int startX, int startY, int width)
	{
		// Line 1: DatabaseGrinder logo
		var logoText = "DatabaseGrinder v1.1.0";
		var logoX = startX + (width - logoText.Length) / 2;
		
		// Draw with blue background for branding
		for (int i = 0; i < logoText.Length && logoX + i < startX + width; i++)
		{
			var ch = logoText[i];
			var fg = ConsoleColor.White;
			var bg = ConsoleColor.DarkBlue;
			_consoleManager.WriteAt(logoX + i, startY, ch.ToString(), fg, bg);
		}

		// Line 2: Description
		var description = "Database Replication Monitor";
		var descX = startX + (width - description.Length) / 2;
		if (descX >= 0 && description.Length <= width)
		{
			_consoleManager.WriteAt(descX, startY + 1, description, ConsoleColor.Cyan, ConsoleColor.Black);
		}
	}

	/// <summary>
	/// Draw the footer with connection status and controls
	/// </summary>
	private void DrawFooter(int startX, int width)
	{
		var footerStartY = _consoleManager.Height - 4;

		// Draw separator line above footer
		var separator = new string(_consoleManager.HorizontalLineChar, width);
		_consoleManager.WriteAt(startX, footerStartY, separator, ConsoleColor.DarkGray);

		// Line 1: Connection status
		var statusText = TruncateText($"Status: {_connectionStatus}", width);
		_consoleManager.WriteAt(startX, footerStartY + 1, statusText, _statusColor);

		// Line 2: Log entries count
		var totalEntries = $"Log Entries: {_logLines.Count}";
		var entriesText = TruncateText(totalEntries, width);
		_consoleManager.WriteAt(startX, footerStartY + 2, entriesText, ConsoleColor.Cyan);

		// Line 3: Quick shortcuts (compact version)
		var shortcuts = "ESC=exit Ctrl+Q=cleanup";
		if (shortcuts.Length <= width)
		{
			var shortcutX = startX + (width - shortcuts.Length) / 2;
			_consoleManager.WriteAt(shortcutX, footerStartY + 3, shortcuts, ConsoleColor.DarkGray);
		}
	}

	/// <summary>
	/// Truncate text to fit within the specified width, adding ellipsis if needed
	/// </summary>
	/// <param name="text">Text to truncate</param>
	/// <param name="maxWidth">Maximum width allowed</param>
	/// <returns>Truncated text</returns>
	private string TruncateText(string text, int maxWidth)
	{
		if (string.IsNullOrEmpty(text) || maxWidth <= 0)
			return string.Empty;

		if (text.Length <= maxWidth)
			return text;

		// Leave room for ellipsis
		if (maxWidth <= 3)
			return text.Substring(0, maxWidth);

		return text.Substring(0, maxWidth - 3) + "...";
	}

	private void OnSizeChanged(int newWidth, int newHeight)
	{
		UpdateMaxLines();
	}

	private void UpdateMaxLines()
	{
		// Calculate maximum lines based on content height
		var displayLines = Math.Max(1, _consoleManager.ContentHeight - 8); // Account for header and footer
		_maxLines = Math.Max(displayLines * 5, 50); // Keep 5x display capacity or minimum 50 lines
	}

	private ConsoleColor GetLogColor(string logLine)
	{
		if (logLine.Contains("ERROR") || logLine.Contains("Failed") || logLine.Contains("FATAL"))
			return ConsoleColor.Red;
		if (logLine.Contains("WARNING") || logLine.Contains("Warn"))
			return ConsoleColor.Yellow;
		if (logLine.Contains("SUCCESS") || logLine.Contains("Inserted") || logLine.Contains("completed successfully"))
			return ConsoleColor.Green;
		if (logLine.Contains("DEBUG") || logLine.Contains("performance"))
			return ConsoleColor.DarkCyan;

		return ConsoleColor.Gray;
	}
}