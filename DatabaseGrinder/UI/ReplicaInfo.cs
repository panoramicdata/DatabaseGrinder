namespace DatabaseGrinder.UI;

/// <summary>
/// Replica connection information for display
/// </summary>
public class ReplicaInfo
{
	public string Name { get; set; } = string.Empty;
	public string ConnectionString { get; set; } = string.Empty;
	public ConnectionStatus Status { get; set; } = ConnectionStatus.Unknown;
	public TimeSpan? TimeLag { get; set; }
	public long? RecordLag { get; set; }
	public long? SequenceLag { get; set; }
	public int MissingSequenceCount { get; set; }
	public List<long> MissingSequences { get; set; } = [];
	public DateTime? LastChecked { get; set; }
	public string? ErrorMessage { get; set; }
}
