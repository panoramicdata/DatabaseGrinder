using DatabaseGrinder.Services;
using Microsoft.Extensions.Logging;

namespace DatabaseGrinder.UI;

/// <summary>
/// Connection status for replica monitoring
/// </summary>
public enum ConnectionStatus
{
    Connected,
    Disconnected,
    Error,
    Unknown
}

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
    public List<long> MissingSequences { get; set; } = new();
    public DateTime? LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Manages the right pane display showing replication status
/// </summary>
public class RightPane
{
    private readonly ConsoleManager _consoleManager;
    private readonly ILogger<RightPane> _logger;
    private readonly List<ReplicaInfo> _replicas = new();
    private readonly object _lockObject = new object();

    /// <summary>
    /// Initializes a new instance of RightPane
    /// </summary>
    /// <param name="consoleManager">Console manager for display operations</param>
    /// <param name="logger">Logger instance</param>
    public RightPane(ConsoleManager consoleManager, ILogger<RightPane> logger)
    {
        _consoleManager = consoleManager;
        _logger = logger;
    }

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
            var paneWidth = _consoleManager.RightPaneWidth;
            var contentHeight = _consoleManager.ContentHeight;
            var paneStartX = _consoleManager.RightPaneStartX;
            var startY = _consoleManager.ContentStartY;

            // Clear right pane content area only
            for (int y = startY; y < _consoleManager.Height; y++)
            {
                var clearLine = new string(' ', paneWidth);
                _consoleManager.WriteAt(paneStartX, y, clearLine);
            }

            // Draw header with overall status
            var header = "REPLICATION MONITOR";
            var headerX = paneStartX + (paneWidth - header.Length) / 2;
            _consoleManager.WriteAt(headerX, startY, header, ConsoleColor.White, ConsoleColor.DarkBlue);

            // Draw overall status summary
            var statusSummary = GetOverallStatusSummary();
            var summaryX = paneStartX + (paneWidth - statusSummary.Text.Length) / 2;
            _consoleManager.WriteAt(summaryX, startY + 1, statusSummary.Text, statusSummary.Color);

            // Draw separator line using proper line drawing character
            var separatorY = startY + 2;
            var separator = new string(_consoleManager.HorizontalLineChar, paneWidth);
            _consoleManager.WriteAt(paneStartX, separatorY, separator, ConsoleColor.DarkGray);

            // Calculate space for each replica
            var availableHeight = contentHeight - 3; // Minus header, status, and separator
            var linesPerReplica = _replicas.Count > 0 ? Math.Max(6, availableHeight / _replicas.Count) : availableHeight;

            // Draw replica information
            var currentY = separatorY + 1;
            for (int i = 0; i < _replicas.Count && currentY < _consoleManager.Height; i++)
            {
                var replica = _replicas[i];
                var replicaEndY = Math.Min(currentY + linesPerReplica, _consoleManager.Height);
                
                DrawReplicaWithSequenceInfo(replica, paneStartX, currentY, paneWidth, replicaEndY - currentY);
                
                currentY = replicaEndY;
                
                // Draw separator between replicas if not the last one
                if (i < _replicas.Count - 1 && currentY < _consoleManager.Height)
                {
                    // Use proper dotted line character if available
                    var repSeparator = new string('·', paneWidth);
                    _consoleManager.WriteAt(paneStartX, currentY, repSeparator, ConsoleColor.DarkGray);
                    currentY++;
                }
            }

            // Show message if no replicas configured
            if (_replicas.Count == 0)
            {
                var noReplicasMsg = "No replicas configured";
                var msgX = paneStartX + (paneWidth - noReplicasMsg.Length) / 2;
                var msgY = startY + (contentHeight / 2);
                _consoleManager.WriteAt(msgX, msgY, noReplicasMsg, ConsoleColor.Yellow);
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
            line1 = line1.Substring(0, width - 3) + "...";
        
        _consoleManager.WriteAt(startX, startY, line1, statusColor);

        if (height < 2) return;

        // Line 2: Lag information with visual indicator
        if (replica.Status == ConnectionStatus.Error && !string.IsNullOrEmpty(replica.ErrorMessage))
        {
            var errorMsg = $"X Error: {replica.ErrorMessage}";
            if (errorMsg.Length > width)
                errorMsg = errorMsg.Substring(0, width - 3) + "...";
            
            _consoleManager.WriteAt(startX, startY + 1, errorMsg, ConsoleColor.Red);
        }
        else if (replica.Status == ConnectionStatus.Connected)
        {
            var lagInfo = GetLagDisplayText(replica);
            var lagColor = GetLagColor(replica.TimeLag);
            _consoleManager.WriteAt(startX, startY + 1, lagInfo, lagColor);
        }
        else
        {
            _consoleManager.WriteAt(startX, startY + 1, "~ Checking...", ConsoleColor.Gray);
        }

        if (height < 3) return;

        // Line 3: Progress bar showing lag severity
        if (replica.Status == ConnectionStatus.Connected && replica.TimeLag.HasValue)
        {
            var progressBar = CreateLagProgressBar(replica.TimeLag.Value, width - 2);
            _consoleManager.WriteAt(startX, startY + 2, progressBar.Bar, progressBar.Color);
        }

        if (height < 4) return;

        // Line 4: Record and sequence lag information
        if (replica.Status == ConnectionStatus.Connected)
        {
            var lagDetails = GetDetailedLagInfo(replica);
            if (lagDetails.Length > width)
                lagDetails = lagDetails.Substring(0, width - 3) + "...";
            
            var lagColor = GetSequenceLagColor(replica);
            _consoleManager.WriteAt(startX, startY + 3, lagDetails, lagColor);
        }

        if (height < 5) return;

        // Line 5: Missing sequence information
        if (replica.Status == ConnectionStatus.Connected && replica.MissingSequenceCount != 0)
        {
            var missingInfo = GetMissingSequenceInfo(replica);
            if (missingInfo.Length > width)
                missingInfo = missingInfo.Substring(0, width - 3) + "...";
            
            var missingColor = replica.MissingSequenceCount > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            _consoleManager.WriteAt(startX, startY + 4, missingInfo, missingColor);
        }
        else if (replica.Status == ConnectionStatus.Connected)
        {
            _consoleManager.WriteAt(startX, startY + 4, "# No missing sequences", ConsoleColor.Green);
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
            _consoleManager.WriteAt(startX, startY + 5, lastChecked, timeColor);
        }
    }

    private string GetDetailedLagInfo(ReplicaInfo replica)
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

    private string GetMissingSequenceInfo(ReplicaInfo replica)
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

    private ConsoleColor GetSequenceLagColor(ReplicaInfo replica)
    {
        if (replica.MissingSequenceCount > 0)
            return ConsoleColor.Red;
        
        if (replica.SequenceLag.HasValue && replica.SequenceLag > 10)
            return ConsoleColor.Yellow;
        
        if (replica.RecordLag.HasValue && replica.RecordLag > 10)
            return ConsoleColor.Yellow;
        
        return ConsoleColor.Green;
    }

    private string GetStatusIcon(ConnectionStatus status)
    {
        // Use ASCII characters that work reliably across all platforms
        return status switch
        {
            ConnectionStatus.Connected => "+",
            ConnectionStatus.Disconnected => "?",
            ConnectionStatus.Error => "!",
            _ => "-"
        };
    }

    private ConsoleColor GetStatusColor(ConnectionStatus status)
    {
        return status switch
        {
            ConnectionStatus.Connected => ConsoleColor.Green,
            ConnectionStatus.Disconnected => ConsoleColor.Yellow,
            ConnectionStatus.Error => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }

    private string GetStatusText(ConnectionStatus status)
    {
        return status switch
        {
            ConnectionStatus.Connected => "ONLINE",
            ConnectionStatus.Disconnected => "OFFLINE",
            ConnectionStatus.Error => "ERROR",
            _ => "UNKNOWN"
        };
    }

    private string GetLagDisplayText(ReplicaInfo replica)
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

    private ConsoleColor GetLagColor(TimeSpan? timeLag)
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