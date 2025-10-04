using System.Text;

namespace DatabaseGrinder.UI.Console;

/// <summary>
/// Manages the console buffer for differential rendering
/// </summary>
public class ConsoleBuffer
{
	private ConsoleCell[,] _currentBuffer = new ConsoleCell[0, 0];
	private ConsoleCell[,] _previousBuffer = new ConsoleCell[0, 0];
	private bool _needsFullRedraw;

	// Performance optimization: reuse StringBuilder for clearing lines
	private readonly StringBuilder _clearLineBuilder = new();

	// Performance tracking
	private int _totalRenderCalls = 0;
	private int _totalCellsChanged = 0;

	/// <summary>
	/// Current buffer width
	/// </summary>
	public int Width { get; private set; }

	/// <summary>
	/// Current buffer height
	/// </summary>
	public int Height { get; private set; }

	/// <summary>
	/// Initialize the buffers with the specified dimensions
	/// </summary>
	/// <param name="width">Buffer width</param>
	/// <param name="height">Buffer height</param>
	public void Initialize(int width, int height)
	{
		Width = width;
		Height = height;

		_currentBuffer = new ConsoleCell[width, height];
		_previousBuffer = new ConsoleCell[width, height];

		var emptyCell = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
		var invalidCell = new ConsoleCell('\0', ConsoleColor.Black, ConsoleColor.Black);

		// Initialize with empty cells
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				_currentBuffer[x, y] = emptyCell;
				_previousBuffer[x, y] = invalidCell; // Force initial draw
			}
		}

		_needsFullRedraw = true;
	}

	/// <summary>
	/// Clear the current buffer (will be applied on next render)
	/// </summary>
	public void Clear()
	{
		var emptyCell = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);

		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				_currentBuffer[x, y] = emptyCell;
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
		if (x >= 0 && y >= 0 && y < Height && !string.IsNullOrEmpty(text))
		{
			var fg = foreground ?? ConsoleColor.Gray;
			var bg = background ?? ConsoleColor.Black;

			// Ensure we don't write beyond console boundaries
			var maxLength = Math.Min(text.Length, Width - x);
			for (int i = 0; i < maxLength; i++)
			{
				if (x + i < Width) // Double-check boundary
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
		if (x >= 0 && y >= 0 && x < Width && y < Height)
		{
			var fg = foreground ?? ConsoleColor.Gray;
			var bg = background ?? ConsoleColor.Black;
			_currentBuffer[x, y] = new ConsoleCell(character, fg, bg);
		}
	}

	/// <summary>
	/// Force a full redraw on the next render
	/// </summary>
	public void ForceFullRedraw()
	{
		_needsFullRedraw = true;
		// Clear previous buffer to force all cells to be considered changed
		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				_previousBuffer[x, y] = new ConsoleCell('\0', ConsoleColor.Black, ConsoleColor.Black);
			}
		}
	}

	/// <summary>
	/// Render the current buffer to the console, only updating changed cells
	/// </summary>
	public void Render()
	{
		_totalRenderCalls++;
		var changedCells = 0;
		var originalForeground = System.Console.ForegroundColor;
		var originalBackground = System.Console.BackgroundColor;

		try
		{
			// If we need a full redraw, clear the screen first
			if (_needsFullRedraw)
			{
				System.Console.Clear();
				_needsFullRedraw = false;
			}

			// Track current colors to minimize color change calls
			var currentForeground = originalForeground;
			var currentBackground = originalBackground;

			// Optimize: batch consecutive character writes where possible
			for (int y = 0; y < Height; y++)
			{
				var lineHasChanges = false;
				var batchStart = -1;
				var batchLength = 0;
				var batchForeground = ConsoleColor.Gray;
				var batchBackground = ConsoleColor.Black;
				_clearLineBuilder.Clear();

				// First pass: check if this line has any changes and build batch
				for (int x = 0; x < Width; x++)
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
			System.Console.ForegroundColor = originalForeground;
			System.Console.BackgroundColor = originalBackground;
		}

#if DEBUG
		// Optional: Log performance metrics periodically
		if (_totalRenderCalls % 100 == 0 && _totalRenderCalls > 0)
		{
			var totalCells = Width * Height;
			var avgCellsPerRender = (double)_totalCellsChanged / _totalRenderCalls;
			var avgPercentage = (avgCellsPerRender * 100.0) / totalCells;
			System.Diagnostics.Debug.WriteLine($"Render performance: Avg {avgCellsPerRender:F1} cells/render ({avgPercentage:F1}%) over {_totalRenderCalls} renders");
		}
#endif
	}

	private void WriteBatch(int x, int y, string text, ConsoleColor foreground, ConsoleColor background,
						   ref ConsoleColor currentForeground, ref ConsoleColor currentBackground)
	{
		try
		{
			// Ensure we don't write beyond console boundaries
			if (x >= 0 && y >= 0 && x < Width && y < Height)
			{
				System.Console.SetCursorPosition(x, y);

				if (foreground != currentForeground)
				{
					System.Console.ForegroundColor = foreground;
					currentForeground = foreground;
				}

				if (background != currentBackground)
				{
					System.Console.BackgroundColor = background;
					currentBackground = background;
				}

				// Truncate text if it would exceed console width
				var maxLength = Width - x;
				if (text.Length > maxLength)
				{
					text = text[..maxLength];
				}

				System.Console.Write(text);
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			// Ignore cursor positioning errors - console may have been resized
		}
	}

	/// <summary>
	/// Get performance statistics for debugging
	/// </summary>
	public string GetPerformanceStats()
	{
		if (_totalRenderCalls == 0) return "No renders yet";

		var totalCells = Width * Height;
		var avgCellsPerRender = (double)_totalCellsChanged / _totalRenderCalls;
		var avgPercentage = (avgCellsPerRender * 100.0) / totalCells;

		return $"Renders: {_totalRenderCalls}, Avg cells/render: {avgCellsPerRender:F1} ({avgPercentage:F1}%)";
	}
}