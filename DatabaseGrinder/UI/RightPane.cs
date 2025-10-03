using DatabaseGrinder.Services;

namespace DatabaseGrinder.UI;

/// <summary>
/// Manages the right pane display showing replication status
/// </summary>
/// <remarks>
/// Initializes a new instance of RightPane
/// </remarks>
/// <param name="consoleManager">Console manager for display operations</param>
/// <param name="logger">Logger instance</param>
public class RightPane(ConsoleManager consoleManager)
{
	private readonly List<ReplicaInfo> _replicas = [];
	private readonly Lock _lockObject = new();

	/// <summary>
	/// Add or update a replica's information
	/// </summary>
	/// <param name="replica">Replica information</param>
	public void UpdateReplica(ReplicaInfo replica)
	{
		lock (_lockObject)
		{
			var existing = _replicas.FirstOrDefault(r => r.Name == replica.Name);
			if (existing != null)
			{
				existing.Status = replica.Status;
				existing.TimeLag = replica.TimeLag;
				existing.RecordLag = replica.RecordLag;
				existing.SequenceLag = replica.SequenceLag;
				existing.MissingSequenceCount = replica.MissingSequenceCount;
				existing.MissingSequences = replica.MissingSequences;
				existing.LastChecked = replica.LastChecked;
				existing.ErrorMessage = replica.ErrorMessage;
			}
			else
			{
				_replicas.Add(replica);
			}
		}
	}

	/// <summary>
	/// Render the right pane content
	/// </summary>
	public void Render()
	{
		lock (_lockObject)
		{
			var paneWidth = consoleManager.RightPaneWidth;
			var paneStartX = consoleManager.RightPaneStartX;
			var startY = consoleManager.ContentStartY + 1; // +1 to skip separator line

			// Clear right pane content area only (content area between global header and footer)
			for (int y = startY; y < consoleManager.FooterStartY; y++)
			{
				var clearLine = new string(' ', paneWidth);
				consoleManager.WriteAt(paneStartX, y, clearLine);
			}

			// Draw main title
			var title = "REPLICATION MONITOR";
			var titleX = paneStartX + (paneWidth - title.Length) / 2;
			consoleManager.WriteAt(titleX, startY, title, ConsoleColor.White, ConsoleColor.DarkBlue);

			// Draw overall status summary
			var (Text, Color) = GetOverallStatusSummary();
			var summaryX = paneStartX + (paneWidth - Text.Length) / 2;
			consoleManager.WriteAt(summaryX, startY + 1, Text, Color);

			// Draw separator line using proper line drawing character with T-piece
			var separatorY = startY + 2;
			var separator = new string(consoleManager.HorizontalLineChar, paneWidth);
			consoleManager.WriteAt(paneStartX, separatorY, separator, ConsoleColor.DarkGray);
			// Add T-piece where this horizontal line meets the vertical separator  
			consoleManager.WriteCharAt(paneStartX - 1, separatorY, consoleManager.GetTeeRightChar(), ConsoleColor.DarkGray);

			// Calculate space for each replica (using available height to footer)
			var headerHeight = 3; // Title + status + separator
			var availableHeight = consoleManager.FooterStartY - startY - headerHeight;
			var linesPerReplica = _replicas.Count > 0 ? Math.Max(6, availableHeight / _replicas.Count) : availableHeight;

			// Draw replica information
			var currentY = separatorY + 1;
			var maxY = consoleManager.FooterStartY;

			for (int i = 0; i < _replicas.Count && currentY < maxY; i++)
			{
				var replica = _replicas[i];
				var replicaEndY = Math.Min(currentY + linesPerReplica, maxY);

				DrawReplicaWithSequenceInfo(replica, paneStartX, currentY, paneWidth, replicaEndY - currentY);

				currentY = replicaEndY;

				// Draw separator between replicas if not the last one
				if (i < _replicas.Count - 1 && currentY < maxY)
				{
					// Use proper dotted line character if available
					var repSeparator = new string('·', paneWidth);
					consoleManager.WriteAt(paneStartX, currentY, repSeparator, ConsoleColor.DarkGray);
					currentY++;
				}
			}

			// Show message if no replicas configured
			if (_replicas.Count == 0)
			{
				var noReplicasMsg = "No replicas configured";
				var msgX = paneStartX + (paneWidth - noReplicasMsg.Length) / 2;
				var msgY = startY + (availableHeight / 2);
				consoleManager.WriteAt(msgX, msgY, noReplicasMsg, ConsoleColor.Yellow);
			}
		}
	}

	private (string Text, ConsoleColor Color) GetOverallStatusSummary()
	{
		var connected = _replicas.Count(r => r.Status == ConnectionStatus.Connected);
		var total = _replicas.Count;
		var totalMissingSequences = _replicas.Sum(r => r.MissingSequenceCount);

		if (total == 0)
			return ("No replicas", ConsoleColor.Gray);

		if (connected == total)
		{
			var maxLag = _replicas.Where(r => r.TimeLag.HasValue).Max(r => r.TimeLag?.TotalMilliseconds ?? 0);

			if (totalMissingSequences > 0)
				return ($"All {total} online - {totalMissingSequences} missing", ConsoleColor.Red);
			else if (maxLag < 500)
				return ($"All {total} online - Excellent", ConsoleColor.Green);
			else if (maxLag < 2000)
				return ($"All {total} online - Good", ConsoleColor.Yellow);
			else
				return ($"All {total} online - High lag", ConsoleColor.Red);
		}
		else if (connected > 0)
		{
			return ($"{connected}/{total} online", ConsoleColor.Yellow);
		}
		else
		{
			return ($"All {total} offline", ConsoleColor.Red);
		}
	}

	private void DrawReplicaWithSequenceInfo(ReplicaInfo replica, int startX, int startY, int width, int height)
	{
		if (height < 1) return;

		// Line 1: Replica name and status with ASCII icon
		var statusIcon = GetStatusIcon(replica.Status);
		var statusColor = GetStatusColor(replica.Status);
		var statusText = GetStatusText(replica.Status);

		var line1 = $"{statusIcon} {replica.Name}: {statusText}";
		if (line1.Length > width)
			line1 = line1[..(width - 3)] + "...";

		consoleManager.WriteAt(startX, startY, line1, statusColor);

		if (height < 2) return;

		// Line 2: Lag information with visual indicator
		if (replica.Status == ConnectionStatus.Error && !string.IsNullOrEmpty(replica.ErrorMessage))
		{
			var errorMsg = $"X Error: {replica.ErrorMessage}";
			if (errorMsg.Length > width)
				errorMsg = errorMsg[..(width - 3)] + "...";

			consoleManager.WriteAt(startX, startY + 1, errorMsg, ConsoleColor.Red);
		}
		else if (replica.Status == ConnectionStatus.Connected)
		{
			var lagInfo = GetLagDisplayText(replica);
			var lagColor = GetLagColor(replica.TimeLag);
			consoleManager.WriteAt(startX, startY + 1, lagInfo, lagColor);
		}
		else
		{
			consoleManager.WriteAt(startX, startY + 1, "~ Checking...", ConsoleColor.Gray);
		}

		if (height < 3) return;

		// Line 3: Progress bar showing lag severity
		if (replica.Status == ConnectionStatus.Connected && replica.TimeLag.HasValue)
		{
			var (Bar, Color) = CreateLagProgressBar(replica.TimeLag.Value, width - 2);
			consoleManager.WriteAt(startX, startY + 2, Bar, Color);
		}

		if (height < 4) return;

		// Line 4: Record and sequence lag information
		if (replica.Status == ConnectionStatus.Connected)
		{
			var lagDetails = GetDetailedLagInfo(replica);
			if (lagDetails.Length > width)
				lagDetails = lagDetails[..(width - 3)] + "...";

			var lagColor = GetSequenceLagColor(replica);
			consoleManager.WriteAt(startX, startY + 3, lagDetails, lagColor);
		}

		if (height < 5) return;

		// Line 5: Missing sequence information
		if (replica.Status == ConnectionStatus.Connected && replica.MissingSequenceCount != 0)
		{
			var missingInfo = GetMissingSequenceInfo(replica);
			if (missingInfo.Length > width)
				missingInfo = missingInfo[..(width - 3)] + "...";

			var missingColor = replica.MissingSequenceCount > 0 ? ConsoleColor.Red : ConsoleColor.Green;
			consoleManager.WriteAt(startX, startY + 4, missingInfo, missingColor);
		}
		else if (replica.Status == ConnectionStatus.Connected)
		{
			consoleManager.WriteAt(startX, startY + 4, "# No missing sequences", ConsoleColor.Green);
		}

		if (height < 6) return;

		// Line 6: Last checked time
		if (replica.LastChecked.HasValue)
		{
			var timeSince = DateTime.Now - replica.LastChecked.Value;
			var lastChecked = timeSince.TotalSeconds < 60
				? $"@ {timeSince.TotalSeconds:F0}s ago"
				: $"@ {replica.LastChecked.Value:HH:mm:ss}";

			var timeColor = timeSince.TotalMinutes > 2 ? ConsoleColor.Red : ConsoleColor.DarkGray;
			consoleManager.WriteAt(startX, startY + 5, lastChecked, timeColor);
		}
	}

	private static string GetDetailedLagInfo(ReplicaInfo replica)
	{
		var parts = new List<string>();

		if (replica.RecordLag.HasValue && replica.RecordLag > 0)
		{
			parts.Add($"{replica.RecordLag} records");
		}

		if (replica.SequenceLag.HasValue && replica.SequenceLag > 0)
		{
			parts.Add($"{replica.SequenceLag} seq");
		}

		if (parts.Count == 0)
		{
			return "= Up to date";
		}

		return $"Behind: {string.Join(", ", parts)}";
	}

	private static string GetMissingSequenceInfo(ReplicaInfo replica)
	{
		if (replica.MissingSequenceCount == -1)
		{
			return "# Sequence check failed";
		}

		if (replica.MissingSequenceCount == 0)
		{
			return "# No missing sequences";
		}

		if (replica.MissingSequences.Count > 0)
		{
			var sequences = string.Join(",", replica.MissingSequences.Take(5));
			if (replica.MissingSequenceCount > 5)
			{
				sequences += "...";
			}

			return $"# Missing: {sequences} ({replica.MissingSequenceCount} total)";
		}

		return $"# {replica.MissingSequenceCount} missing sequences";
	}

	private static ConsoleColor GetSequenceLagColor(ReplicaInfo replica)
	{
		if (replica.MissingSequenceCount > 0)
			return ConsoleColor.Red;

		if (replica.SequenceLag.HasValue && replica.SequenceLag > 10)
			return ConsoleColor.Yellow;

		if (replica.RecordLag.HasValue && replica.RecordLag > 10)
			return ConsoleColor.Yellow;

		return ConsoleColor.Green;
	}

	private static string GetStatusIcon(ConnectionStatus status) =>
		// Use ASCII characters that work reliably across all platforms
		status switch
		{
			ConnectionStatus.Connected => "+",
			ConnectionStatus.Disconnected => "?",
			ConnectionStatus.Error => "!",
			_ => "-"
		};

	private static ConsoleColor GetStatusColor(ConnectionStatus status) => status switch
	{
		ConnectionStatus.Connected => ConsoleColor.Green,
		ConnectionStatus.Disconnected => ConsoleColor.Yellow,
		ConnectionStatus.Error => ConsoleColor.Red,
		_ => ConsoleColor.Gray
	};

	private static string GetStatusText(ConnectionStatus status) => status switch
	{
		ConnectionStatus.Connected => "ONLINE",
		ConnectionStatus.Disconnected => "OFFLINE",
		ConnectionStatus.Error => "ERROR",
		_ => "UNKNOWN"
	};

	private static string GetLagDisplayText(ReplicaInfo replica)
	{
		var parts = new List<string>();

		if (replica.TimeLag.HasValue)
		{
			if (replica.TimeLag.Value.TotalMilliseconds < 1000)
				parts.Add($"* {replica.TimeLag.Value.TotalMilliseconds:F0}ms");
			else if (replica.TimeLag.Value.TotalSeconds < 60)
				parts.Add($"^ {replica.TimeLag.Value.TotalSeconds:F1}s");
			else
				parts.Add($">> {replica.TimeLag.Value.TotalMinutes:F1}m");
		}

		return parts.Count > 0 ? string.Join(", ", parts) : "~ Calculating...";
	}

	private (string Bar, ConsoleColor Color) CreateLagProgressBar(TimeSpan timeLag, int width)
	{
		if (width < 10) return ("", ConsoleColor.Gray);

		var lagMs = timeLag.TotalMilliseconds;

		// Define thresholds: 0-500ms (excellent), 500ms-2s (good), 2s-10s (warning), 10s+ (critical)
		var maxScale = 10000; // 10 seconds max scale
		var percentage = Math.Min(lagMs / maxScale, 1.0);

		var barWidth = width - 8; // Leave space for brackets and percentage
		var filledBlocks = (int)(percentage * barWidth);

		var bar = new System.Text.StringBuilder();
		bar.Append("LAG [");

		// Create the progress bar using ASCII characters
		for (int i = 0; i < barWidth; i++)
		{
			if (i < filledBlocks)
			{
				if (lagMs < 500) bar.Append('='); // Excellent - solid block
				else if (lagMs < 2000) bar.Append('#'); // Good - hash
				else if (lagMs < 10000) bar.Append('-'); // Warning - dash
				else bar.Append('.'); // Critical - dot
			}
			else
			{
				bar.Append(' ');
			}
		}

		bar.Append(']');

		// Add status
		if (lagMs < 500)
			bar.Append(" OK");
		else if (lagMs < 2000)
			bar.Append(" GOOD");
		else if (lagMs < 10000)
			bar.Append(" WARN");
		else
			bar.Append(" CRIT");

		var color = GetLagColor(timeLag);
		return (bar.ToString(), color);
	}

	private static ConsoleColor GetLagColor(TimeSpan? timeLag)
	{
		if (!timeLag.HasValue)
			return ConsoleColor.Gray;

		var totalMs = timeLag.Value.TotalMilliseconds;

		if (totalMs < 500) // Less than 500ms - excellent
			return ConsoleColor.Green;
		else if (totalMs < 2000) // Less than 2s - good
			return ConsoleColor.Yellow;
		else if (totalMs < 10000) // Less than 10s - concerning
			return ConsoleColor.Red;
		else // More than 10s - critical
			return ConsoleColor.Magenta;
	}
}