namespace DatabaseGrinder.Services;

/// <summary>
/// Summary of replication status across all replicas
/// </summary>
public class ReplicationSummary
{
	public int TotalReplicas { get; set; }
	public int ConnectedReplicas { get; set; }
	public int DisconnectedReplicas { get; set; }
	public int ErrorReplicas { get; set; }
	public TimeSpan? MaxTimeLag { get; set; }
	public TimeSpan? AverageTimeLag { get; set; }

	public bool AllReplicasHealthy => ConnectedReplicas == TotalReplicas;
	public bool HasCriticalLag => MaxTimeLag.HasValue && MaxTimeLag.Value.TotalSeconds > 10;
}