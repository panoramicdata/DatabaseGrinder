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
	private readonly List<string> _logLines = [];
	private readonly Lock _lockObject = new();
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
			var startY = _consoleManager.ContentStartY + 1; // +1 to skip separator line

			// Ensure we have valid dimensions
			if (paneWidth <= 0 || contentHeight <= 0)
				return;

			// Clear left pane content area only (content area between global header and footer)
			for (int y = startY; y < _consoleManager.FooterStartY; y++)
			{
				var clearLine = new string(' ', paneWidth);
				_consoleManager.WriteAt(0, y, clearLine);
			}

			// Draw main title
			var header = "DATABASE WRITER";
			var headerX = Math.Max(0, (paneWidth - header.Length) / 2);
			if (header.Length <= paneWidth)
			{
				_consoleManager.WriteAt(headerX, startY, header, ConsoleColor.White, ConsoleColor.Blue);
			}
			else
			{
				// Truncate header if too long
				var truncatedHeader = header.Length > paneWidth - 3 ? header[..(paneWidth - 3)] + "..." : header;
				_consoleManager.WriteAt(0, startY, truncatedHeader, ConsoleColor.White, ConsoleColor.Blue);
			}

			// Draw separator line using proper line drawing character with T-piece
			var separatorY = startY + 1;
			var separator = new string(_consoleManager.HorizontalLineChar, paneWidth);
			_consoleManager.WriteAt(0, separatorY, separator, ConsoleColor.DarkGray);
			// Add T-piece where this horizontal line meets the vertical separator
			_consoleManager.WriteCharAt(paneWidth, separatorY, _consoleManager.GetTeeLeftChar(), ConsoleColor.DarkGray);

			// Calculate available space for log entries (accounting for title and local separator)
			var logStartY = separatorY + 1;
			var statusAreaHeight = 3; // Reserve space for connection status and log count
			var availableHeight = _consoleManager.FooterStartY - logStartY - statusAreaHeight;
			var availableLines = Math.Max(0, availableHeight);
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

			// Draw status area at the bottom of the content area (above global footer)
			var statusStartY = _consoleManager.FooterStartY - statusAreaHeight;

			// Status separator line with T-piece
			var statusSeparator = new string(_consoleManager.HorizontalLineChar, paneWidth);
			_consoleManager.WriteAt(0, statusStartY, statusSeparator, ConsoleColor.DarkGray);
			// Add T-piece where this horizontal line meets the vertical separator
			_consoleManager.WriteCharAt(paneWidth, statusStartY, _consoleManager.GetTeeLeftChar(), ConsoleColor.DarkGray);

			// Connection status
			var statusText = TruncateText(_connectionStatus, paneWidth);
			_consoleManager.WriteAt(0, statusStartY + 1, statusText, _statusColor);

			// Total entries
			var totalEntries = $"Log Entries: {_logLines.Count}";
			var entriesText = TruncateText(totalEntries, paneWidth);
			_consoleManager.WriteAt(0, statusStartY + 2, entriesText, ConsoleColor.Cyan);
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
			return text[..maxWidth];

		return text[..(maxWidth - 3)] + "...";
	}

	private void OnSizeChanged(int newWidth, int newHeight)
	{
		UpdateMaxLines();
	}

	private void UpdateMaxLines()
	{
		// Calculate maximum lines based on content height
		var displayLines = Math.Max(1, _consoleManager.ContentHeight - 6); // Account for title, separators, and status
		_maxLines = Math.Max(displayLines * 5, 50); // Keep 5x display capacity or minimum 50 lines
	}

	private static ConsoleColor GetLogColor(string logLine)
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