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
            _connectionStatus = $"Status: {status}";
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
            var paneHeight = _consoleManager.Height;

            // Clear left pane
            for (int y = 0; y < paneHeight; y++)
            {
                var clearLine = new string(' ', paneWidth);
                _consoleManager.WriteAt(0, y, clearLine);
            }

            // Draw header
            var header = "DATABASE WRITER";
            var headerX = (paneWidth - header.Length) / 2;
            _consoleManager.WriteAt(headerX, 0, header, ConsoleColor.White, ConsoleColor.Blue);

            // Draw separator line
            var separator = new string('─', paneWidth);
            _consoleManager.WriteAt(0, 1, separator, ConsoleColor.DarkGray);

            // Draw log entries (starting from line 2)
            var startY = 2;
            var availableLines = paneHeight - startY;
            var linesToShow = Math.Min(_logLines.Count, availableLines);

            for (int i = 0; i < linesToShow; i++)
            {
                var logLineIndex = _logLines.Count - linesToShow + i;
                if (logLineIndex >= 0 && logLineIndex < _logLines.Count)
                {
                    var logLine = _logLines[logLineIndex];
                    
                    // Truncate if too long
                    if (logLine.Length > paneWidth)
                        logLine = logLine.Substring(0, paneWidth - 3) + "...";

                    var color = GetLogColor(logLine);
                    _consoleManager.WriteAt(0, startY + i, logLine, color);
                }
            }

            // Draw statistics at the bottom if there's space
            if (paneHeight > 5)
            {
                var statsY = paneHeight - 3;
                var statsLine = new string('─', paneWidth);
                _consoleManager.WriteAt(0, statsY, statsLine, ConsoleColor.DarkGray);

                // Connection status
                if (_connectionStatus.Length > paneWidth)
                    _connectionStatus = _connectionStatus.Substring(0, paneWidth - 3) + "...";
                _consoleManager.WriteAt(0, statsY + 1, _connectionStatus, _statusColor);

                // Total entries
                var totalEntries = $"Log Entries: {_logLines.Count}";
                _consoleManager.WriteAt(0, statsY + 2, totalEntries, ConsoleColor.Cyan);
            }
        }
    }

    private void OnSizeChanged(int newWidth, int newHeight)
    {
        UpdateMaxLines();
    }

    private void UpdateMaxLines()
    {
        // Calculate maximum lines (height - header - separator - stats)
        _maxLines = Math.Max(10, _consoleManager.Height * 10); // Keep more lines in buffer for scrolling
    }

    private ConsoleColor GetLogColor(string logLine)
    {
        if (logLine.Contains("ERROR") || logLine.Contains("Failed"))
            return ConsoleColor.Red;
        if (logLine.Contains("WARNING") || logLine.Contains("Warn"))
            return ConsoleColor.Yellow;
        if (logLine.Contains("SUCCESS") || logLine.Contains("Inserted"))
            return ConsoleColor.Green;
        
        return ConsoleColor.Gray;
    }
}