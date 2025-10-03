using System.Runtime.InteropServices;

namespace DatabaseGrinder.Services;

/// <summary>
/// Manages console display, sizing, and cross-platform compatibility
/// </summary>
public class ConsoleManager
{
    private readonly object _lockObject = new object();
    private int _currentWidth;
    private int _currentHeight;
    private bool _isInitialized;

    /// <summary>
    /// Minimum supported console width
    /// </summary>
    public int MinWidth { get; }

    /// <summary>
    /// Minimum supported console height
    /// </summary>
    public int MinHeight { get; }

    /// <summary>
    /// Current console width
    /// </summary>
    public int Width => _currentWidth;

    /// <summary>
    /// Current console height
    /// </summary>
    public int Height => _currentHeight;

    /// <summary>
    /// Gets the width of the left pane (half of console minus separator)
    /// </summary>
    public int LeftPaneWidth => (_currentWidth / 2) - 1;

    /// <summary>
    /// Gets the width of the right pane (half of console minus separator)
    /// </summary>
    public int RightPaneWidth => (_currentWidth / 2) - 1;

    /// <summary>
    /// Event raised when console size changes
    /// </summary>
    public event Action<int, int>? SizeChanged;

    /// <summary>
    /// Initializes a new instance of ConsoleManager
    /// </summary>
    /// <param name="minWidth">Minimum console width to support</param>
    /// <param name="minHeight">Minimum console height to support</param>
    public ConsoleManager(int minWidth = 20, int minHeight = 20)
    {
        MinWidth = minWidth;
        MinHeight = minHeight;
    }

    /// <summary>
    /// Initialize the console and start monitoring for size changes
    /// </summary>
    public void Initialize()
    {
        lock (_lockObject)
        {
            if (_isInitialized)
                return;

            // Set up console
            Console.Clear();
            Console.CursorVisible = false;
            
            // Get initial size
            UpdateSize();

            // Validate minimum size
            if (_currentWidth < MinWidth || _currentHeight < MinHeight)
            {
                throw new InvalidOperationException(
                    $"Console too small. Minimum size: {MinWidth}x{MinHeight}, Current: {_currentWidth}x{_currentHeight}");
            }

            _isInitialized = true;

            // Start monitoring for size changes (in a real implementation, this would use a background thread)
            // For now, we'll check size before each refresh
        }
    }

    /// <summary>
    /// Check for console size changes and update if needed
    /// </summary>
    public bool CheckForSizeChange()
    {
        lock (_lockObject)
        {
            var newWidth = Console.WindowWidth;
            var newHeight = Console.WindowHeight;

            if (newWidth != _currentWidth || newHeight != _currentHeight)
            {
                var oldWidth = _currentWidth;
                var oldHeight = _currentHeight;

                _currentWidth = newWidth;
                _currentHeight = newHeight;

                // Validate minimum size
                if (_currentWidth < MinWidth || _currentHeight < MinHeight)
                {
                    throw new InvalidOperationException(
                        $"Console too small. Minimum size: {MinWidth}x{MinHeight}, Current: {_currentWidth}x{_currentHeight}");
                }

                SizeChanged?.Invoke(_currentWidth, _currentHeight);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clear the console and reset cursor position
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            Console.Clear();
        }
    }

    /// <summary>
    /// Write text at a specific position with optional color
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="text">Text to write</param>
    /// <param name="foreground">Foreground color (optional)</param>
    /// <param name="background">Background color (optional)</param>
    public void WriteAt(int x, int y, string text, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        lock (_lockObject)
        {
            if (x >= 0 && y >= 0 && x < _currentWidth && y < _currentHeight)
            {
                var originalForeground = Console.ForegroundColor;
                var originalBackground = Console.BackgroundColor;

                try
                {
                    Console.SetCursorPosition(x, y);
                    
                    if (foreground.HasValue)
                        Console.ForegroundColor = foreground.Value;
                    if (background.HasValue)
                        Console.BackgroundColor = background.Value;

                    // Truncate text if it would exceed console width
                    var maxLength = _currentWidth - x;
                    if (text.Length > maxLength)
                        text = text.Substring(0, maxLength);

                    Console.Write(text);
                }
                finally
                {
                    Console.ForegroundColor = originalForeground;
                    Console.BackgroundColor = originalBackground;
                }
            }
        }
    }

    /// <summary>
    /// Draw a vertical separator between left and right panes
    /// </summary>
    public void DrawVerticalSeparator()
    {
        lock (_lockObject)
        {
            var separatorX = _currentWidth / 2;
            for (int y = 0; y < _currentHeight; y++)
            {
                WriteAt(separatorX, y, "│", ConsoleColor.DarkGray);
            }
        }
    }

    /// <summary>
    /// Get platform-specific information for debugging
    /// </summary>
    /// <returns>Platform information string</returns>
    public string GetPlatformInfo()
    {
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.OSArchitecture;
        return $"{os} ({arch})";
    }

    private void UpdateSize()
    {
        _currentWidth = Console.WindowWidth;
        _currentHeight = Console.WindowHeight;
    }
}