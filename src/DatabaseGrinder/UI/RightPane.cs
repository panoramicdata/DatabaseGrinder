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
            var paneHeight = _consoleManager.Height;
            var paneStartX = _consoleManager.LeftPaneWidth + 1; // +1 for separator

            // Clear right pane
            for (int y = 0; y < paneHeight; y++)
            {
                var clearLine = new string(' ', paneWidth);
                _consoleManager.WriteAt(paneStartX, y, clearLine);
            }

            // Draw header with overall status
            var header = "REPLICATION MONITOR";
            var headerX = paneStartX + (paneWidth - header.Length) / 2;
            _consoleManager.WriteAt(headerX, 0, header, ConsoleColor.White, ConsoleColor.DarkBlue);

            // Draw overall status summary
            var statusSummary = GetOverallStatusSummary();
            var summaryX = paneStartX + (paneWidth - statusSummary.Text.Length) / 2;
            _consoleManager.WriteAt(summaryX, 1, statusSummary.Text, statusSummary.Color);

            // Draw separator line
            var separator = new string('─', paneWidth);
            _consoleManager.WriteAt(paneStartX, 2, separator, ConsoleColor.DarkGray);

            // Calculate space for each replica (now we need more space per replica)
            var availableHeight = paneHeight - 3; // Minus header, status, and separator
            var linesPerReplica = _replicas.Count > 0 ? Math.Max(5, availableHeight / _replicas.Count) : availableHeight;

            // Draw replica information
            var currentY = 3;
            for (int i = 0; i < _replicas.Count && currentY < paneHeight; i++)
            {
                var replica = _replicas[i];
                var replicaEndY = Math.Min(currentY + linesPerReplica, paneHeight);
                
                DrawReplicaWithProgressIndicators(replica, paneStartX, currentY, paneWidth, replicaEndY - currentY);
                
                currentY = replicaEndY;
                
                // Draw separator between replicas if not the last one
                if (i < _replicas.Count - 1 && currentY < paneHeight)
                {
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
                _consoleManager.WriteAt(msgX, paneHeight / 2, noReplicasMsg, ConsoleColor.Yellow);
            }
        }
    }

    private (string Text, ConsoleColor Color) GetOverallStatusSummary()
    {
        var connected = _replicas.Count(r => r.Status == ConnectionStatus.Connected);
        var total = _replicas.Count;
        
        if (total == 0)
            return ("No replicas", ConsoleColor.Gray);
        
        if (connected == total)
        {
            var maxLag = _replicas.Where(r => r.TimeLag.HasValue).Max(r => r.TimeLag?.TotalMilliseconds ?? 0);
            if (maxLag < 500)
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

    private void DrawReplicaWithProgressIndicators(ReplicaInfo replica, int startX, int startY, int width, int height)
    {
        if (height < 1) return;

        // Line 1: Replica name and status with icon
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
            var errorMsg = $"✖ Error: {replica.ErrorMessage}";
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
            _consoleManager.WriteAt(startX, startY + 1, "⏳ Checking...", ConsoleColor.Gray);
        }

        if (height < 3) return;

        // Line 3: Progress bar showing lag severity
        if (replica.Status == ConnectionStatus.Connected && replica.TimeLag.HasValue)
        {
            var progressBar = CreateLagProgressBar(replica.TimeLag.Value, width - 2);
            _consoleManager.WriteAt(startX, startY + 2, progressBar.Bar, progressBar.Color);
        }

        if (height < 4) return;

        // Line 4: Record lag information (if available)
        if (replica.RecordLag.HasValue && replica.RecordLag > 0)
        {
            var recordInfo = $"📊 {replica.RecordLag} records behind";
            if (recordInfo.Length > width)
                recordInfo = recordInfo.Substring(0, width - 3) + "...";
            
            var recordColor = replica.RecordLag > 100 ? ConsoleColor.Red : 
                             replica.RecordLag > 10 ? ConsoleColor.Yellow : ConsoleColor.Green;
            _consoleManager.WriteAt(startX, startY + 3, recordInfo, recordColor);
        }
        else if (replica.Status == ConnectionStatus.Connected)
        {
            _consoleManager.WriteAt(startX, startY + 3, "📊 Up to date", ConsoleColor.Green);
        }

        if (height < 5) return;

        // Line 5: Last checked time
        if (replica.LastChecked.HasValue)
        {
            var timeSince = DateTime.Now - replica.LastChecked.Value;
            var lastChecked = timeSince.TotalSeconds < 60 
                ? $"🕐 {timeSince.TotalSeconds:F0}s ago"
                : $"🕐 {replica.LastChecked.Value:HH:mm:ss}";
            
            var timeColor = timeSince.TotalMinutes > 2 ? ConsoleColor.Red : ConsoleColor.DarkGray;
            _consoleManager.WriteAt(startX, startY + 4, lastChecked, timeColor);
        }
    }

    private string GetStatusIcon(ConnectionStatus status)
    {
        return status switch
        {
            ConnectionStatus.Connected => "🟢",
            ConnectionStatus.Disconnected => "🟡",
            ConnectionStatus.Error => "🔴",
            _ => "⚪"
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
                parts.Add($"⚡ {replica.TimeLag.Value.TotalMilliseconds:F0}ms");
            else if (replica.TimeLag.Value.TotalSeconds < 60)
                parts.Add($"⏱️ {replica.TimeLag.Value.TotalSeconds:F1}s");
            else
                parts.Add($"⏰ {replica.TimeLag.Value.TotalMinutes:F1}m");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "⏳ Calculating...";
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
        
        // Create the progress bar
        for (int i = 0; i < barWidth; i++)
        {
            if (i < filledBlocks)
            {
                if (lagMs < 500) bar.Append('█'); // Excellent - solid block
                else if (lagMs < 2000) bar.Append('▓'); // Good - medium block  
                else if (lagMs < 10000) bar.Append('▒'); // Warning - light block
                else bar.Append('░'); // Critical - very light block
            }
            else
            {
                bar.Append('·');
            }
        }
        
        bar.Append(']');
        
        // Add percentage
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