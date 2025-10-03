using System.Runtime.InteropServices;
using System.Text;

namespace DatabaseGrinder.Services;

/// <summary>
/// Represents a single character cell in the console buffer
/// </summary>
public struct ConsoleCell
{
    public char Character { get; set; }
    public ConsoleColor Foreground { get; set; }
    public ConsoleColor Background { get; set; }

    public ConsoleCell(char character, ConsoleColor foreground = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black)
    {
        Character = character;
        Foreground = foreground;
        Background = background;
    }

    public bool Equals(ConsoleCell other)
    {
        return Character == other.Character && 
               Foreground == other.Foreground && 
               Background == other.Background;
    }
}

/// <summary>
/// Manages console display with differential updates for improved performance
/// </summary>
public class ConsoleManager
{
    private readonly object _lockObject = new object();
    private int _currentWidth;
    private int _currentHeight;
    private bool _isInitialized;
    private ConsoleCell[,] _currentBuffer;
    private ConsoleCell[,] _previousBuffer;
    private bool _needsFullRedraw;
    
    // Performance optimization: reuse StringBuilder for clearing lines
    private readonly StringBuilder _clearLineBuilder = new StringBuilder();
    
    // Performance tracking
    private int _totalRenderCalls = 0;
    private int _totalCellsChanged = 0;

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
        _currentBuffer = new ConsoleCell[0, 0];
        _previousBuffer = new ConsoleCell[0, 0];
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

            // Initialize buffers
            InitializeBuffers();
            _needsFullRedraw = true;

            _isInitialized = true;
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

                // Reinitialize buffers for new size
                InitializeBuffers();
                _needsFullRedraw = true;

                SizeChanged?.Invoke(_currentWidth, _currentHeight);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Clear the current buffer (will be applied on next render)
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            var emptyCell = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
            
            for (int y = 0; y < _currentHeight; y++)
            {
                for (int x = 0; x < _currentWidth; x++)
                {
                    _currentBuffer[x, y] = emptyCell;
                }
            }
        }
    }

    /// <summary>
    /// Set text at a specific position in the buffer (will be applied on next render)
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
            if (x >= 0 && y >= 0 && y < _currentHeight && !string.IsNullOrEmpty(text))
            {
                var fg = foreground ?? ConsoleColor.Gray;
                var bg = background ?? ConsoleColor.Black;

                var maxLength = Math.Min(text.Length, _currentWidth - x);
                for (int i = 0; i < maxLength; i++)
                {
                    _currentBuffer[x + i, y] = new ConsoleCell(text[i], fg, bg);
                }
            }
        }
    }

    /// <summary>
    /// Set a single character at a specific position in the buffer
    /// </summary>
    /// <param name="x">X coordinate</param>
    /// <param name="y">Y coordinate</param>
    /// <param name="character">Character to write</param>
    /// <param name="foreground">Foreground color (optional)</param>
    /// <param name="background">Background color (optional)</param>
    public void WriteCharAt(int x, int y, char character, ConsoleColor? foreground = null, ConsoleColor? background = null)
    {
        lock (_lockObject)
        {
            if (x >= 0 && y >= 0 && x < _currentWidth && y < _currentHeight)
            {
                var fg = foreground ?? ConsoleColor.Gray;
                var bg = background ?? ConsoleColor.Black;
                _currentBuffer[x, y] = new ConsoleCell(character, fg, bg);
            }
        }
    }

    /// <summary>
    /// Draw a vertical separator between left and right panes in the buffer
    /// </summary>
    public void DrawVerticalSeparator()
    {
        lock (_lockObject)
        {
            var separatorX = _currentWidth / 2;
            for (int y = 0; y < _currentHeight; y++)
            {
                WriteCharAt(separatorX, y, '│', ConsoleColor.DarkGray);
            }
        }
    }

    /// <summary>
    /// Render the current buffer to the console, only updating changed cells
    /// </summary>
    public void Render()
    {
        lock (_lockObject)
        {
            if (!_isInitialized)
                return;

            _totalRenderCalls++;
            var changedCells = 0;
            var originalForeground = Console.ForegroundColor;
            var originalBackground = Console.BackgroundColor;
            
            try
            {
                // If we need a full redraw, clear the screen first
                if (_needsFullRedraw)
                {
                    Console.Clear();
                    _needsFullRedraw = false;
                }

                // Track current colors to minimize color change calls
                var currentForeground = originalForeground;
                var currentBackground = originalBackground;

                // Optimize: batch consecutive character writes where possible
                for (int y = 0; y < _currentHeight; y++)
                {
                    var lineHasChanges = false;
                    var batchStart = -1;
                    var batchLength = 0;
                    var batchForeground = ConsoleColor.Gray;
                    var batchBackground = ConsoleColor.Black;
                    _clearLineBuilder.Clear();

                    // First pass: check if this line has any changes and build batch
                    for (int x = 0; x < _currentWidth; x++)
                    {
                        var currentCell = _currentBuffer[x, y];
                        var previousCell = _previousBuffer[x, y];

                        if (!currentCell.Equals(previousCell))
                        {
                            if (!lineHasChanges)
                            {
                                lineHasChanges = true;
                                batchStart = x;
                                batchForeground = currentCell.Foreground;
                                batchBackground = currentCell.Background;
                            }

                            // Check if we can continue the batch (same colors)
                            if (x == batchStart + batchLength && 
                                currentCell.Foreground == batchForeground && 
                                currentCell.Background == batchBackground)
                            {
                                _clearLineBuilder.Append(currentCell.Character);
                                batchLength++;
                            }
                            else
                            {
                                // Write the current batch if we have one
                                if (batchLength > 0)
                                {
                                    WriteBatch(batchStart, y, _clearLineBuilder.ToString(), 
                                             batchForeground, batchBackground,
                                             ref currentForeground, ref currentBackground);
                                    changedCells += batchLength;
                                }

                                // Start new batch
                                batchStart = x;
                                batchLength = 1;
                                batchForeground = currentCell.Foreground;
                                batchBackground = currentCell.Background;
                                _clearLineBuilder.Clear();
                                _clearLineBuilder.Append(currentCell.Character);
                            }

                            // Update previous buffer
                            _previousBuffer[x, y] = currentCell;
                        }
                        else if (batchLength > 0)
                        {
                            // End of batch due to unchanged cell
                            WriteBatch(batchStart, y, _clearLineBuilder.ToString(),
                                     batchForeground, batchBackground,
                                     ref currentForeground, ref currentBackground);
                            changedCells += batchLength;
                            batchLength = 0;
                            _clearLineBuilder.Clear();
                        }
                    }

                    // Write final batch if exists
                    if (batchLength > 0)
                    {
                        WriteBatch(batchStart, y, _clearLineBuilder.ToString(),
                                 batchForeground, batchBackground,
                                 ref currentForeground, ref currentBackground);
                        changedCells += batchLength;
                    }
                }

                _totalCellsChanged += changedCells;
            }
            finally
            {
                Console.ForegroundColor = originalForeground;
                Console.BackgroundColor = originalBackground;
            }

            // Optional: Log performance metrics periodically
            #if DEBUG
            if (_totalRenderCalls % 50 == 0 && _totalRenderCalls > 0)
            {
                var totalCells = _currentWidth * _currentHeight;
                var avgCellsPerRender = (double)_totalCellsChanged / _totalRenderCalls;
                var avgPercentage = (avgCellsPerRender * 100.0) / totalCells;
                System.Diagnostics.Debug.WriteLine($"Render performance: Avg {avgCellsPerRender:F1} cells/render ({avgPercentage:F1}%) over {_totalRenderCalls} renders");
            }
            #endif
        }
    }

    /// <summary>
    /// Force a full redraw on the next render
    /// </summary>
    public void ForceFullRedraw()
    {
        lock (_lockObject)
        {
            _needsFullRedraw = true;
            // Clear previous buffer to force all cells to be considered changed
            for (int y = 0; y < _currentHeight; y++)
            {
                for (int x = 0; x < _currentWidth; x++)
                {
                    _previousBuffer[x, y] = new ConsoleCell('\0', ConsoleColor.Black, ConsoleColor.Black);
                }
            }
        }
    }

    /// <summary>
    /// Get performance statistics for debugging
    /// </summary>
    public string GetPerformanceStats()
    {
        lock (_lockObject)
        {
            if (_totalRenderCalls == 0) return "No renders yet";
            
            var totalCells = _currentWidth * _currentHeight;
            var avgCellsPerRender = (double)_totalCellsChanged / _totalRenderCalls;
            var avgPercentage = (avgCellsPerRender * 100.0) / totalCells;
            
            return $"Renders: {_totalRenderCalls}, Avg cells/render: {avgCellsPerRender:F1} ({avgPercentage:F1}%)";
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

    private void WriteBatch(int x, int y, string text, ConsoleColor foreground, ConsoleColor background,
                           ref ConsoleColor currentForeground, ref ConsoleColor currentBackground)
    {
        Console.SetCursorPosition(x, y);
        
        if (foreground != currentForeground)
        {
            Console.ForegroundColor = foreground;
            currentForeground = foreground;
        }
        
        if (background != currentBackground)
        {
            Console.BackgroundColor = background;
            currentBackground = background;
        }
        
        Console.Write(text);
    }

    private void UpdateSize()
    {
        _currentWidth = Console.WindowWidth;
        _currentHeight = Console.WindowHeight;
    }

    private void InitializeBuffers()
    {
        _currentBuffer = new ConsoleCell[_currentWidth, _currentHeight];
        _previousBuffer = new ConsoleCell[_currentWidth, _currentHeight];

        var emptyCell = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        var invalidCell = new ConsoleCell('\0', ConsoleColor.Black, ConsoleColor.Black);

        // Initialize with empty cells
        for (int y = 0; y < _currentHeight; y++)
        {
            for (int x = 0; x < _currentWidth; x++)
            {
                _currentBuffer[x, y] = emptyCell;
                _previousBuffer[x, y] = invalidCell; // Force initial draw
            }
        }
    }
}