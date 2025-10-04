using DatabaseGrinder.Services;

namespace DatabaseGrinder.UI.Console;

/// <summary>
/// Service for generating branding text and footer content
/// </summary>
public class BrandingService
{
	private readonly TerminalCapabilities _terminalCapabilities;

	/// <summary>
	/// Initializes a new instance of BrandingService
	/// </summary>
	/// <param name="terminalCapabilities">Terminal capabilities service</param>
	public BrandingService(TerminalCapabilities terminalCapabilities)
	{
		_terminalCapabilities = terminalCapabilities;
	}

	/// <summary>
	/// Get the branding text with appropriate logo character
	/// </summary>
	public string GetBrandingText()
	{
		const string nkoChar = "?"; // Unicode Nko letter FA

		// Get version using the centralized VersionService
		var versionString = VersionService.GetVersion(includePrefix: true);

		if (_terminalCapabilities.SupportsUnicode)
		{
			// Try Unicode logo first - with proper spacing: [logo] [space] [panoramic data] [DatabaseGrinder] [version] [URL]
			try
			{
				return $"{nkoChar} panoramic data  DatabaseGrinder {versionString}  https://panoramicdata.com/";
			}
			catch
			{
				// Fallback if Unicode char fails
			}
		}

		// Fallback without logo character
		return $"panoramic data  DatabaseGrinder {versionString}  https://panoramicdata.com/";
	}

	/// <summary>
	/// Get the footer shortcuts text
	/// </summary>
	public string GetFooterText()
	{
		return "Q = Quit   R = Refresh   ESC/Ctrl+C = exit   Ctrl+Q = Delete r/o user, delete database and exit";
	}

	/// <summary>
	/// Get the length of the logo portion (for background coloring)
	/// </summary>
	public int GetLogoLength()
	{
		if (_terminalCapabilities.SupportsUnicode)
		{
			// "? panoramic data" - includes space between Unicode char and text
			return "? panoramic data".Length;
		}
		else
		{
			// "panoramic data"
			return "panoramic data".Length;
		}
	}
}