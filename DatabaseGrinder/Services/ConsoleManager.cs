using System.Runtime.InteropServices;
using System.Text;

namespace DatabaseGrinder.Services;

/// <summary>
/// Represents a single character cell in the console buffer
/// </summary>
public struct ConsoleCell(char character, ConsoleColor foreground = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black)
{
	public char Character { get; set; } = character;
	public ConsoleColor Foreground { get; set; } = foreground;
	public ConsoleColor Background { get; set; } = background;

	public readonly bool Equals(ConsoleCell other) => Character == other.Character &&
			   Foreground == other.Foreground &&
			   Background == other.Background;
}

/// <summary>
/// Line drawing character sets for different terminal capabilities
/// </summary>
public static class LineChars
{
	// Unicode box drawing characters (preferred)
	public static class Unicode
	{
		public const char VerticalLine = '│';       // ┃ for thick
		public const char HorizontalLine = '─';     // ━ for thick
		public const char TopLeft = '┌';
		public const char TopRight = '┐';
		public const char BottomLeft = '└';
		public const char BottomRight = '┘';
		public const char Cross = '┼';
		public const char TeeDown = '┬';
		public const char TeeUp = '┴';
		public const char TeeRight = '├';
		public const char TeeLeft = '┤';
	}

	// Extended ASCII/CP437 characters (fallback)
	public static class ExtendedAscii
	{
		public const char VerticalLine = '│';       // 179
		public const char HorizontalLine = '─';     // 196
		public const char TopLeft = '┌';           // 218
		public const char TopRight = '┐';          // 191
		public const char BottomLeft = '└';        // 192
		public const char BottomRight = '┘';       // 217
		public const char Cross = '┼';             // 197
		public const char TeeDown = '┬';           // 194
		public const char TeeUp = '┴';             // 193
		public const char TeeRight = '├';          // 195
		public const char TeeLeft = '┤';           // 180
	}

	// Basic ASCII (ultimate fallback)
	public static class Ascii
	{
		public const char VerticalLine = '|';
		public const char HorizontalLine = '-';
		public const char TopLeft = '+';
		public const char TopRight = '+';
		public const char BottomLeft = '+';
		public const char BottomRight = '+';
		public const char Cross = '+';
		public const char TeeDown = '+';
		public const char TeeUp = '+';
		public const char TeeRight = '+';
		public const char TeeLeft = '+';
	}
}

/// <summary>
/// Manages console display with differential updates for improved performance
/// </summary>
/// <remarks>
/// Initializes a new instance of ConsoleManager
/// </remarks>
/// <param name="minWidth">Minimum console width to support</param>
/// <param name="minHeight">Minimum console height to support</param>
public class ConsoleManager(int minWidth = 80, int minHeight = 25)
{
	private readonly Lock _lockObject = new();
	private bool _isInitialized;
	private ConsoleCell[,] _currentBuffer = new ConsoleCell[0, 0];
	private ConsoleCell[,] _previousBuffer = new ConsoleCell[0, 0];
	private bool _needsFullRedraw;
	private bool _supportsUnicode = true;
	private bool _supportsExtendedAscii = true;

	// Performance optimization: reuse StringBuilder for clearing lines
	private readonly StringBuilder _clearLineBuilder = new();

	// Performance tracking
	private int _totalRenderCalls = 0;
	private int _totalCellsChanged = 0;

	/// <summary>
	/// Minimum supported console width
	/// </summary>
	public int MinWidth { get; } = minWidth;

	/// <summary>
	/// Minimum supported console height
	/// </summary>
	public int MinHeight { get; } = minHeight;

	/// <summary>
	/// Current console width
	/// </summary>
	public int Width { get; private set; }

	/// <summary>
	/// Current console height
	/// </summary>
	public int Height { get; private set; }

	/// <summary>
	/// Height of the branding area at the top (1 row for compact branding)
	/// </summary>
	public static int BrandingHeight => 1;

	/// <summary>
	/// Height of the footer area at the bottom (1 row for shortcuts)
	/// </summary>
	public static int FooterHeight => 1;

	/// <summary>
	/// Available height for content (excluding branding and footer areas)
	/// </summary>
	public int ContentHeight => Math.Max(0, Height - BrandingHeight - FooterHeight);

	/// <summary>
	/// Y position where content starts (after branding area)
	/// </summary>
	public int ContentStartY => BrandingHeight;

	/// <summary>
	/// Y position where footer starts
	/// </summary>
	public int FooterStartY => Height - FooterHeight;

	/// <summary>
	/// X position of the vertical separator
	/// </summary>
	public int SeparatorX => Width / 2;

	/// <summary>
	/// Gets the width of the left pane (excluding the separator)
	/// Left pane uses columns 0 to SeparatorX-1
	/// </summary>
	public int LeftPaneWidth => Math.Max(0, SeparatorX);

	/// <summary>
	/// Gets the starting X position of the right pane
	/// Right pane starts after the separator
	/// </summary>
	public int RightPaneStartX => SeparatorX + 1;

	/// <summary>
	/// Gets the width of the right pane (excluding the separator)
	/// Right pane uses columns SeparatorX+1 to Width-1
	/// </summary>
	public int RightPaneWidth => Math.Max(0, Width - RightPaneStartX);

	/// <summary>
	/// Event raised when console size changes
	/// </summary>
	public event Action<int, int>? SizeChanged;

	/// <summary>
	/// Get the appropriate vertical line character for the current terminal
	/// </summary>
	public char VerticalLineChar => _supportsUnicode ? LineChars.Unicode.VerticalLine :
								   _supportsExtendedAscii ? LineChars.ExtendedAscii.VerticalLine :
								   LineChars.Ascii.VerticalLine;

	/// <summary>
	/// Get the appropriate horizontal line character for the current terminal
	/// </summary>
	public char HorizontalLineChar => _supportsUnicode ? LineChars.Unicode.HorizontalLine :
									 _supportsExtendedAscii ? LineChars.ExtendedAscii.HorizontalLine :
									 LineChars.Ascii.HorizontalLine;

	/// <summary>
	/// Get detailed layout information for debugging
	/// </summary>
	/// <returns>Layout information string</returns>
	public string GetLayoutInfo() => $"Console: {Width}x{Height}, " +
			   $"Content: {Width}x{ContentHeight} (starts at Y:{ContentStartY}), " +
			   $"Left: 0-{LeftPaneWidth - 1} ({LeftPaneWidth} chars), " +
			   $"Sep: {SeparatorX}, " +
			   $"Right: {RightPaneStartX}-{Width - 1} ({RightPaneWidth} chars)";

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

			// Test Unicode support
			DetectTerminalCapabilities();

			// Try to prevent window resizing below minimum (platform dependent)
			TrySetMinimumWindowSize();

			// Check if console is too small
			if (Width < MinWidth || Height < MinHeight)
			{
				ShowWindowTooSmallMessage();
				return; // Don't initialize buffers, just show the message
			}

			// Validate layout makes sense
			if (LeftPaneWidth + RightPaneWidth + 1 != Width) // +1 for separator
			{
				throw new InvalidOperationException(
					$"Layout calculation error: Left({LeftPaneWidth}) + Sep(1) + Right({RightPaneWidth}) != Total({Width})");
			}

			// Initialize buffers
			InitializeBuffers();
			_needsFullRedraw = true;

			_isInitialized = true;
		}
	}

	/// <summary>
	/// Detect what line drawing capabilities the terminal supports
	/// </summary>
	private void DetectTerminalCapabilities()
	{
		try
		{
			// Try to detect terminal type
			var term = Environment.GetEnvironmentVariable("TERM");
			var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");

			// Windows Terminal, modern terminals generally support Unicode
			if (termProgram == "vscode" || termProgram == "Windows Terminal" ||
				term?.Contains("xterm") == true || term?.Contains("screen") == true)
			{
				_supportsUnicode = true;
				_supportsExtendedAscii = true;
			}
			// Basic Windows Command Prompt may have limited support
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Test if we can write Unicode characters
				_supportsUnicode = TestUnicodeSupport();
				_supportsExtendedAscii = true; // Most Windows consoles support extended ASCII
			}
			else
			{
				// Linux/Mac terminals generally support Unicode
				_supportsUnicode = true;
				_supportsExtendedAscii = true;
			}
		}
		catch
		{
			// Fallback to basic ASCII if detection fails
			_supportsUnicode = false;
			_supportsExtendedAscii = false;
		}
	}

	/// <summary>
	/// Test if the terminal supports Unicode line drawing characters
	/// </summary>
	private static bool TestUnicodeSupport()
	{
		try
		{
			// Simple test - try to use Unicode and see if it causes issues
			Console.OutputEncoding = System.Text.Encoding.UTF8;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Try to set minimum window size (platform dependent)
	/// </summary>
	private void TrySetMinimumWindowSize()
	{
		try
		{
			// Only works on Windows and some terminals
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Try to set minimum window size if supported
				// This may not work in all terminal types
				var currentWidth = Console.WindowWidth;
				var currentHeight = Console.WindowHeight;

				if (currentWidth < MinWidth || currentHeight < MinHeight)
				{
					try
					{
						Console.SetWindowSize(
							Math.Max(currentWidth, MinWidth),
							Math.Max(currentHeight, MinHeight)
						);
					}
					catch
					{
						// Silently fail if setting window size is not supported
					}
				}
			}
		}
		catch
		{
			// Platform doesn't support window size manipulation
		}
	}

	/// <summary>
	/// Show "Window too small" message centered on screen
	/// </summary>
	private void ShowWindowTooSmallMessage()
	{
		try
		{
			Console.Clear();
			Console.CursorVisible = false;

			var messages = new[]
			{
				"Window too small",
				$"Minimum: {MinWidth}x{MinHeight}",
				$"Current: {Width}x{Height}",
				"",
				"Please resize your terminal window",
				"Press any key to retry..."
			};

			var startY = Math.Max(0, (Height - messages.Length) / 2);

			for (int i = 0; i < messages.Length; i++)
			{
				var message = messages[i];
				var x = Math.Max(0, (Width - message.Length) / 2);
				var y = Math.Min(startY + i, Height - 1);

				if (y >= 0 && y < Height)
				{
					Console.SetCursorPosition(x, y);
					if (i == 0) // Title
					{
						Console.ForegroundColor = ConsoleColor.Red;
					}
					else if (i == 1 || i == 2) // Size info
					{
						Console.ForegroundColor = ConsoleColor.Yellow;
					}
					else if (i == 4) // Instructions
					{
						Console.ForegroundColor = ConsoleColor.Cyan;
					}
					else
					{
						Console.ForegroundColor = ConsoleColor.Gray;
					}

					Console.Write(message);
				}
			}

			Console.ResetColor();
		}
		catch
		{
			// Fallback for very small or problematic terminals
			Console.Clear();
			Console.WriteLine("Window too small - please resize");
		}
	}

	/// <summary>
	/// Draw the permanent branding area at the top and footer at the bottom
	/// </summary>
	public void DrawBrandingArea()
	{
		lock (_lockObject)
		{
			// === TOP BRANDING ROW ===
			// Clear the branding area
			for (int brandX = 0; brandX < Width; brandX++)
			{
				_currentBuffer[brandX, 0] = new ConsoleCell(' ', ConsoleColor.White, ConsoleColor.Black);
			}

			// Create branding text with logo
			var brandText = GetBrandingText();

			// Draw branding with orange background for the logo part only
			var brandingX = 0;

			for (int i = 0; i < brandText.Length && brandingX < Width; i++)
			{
				var ch = brandText[i];
				var fg = ConsoleColor.White;
				var bg = ConsoleColor.Black; // Default background

				// Apply orange background only to the Unicode character and "panoramic data" but NOT the space between them
				if (_supportsUnicode)
				{
					// "ߝ panoramic data" - orange background on Unicode char and "panoramic data", transparent space
					if (i == 0) // Unicode character ߝ
					{
						bg = ConsoleColor.DarkYellow;
					}
					else if (i == 1) // Space after Unicode character - keep transparent/black
					{
						bg = ConsoleColor.Black;
					}
					else if (i >= 2 && i < "ߝ panoramic data".Length) // "panoramic data"
					{
						bg = ConsoleColor.DarkYellow;
					}
					else
					{
						fg = ConsoleColor.Gray; // Rest of text in gray
					}
				}
				else
				{
					// Non-Unicode fallback - orange background on "panoramic data" only
					if (i < "panoramic data".Length)
					{
						bg = ConsoleColor.DarkYellow;
					}
					else
					{
						fg = ConsoleColor.Gray;
					}
				}

				_currentBuffer[brandingX, 0] = new ConsoleCell(ch, fg, bg);
				brandingX++;

				// Add extra background space immediately after Unicode character if it overflows visually
				if (_supportsUnicode && i == 0 && brandingX < Width)
				{
					// The Unicode character might take more visual space, so add an extra orange background cell right after it
					_currentBuffer[brandingX, 0] = new ConsoleCell(' ', ConsoleColor.White, ConsoleColor.DarkYellow);
					brandingX++;
				}
			}

			// === BOTTOM FOOTER ROW ===
			var footerY = FooterStartY;

			// Clear the footer area
			for (int footerX = 0; footerX < Width; footerX++)
			{
				_currentBuffer[footerX, footerY] = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
			}

			// Footer shortcuts - centered
			var footerShortcuts = "Q = Quit   R = Refresh   ESC/Ctrl+C = exit   Ctrl+Q = Delete r/o user, delete database and exit";
			var footerShortcutsStartX = (Width - footerShortcuts.Length) / 2;
			if (footerShortcutsStartX >= 0)
			{
				for (int i = 0; i < footerShortcuts.Length && footerShortcutsStartX + i < Width; i++)
				{
					_currentBuffer[footerShortcutsStartX + i, footerY] = new ConsoleCell(footerShortcuts[i], ConsoleColor.DarkGray, ConsoleColor.Black);
				}
			}

			// Draw a separator line below branding if there's space
			if (Height > 2)
			{
				for (int sepX = 0; sepX < Width; sepX++)
				{
					_currentBuffer[sepX, ContentStartY] = new ConsoleCell(HorizontalLineChar, ConsoleColor.DarkGray, ConsoleColor.Black);
				}
			}

			// Draw a separator line above footer if there's space
			if (Height > 3)
			{
				for (int sepX = 0; sepX < Width; sepX++)
				{
					_currentBuffer[sepX, FooterStartY - 1] = new ConsoleCell(HorizontalLineChar, ConsoleColor.DarkGray, ConsoleColor.Black);
				}
			}
		}
	}

	/// <summary>
	/// Get the branding text with appropriate logo character
	/// </summary>
	private string GetBrandingText()
	{
		const string nkoChar = "ߝ"; // Unicode Nko letter FA

		if (_supportsUnicode)
		{
			// Try Unicode logo first - with proper spacing: [logo] [space] [panoramic data] [DatabaseGrinder] [version] [URL]
			try
			{
				return $"{nkoChar} panoramic data  DatabaseGrinder v1.1.0  https://panoramicdata.com/";
			}
			catch
			{
				// Fallback if Unicode char fails
			}
		}

		// Fallback without logo character
		return "panoramic data  DatabaseGrinder v1.1.0  https://panoramicdata.com/";
	}

	/// <summary>
	/// Get the length of the logo portion (for background coloring)
	/// </summary>
	private int GetLogoLength()
	{
		if (_supportsUnicode)
		{
			// "ߝ panoramic data" - includes space between Unicode char and text
			return "ߝ panoramic data".Length;
		}
		else
		{
			// "panoramic data"
			return "panoramic data".Length;
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

			if (newWidth != Width || newHeight != Height)
			{
				var oldWidth = Width;
				var oldHeight = Height;

				Width = newWidth;
				Height = newHeight;

				// Check if window is now too small
				if (Width < MinWidth || Height < MinHeight)
				{
					ShowWindowTooSmallMessage();
					_isInitialized = false; // Mark as not initialized until size is acceptable
					return true;
				}

				// If we were previously too small but now acceptable, reinitialize
				if (!_isInitialized && Width >= MinWidth && Height >= MinHeight)
				{
					// Reinitialize everything
					InitializeBuffers();
					_needsFullRedraw = true;
					_isInitialized = true;

					SizeChanged?.Invoke(Width, Height);
					return true;
				}

				// Normal resize handling for initialized console
				if (_isInitialized)
				{
					// Reinitialize buffers for new size
					InitializeBuffers();
					_needsFullRedraw = true;

					SizeChanged?.Invoke(Width, Height);
					return true;
				}
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

			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
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
			if (x >= 0 && y >= 0 && x < Width && y < Height)
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
			var separatorX = SeparatorX;
			// Only draw separator if we have valid position, in content area only (excluding footer)
			if (separatorX > 0 && separatorX < Width)
			{
				// Draw the junction at the top where vertical separator meets horizontal line
				var topY = ContentStartY;
				WriteCharAt(separatorX, topY, GetTeeDownChar(), ConsoleColor.DarkGray);

				// Draw vertical line in content area
				for (int y = ContentStartY + 1; y < FooterStartY - 1; y++)
				{
					WriteCharAt(separatorX, y, VerticalLineChar, ConsoleColor.DarkGray);
				}

				// Draw the junction at the bottom where vertical separator meets footer horizontal line
				var bottomY = FooterStartY - 1;
				if (bottomY > ContentStartY)
				{
					WriteCharAt(separatorX, bottomY, GetTeeUpChar(), ConsoleColor.DarkGray);
				}
			}
		}
	}

	/// <summary>
	/// Get the appropriate T-piece (tee down) character for the current terminal
	/// </summary>
	public char GetTeeDownChar()
	{
		return _supportsUnicode ? LineChars.Unicode.TeeDown :
			   _supportsExtendedAscii ? LineChars.ExtendedAscii.TeeDown :
			   LineChars.Ascii.TeeDown;
	}

	/// <summary>
	/// Get the appropriate T-piece (tee up) character for the current terminal
	/// </summary>
	public char GetTeeUpChar()
	{
		return _supportsUnicode ? LineChars.Unicode.TeeUp :
			   _supportsExtendedAscii ? LineChars.ExtendedAscii.TeeUp :
			   LineChars.Ascii.TeeUp;
	}

	/// <summary>
	/// Get the appropriate T-piece (tee right) character for the current terminal
	/// </summary>
	public char GetTeeRightChar()
	{
		return _supportsUnicode ? LineChars.Unicode.TeeRight :
			   _supportsExtendedAscii ? LineChars.ExtendedAscii.TeeRight :
			   LineChars.Ascii.TeeRight;
	}

	/// <summary>
	/// Get the appropriate T-piece (tee left) character for the current terminal
	/// </summary>
	public char GetTeeLeftChar()
	{
		return _supportsUnicode ? LineChars.Unicode.TeeLeft :
			   _supportsExtendedAscii ? LineChars.ExtendedAscii.TeeLeft :
			   LineChars.Ascii.TeeLeft;
	}

	/// <summary>
	/// Render the current buffer to the console, only updating changed cells
	/// </summary>
	public void Render()
	{
		lock (_lockObject)
		{
			// If console is too small, just show the message
			if (Width < MinWidth || Height < MinHeight)
			{
				ShowWindowTooSmallMessage();
				return;
			}

			if (!_isInitialized)
				return;

			// Always draw branding area first
			DrawBrandingArea();

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
				Console.ForegroundColor = originalForeground;
				Console.BackgroundColor = originalBackground;
			}

			// Optional: Log performance metrics periodically
#if DEBUG
			if (_totalRenderCalls % 100 == 0 && _totalRenderCalls > 0)
			{
				var totalCells = Width * Height;
				var avgCellsPerRender = (double)_totalCellsChanged / _totalRenderCalls;
				var avgPercentage = (avgCellsPerRender * 100.0) / totalCells;
				System.Diagnostics.Debug.WriteLine($"Render performance: Avg {avgCellsPerRender:F1} cells/render ({avgPercentage:F1}%) over {_totalRenderCalls} renders");
			}
#endif
		}
	}

	private void WriteBatch(int x, int y, string text, ConsoleColor foreground, ConsoleColor background,
						   ref ConsoleColor currentForeground, ref ConsoleColor currentBackground)
	{
		try
		{
			// Ensure we don't write beyond console boundaries
			if (x >= 0 && y >= 0 && x < Width && y < Height)
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

				// Truncate text if it would exceed console width
				var maxLength = Width - x;
				if (text.Length > maxLength)
				{
					text = text[..maxLength];
				}

				Console.Write(text);
			}
		}
		catch (ArgumentOutOfRangeException)
		{
			// Ignore cursor positioning errors - console may have been resized
		}
	}

	private void UpdateSize()
	{
		Width = Console.WindowWidth;
		Height = Console.WindowHeight;
	}

	private void InitializeBuffers()
	{
		_currentBuffer = new ConsoleCell[Width, Height];
		_previousBuffer = new ConsoleCell[Width, Height];

		var emptyCell = new ConsoleCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
		var invalidCell = new ConsoleCell('\0', ConsoleColor.Black, ConsoleColor.Black);

		// Initialize with empty cells
		for (int y = 0; y < Height; y++)
		{
			for (int x = 0; x < Width; x++)
			{
				_currentBuffer[x, y] = emptyCell;
				_previousBuffer[x, y] = invalidCell; // Force initial draw
			}
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
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
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

			var totalCells = Width * Height;
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
		var unicode = _supportsUnicode ? "Unicode" : "ASCII";
		return $"{os} ({arch}) - {unicode} support";
	}
}