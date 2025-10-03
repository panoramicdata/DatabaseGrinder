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

            // Draw header
            var header = "REPLICATION MONITOR";
            var headerX = paneStartX + (paneWidth - header.Length) / 2;
            _consoleManager.WriteAt(headerX, 0, header, ConsoleColor.White, ConsoleColor.DarkBlue);

            // Draw separator line
            var separator = new string('─', paneWidth);
            _consoleManager.WriteAt(paneStartX, 1, separator, ConsoleColor.DarkGray);

            // Calculate space for each replica
            var availableHeight = paneHeight - 2; // Minus header and separator
            var linesPerReplica = _replicas.Count > 0 ? Math.Max(3, availableHeight / _replicas.Count) : availableHeight;

            // Draw replica information
            var currentY = 2;
            for (int i = 0; i < _replicas.Count && currentY < paneHeight; i++)
            {
                var replica = _replicas[i];
                var replicaEndY = Math.Min(currentY + linesPerReplica, paneHeight);
                
                DrawReplica(replica, paneStartX, currentY, paneWidth, replicaEndY - currentY);
                
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

    private void DrawReplica(ReplicaInfo replica, int startX, int startY, int width, int height)
    {
        if (height < 1) return;

        // Line 1: Replica name and status
        var statusColor = GetStatusColor(replica.Status);
        var statusText = GetStatusText(replica.Status);
        
        var line1 = $"{replica.Name}: {statusText}";
        if (line1.Length > width)
            line1 = line1.Substring(0, width - 3) + "...";
        
        _consoleManager.WriteAt(startX, startY, line1, statusColor);

        if (height < 2) return;

        // Line 2: Lag information or error
        if (replica.Status == ConnectionStatus.Error && !string.IsNullOrEmpty(replica.ErrorMessage))
        {
            var errorMsg = $"Error: {replica.ErrorMessage}";
            if (errorMsg.Length > width)
                errorMsg = errorMsg.Substring(0, width - 3) + "...";
            
            _consoleManager.WriteAt(startX, startY + 1, errorMsg, ConsoleColor.Red);
        }
        else if (replica.Status == ConnectionStatus.Connected)
        {
            var lagInfo = GetLagDisplayText(replica);
            _consoleManager.WriteAt(startX, startY + 1, lagInfo, GetLagColor(replica.TimeLag));
        }

        if (height < 3) return;

        // Line 3: Last checked time
        if (replica.LastChecked.HasValue)
        {
            var lastChecked = $"Last: {replica.LastChecked.Value:HH:mm:ss}";
            _consoleManager.WriteAt(startX, startY + 2, lastChecked, ConsoleColor.DarkGray);
        }
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
                parts.Add($"{replica.TimeLag.Value.TotalMilliseconds:F0}ms");
            else
                parts.Add($"{replica.TimeLag.Value.TotalSeconds:F1}s");
        }

        if (replica.RecordLag.HasValue)
        {
            parts.Add($"{replica.RecordLag.Value} records");
        }

        return parts.Count > 0 ? $"Lag: {string.Join(", ", parts)}" : "Lag: Unknown";
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