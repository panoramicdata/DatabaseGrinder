using DatabaseGrinder.Services;

namespace DatabaseGrinder.UI;

/// <summary>
/// Manages the top status bar showing PostgreSQL native replication statistics
/// </summary>
/// <remarks>
/// Initializes a new instance of ReplicationStatsPane
/// </remarks>
/// <param name="consoleManager">Console manager for display operations</param>
public class ReplicationStatsPane(ConsoleManager consoleManager)
{
	private readonly Lock _lockObject = new();
	private PostgreSQLReplicationSummary? _primaryStats;
	private readonly Dictionary<string, PostgreSQLReplicationSummary> _replicaStats = [];

	/// <summary>
	/// Update PostgreSQL replication statistics for primary server
	/// </summary>
	/// <param name="stats">Primary server replication statistics</param>
	public void UpdatePrimaryStats(PostgreSQLReplicationSummary stats)
	{
		lock (_lockObject)
		{
			_primaryStats = stats;
		}
	}

	/// <summary>
	/// Update PostgreSQL replication statistics for a replica server
	/// </summary>
	/// <param name="replicaName">Name of the replica</param>
	/// <param name="stats">Replica server replication statistics</param>
	public void UpdateReplicaStats(string replicaName, PostgreSQLReplicationSummary stats)
	{
		lock (_lockObject)
		{
			_replicaStats[replicaName] = stats;
		}
	}

	/// <summary>
	/// Render the replication statistics pane (top status bar)
	/// </summary>
	public void Render()
	{
		lock (_lockObject)
		{
			var paneWidth = consoleManager.Width;
			var paneY = 1; // Right below the branding area

			// Ensure we have valid dimensions
			if (paneWidth <= 0)
				return;

			// Clear the replication stats pane area
			var clearLine = new string(' ', paneWidth);
			consoleManager.WriteAt(0, paneY, clearLine);

			// Create the status display
			var statusText = BuildStatusText();

			// Truncate if necessary and display
			if (statusText.Length > paneWidth)
			{
				statusText = statusText[..(paneWidth - 3)] + "...";
			}

			// Display with appropriate colors
			var color = GetStatusColor();
			consoleManager.WriteAt(0, paneY, statusText, color, ConsoleColor.Black);

			// Add separator line if there's space
			if (consoleManager.Height > 3)
			{
				var separatorY = paneY + 1;
				var separator = new string(consoleManager.HorizontalLineChar, paneWidth);
				consoleManager.WriteAt(0, separatorY, separator, ConsoleColor.DarkGray);
			}
		}
	}

	/// <summary>
	/// Build the status text from current statistics
	/// </summary>
	private string BuildStatusText()
	{
		if (_primaryStats == null)
		{
			return "PostgreSQL Replication: Gathering statistics...";
		}

		var parts = new List<string>();

		// Check if we have limited access (LSN is "N/A" indicates permission issues)
		if (_primaryStats.CurrentLsn == "N/A")
		{
			parts.Add("Limited Access");
			parts.Add("Need pg_monitor role for full stats");

			// Show basic connection info we can still get
			parts.Add($"Connected: {(!string.IsNullOrEmpty(_primaryStats.CurrentLsn) ? "Yes" : "No")}");

			// Show last updated
			var ago = DateTime.UtcNow - _primaryStats.LastUpdated;
			if (ago.TotalSeconds < 60)
			{
				parts.Add($"Updated: {ago.TotalSeconds:F0}s ago");
			}
			else
			{
				parts.Add($"Updated: {ago.TotalMinutes:F0}m ago");
			}
		}
		else
		{
			// Full access - show complete stats
			// Primary server info
			if (_primaryStats.IsStandby)
			{
				parts.Add("STANDBY");
				if (_primaryStats.WalReceiverStats != null)
				{
					parts.Add($"Primary: {_primaryStats.WalReceiverStats.SenderHost}:{_primaryStats.WalReceiverStats.SenderPort}");
					parts.Add($"Status: {_primaryStats.WalReceiverStats.Status}");
				}
			}
			else
			{
				parts.Add("PRIMARY");
				parts.Add($"LSN: {_primaryStats.CurrentLsn}");
				parts.Add($"Replicas: {_primaryStats.ActiveReplicas}/{_primaryStats.ReplicationStats.Count}");

				if (_primaryStats.MaxReplayLag > 0)
				{
					var lagMs = _primaryStats.MaxReplayLag / 1000; // Convert microseconds to milliseconds
					parts.Add($"Max Lag: {lagMs}ms");
				}
			}

			// Replication slots
			if (_primaryStats.TotalSlots > 0)
			{
				parts.Add($"Slots: {_primaryStats.ActiveSlots}/{_primaryStats.TotalSlots}");
			}

			// Last updated
			var ago = DateTime.UtcNow - _primaryStats.LastUpdated;
			if (ago.TotalSeconds < 60)
			{
				parts.Add($"Updated: {ago.TotalSeconds:F0}s ago");
			}
			else
			{
				parts.Add($"Updated: {ago.TotalMinutes:F0}m ago");
			}
		}

		return "PostgreSQL: " + string.Join(" | ", parts);
	}

	/// <summary>
	/// Get the appropriate status color based on replication health
	/// </summary>
	private ConsoleColor GetStatusColor()
	{
		if (_primaryStats == null)
		{
			return ConsoleColor.Yellow;
		}

		// Check for limited access
		if (_primaryStats.CurrentLsn == "N/A")
		{
			return ConsoleColor.DarkYellow; // Orange-ish to indicate limited access
		}

		// Check for any issues
		if (_primaryStats.IsStandby)
		{
			// For standby servers, check WAL receiver status
			if (_primaryStats.WalReceiverStats?.Status != "streaming")
			{
				return ConsoleColor.Red;
			}
		}
		else
		{
			// For primary servers, check replica lag
			if (_primaryStats.MaxReplayLag > 10_000_000) // 10 seconds in microseconds
			{
				return ConsoleColor.Red;
			}
			else if (_primaryStats.MaxReplayLag > 2_000_000) // 2 seconds in microseconds
			{
				return ConsoleColor.Yellow;
			}
		}

		// Check if data is stale
		var age = DateTime.UtcNow - _primaryStats.LastUpdated;
		if (age.TotalSeconds > 30)
		{
			return ConsoleColor.DarkGray;
		}

		return ConsoleColor.Green;
	}

	/// <summary>
	/// Get detailed replication information for logging/debugging
	/// </summary>
	public string GetDetailedStatus()
	{
		lock (_lockObject)
		{
			if (_primaryStats == null)
			{
				return "No PostgreSQL replication statistics available";
			}

			var details = new List<string>
			{
				$"Server Type: {(_primaryStats.IsStandby ? "Standby" : "Primary")}",
				$"Current LSN: {_primaryStats.CurrentLsn}",
				$"Last Updated: {_primaryStats.LastUpdated:yyyy-MM-dd HH:mm:ss} UTC"
			};

			if (_primaryStats.IsStandby && _primaryStats.WalReceiverStats != null)
			{
				var wal = _primaryStats.WalReceiverStats;
				details.AddRange([
					$"WAL Receiver Status: {wal.Status}",
					$"Primary: {wal.SenderHost}:{wal.SenderPort}",
					$"Written LSN: {wal.WrittenLsn}",
					$"Flushed LSN: {wal.FlushedLsn}",
					$"Slot: {wal.SlotName}"
				]);
			}
			else if (!_primaryStats.IsStandby)
			{
				details.Add($"Active Replicas: {_primaryStats.ActiveReplicas}");
				details.Add($"Max Replay Lag: {_primaryStats.MaxReplayLag / 1000}ms");

				foreach (var replica in _primaryStats.ReplicationStats)
				{
					details.Add($"  - {replica.ApplicationName} ({replica.ClientAddr}): {replica.State}, Lag: {replica.ReplayLag / 1000}ms");
				}
			}

			details.Add($"Replication Slots: {_primaryStats.ActiveSlots}/{_primaryStats.TotalSlots}");
			foreach (var slot in _primaryStats.ReplicationSlots)
			{
				var status = slot.Active ? "Active" : "Inactive";
				details.Add($"  - {slot.SlotName} ({slot.SlotType}): {status}");
			}

			return string.Join(Environment.NewLine, details);
		}
	}
}