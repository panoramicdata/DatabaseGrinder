namespace DatabaseGrinder.UI.Console;

/// <summary>
/// Line drawing character sets for different terminal capabilities
/// </summary>
public static class LineChars
{
	// Unicode box drawing characters (preferred)
	public static class Unicode
	{
		public const char VerticalLine = '?';       // ? for thick
		public const char HorizontalLine = '?';     // ? for thick
		public const char TopLeft = '?';
		public const char TopRight = '?';
		public const char BottomLeft = '?';
		public const char BottomRight = '?';
		public const char Cross = '?';
		public const char TeeDown = '?';
		public const char TeeUp = '?';
		public const char TeeRight = '?';
		public const char TeeLeft = '?';
	}

	// Extended ASCII/CP437 characters (fallback)
	public static class ExtendedAscii
	{
		public const char VerticalLine = '?';       // 179
		public const char HorizontalLine = '?';     // 196
		public const char TopLeft = '?';           // 218
		public const char TopRight = '?';          // 191
		public const char BottomLeft = '?';        // 192
		public const char BottomRight = '?';       // 217
		public const char Cross = '?';             // 197
		public const char TeeDown = '?';           // 194
		public const char TeeUp = '?';             // 193
		public const char TeeRight = '?';          // 195
		public const char TeeLeft = '?';           // 180
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