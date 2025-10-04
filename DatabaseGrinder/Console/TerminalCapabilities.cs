using System.Runtime.InteropServices;

namespace DatabaseGrinder.UI.Console;

/// <summary>
/// Service for detecting terminal capabilities and Unicode support
/// </summary>
public class TerminalCapabilities
{
	/// <summary>
	/// Whether the terminal supports Unicode characters
	/// </summary>
	public bool SupportsUnicode { get; private set; } = true;

	/// <summary>
	/// Whether the terminal supports Extended ASCII characters
	/// </summary>
	public bool SupportsExtendedAscii { get; private set; } = true;

	/// <summary>
	/// Initialize and detect terminal capabilities
	/// </summary>
	public void Initialize()
	{
		DetectTerminalCapabilities();
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
				SupportsUnicode = true;
				SupportsExtendedAscii = true;
			}
			// Basic Windows Command Prompt may have limited support
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				// Test if we can write Unicode characters
				SupportsUnicode = TestUnicodeSupport();
				SupportsExtendedAscii = true; // Most Windows consoles support extended ASCII
			}
			else
			{
				// Linux/Mac terminals generally support Unicode
				SupportsUnicode = true;
				SupportsExtendedAscii = true;
			}
		}
		catch
		{
			// Fallback to basic ASCII if detection fails
			SupportsUnicode = false;
			SupportsExtendedAscii = false;
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
			System.Console.OutputEncoding = System.Text.Encoding.UTF8;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Get the appropriate vertical line character for the current terminal
	/// </summary>
	public char GetVerticalLineChar() => SupportsUnicode ? LineChars.Unicode.VerticalLine :
								   SupportsExtendedAscii ? LineChars.ExtendedAscii.VerticalLine :
								   LineChars.Ascii.VerticalLine;

	/// <summary>
	/// Get the appropriate horizontal line character for the current terminal
	/// </summary>
	public char GetHorizontalLineChar() => SupportsUnicode ? LineChars.Unicode.HorizontalLine :
									 SupportsExtendedAscii ? LineChars.ExtendedAscii.HorizontalLine :
									 LineChars.Ascii.HorizontalLine;

	/// <summary>
	/// Get the appropriate T-piece (tee down) character for the current terminal
	/// </summary>
	public char GetTeeDownChar() => SupportsUnicode ? LineChars.Unicode.TeeDown :
			   SupportsExtendedAscii ? LineChars.ExtendedAscii.TeeDown :
			   LineChars.Ascii.TeeDown;

	/// <summary>
	/// Get the appropriate T-piece (tee up) character for the current terminal
	/// </summary>
	public char GetTeeUpChar() => SupportsUnicode ? LineChars.Unicode.TeeUp :
			   SupportsExtendedAscii ? LineChars.ExtendedAscii.TeeUp :
			   LineChars.Ascii.TeeUp;

	/// <summary>
	/// Get the appropriate T-piece (tee right) character for the current terminal
	/// </summary>
	public char GetTeeRightChar() => SupportsUnicode ? LineChars.Unicode.TeeRight :
			   SupportsExtendedAscii ? LineChars.ExtendedAscii.TeeRight :
			   LineChars.Ascii.TeeRight;

	/// <summary>
	/// Get the appropriate T-piece (tee left) character for the current terminal
	/// </summary>
	public char GetTeeLeftChar() => SupportsUnicode ? LineChars.Unicode.TeeLeft :
			   SupportsExtendedAscii ? LineChars.ExtendedAscii.TeeLeft :
			   LineChars.Ascii.TeeLeft;
}