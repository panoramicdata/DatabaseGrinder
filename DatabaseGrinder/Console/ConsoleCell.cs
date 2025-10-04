namespace DatabaseGrinder.UI.Console;

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