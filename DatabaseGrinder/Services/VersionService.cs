namespace DatabaseGrinder.Services;

/// <summary>
/// Service for retrieving version information from Nerdbank GitVersioning
/// </summary>
public static class VersionService
{
	/// <summary>
	/// Get the application version in Major.Minor.Patch format (first 3 parts only)
	/// </summary>
	/// <param name="includePrefix">Whether to include 'v' prefix (default: false)</param>
	/// <returns>Version string in format "1.4.1" or "v1.4.1"</returns>
	public static string GetVersion(bool includePrefix = false)
	{
		string versionString;

		try
		{
			var assembly = System.Reflection.Assembly.GetEntryAssembly();

			// Try to get FileVersion first - this contains the full version (e.g., 1.4.1.5821)
			// We want to extract only the first 3 parts (e.g., 1.4.1)
			try
			{
				var location = assembly?.Location;
				if (!string.IsNullOrEmpty(location))
				{
					var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(location);
					if (!string.IsNullOrEmpty(fileVersionInfo.FileVersion))
					{
						// Parse FileVersion (e.g., "1.4.1.5821") and extract first 3 parts (1.4.1)
						var parts = fileVersionInfo.FileVersion.Split('.');
						if (parts.Length >= 3)
						{
							// Format as Major.Minor.Patch (first 3 parts only)
							versionString = $"{parts[0]}.{parts[1]}.{parts[2]}";
						}
						else
						{
							versionString = fileVersionInfo.FileVersion;
						}
					}
					else
					{
						throw new InvalidOperationException("FileVersion is empty");
					}
				}
				else
				{
					throw new InvalidOperationException("Assembly location is empty");
				}
			}
			catch
			{
				// Fallback: try to get AssemblyInformationalVersion 
				var informationalVersionAttribute = assembly?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
					.FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

				if (informationalVersionAttribute != null && !string.IsNullOrEmpty(informationalVersionAttribute.InformationalVersion))
				{
					// Parse "1.4.1+16bd36ca4e" format - take the version part before the '+'
					versionString = informationalVersionAttribute.InformationalVersion.Split('+')[0];
				}
				else
				{
					versionString = "1.4.0";
				}
			}
		}
		catch
		{
			versionString = "1.4.0";
		}

		return includePrefix ? $"v{versionString}" : versionString;
	}
}