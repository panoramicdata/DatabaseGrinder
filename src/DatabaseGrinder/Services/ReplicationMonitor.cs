using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using DatabaseGrinder.Configuration;
using DatabaseGrinder.Data;
using DatabaseGrinder.Models;
using DatabaseGrinder.UI;
using Npgsql;

namespace DatabaseGrinder.Services;

/// <summary>
/// Replica monitoring statistics
/// </summary>
public class ReplicaStatistics
{
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Unknown;
    public DateTime? LastSuccessfulCheck { get; set; }
    public DateTime? LastAttemptedCheck { get; set; }
    public long? LatestReplicaRecordId { get; set; }
    public DateTime? LatestReplicaTimestamp { get; set; }
    public TimeSpan? TimeLag { get; set; }
    public long? RecordLag { get; set; }
    public string? ErrorMessage { get; set; }
    public int ConsecutiveErrors { get; set; }
    public TimeSpan ResponseTime { get; set; }
}

/// <summary>
/// Background service that monitors replication lag across multiple replica databases
/// </summary>
public class ReplicationMonitor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReplicationMonitor> _logger;
    private readonly DatabaseGrinderSettings _settings;
    private readonly RightPane _rightPane;
    private readonly ConcurrentDictionary<string, ReplicaStatistics> _replicaStats = new();
    private readonly ConcurrentDictionary<string, Task> _monitoringTasks = new();

    public ReplicationMonitor(
        IServiceProvider serviceProvider,
        ILogger<ReplicationMonitor> logger,
        IOptions<DatabaseGrinderSettings> settings,
        RightPane rightPane)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _rightPane = rightPane;
    }

    /// <summary>
    /// Get current replica statistics
    /// </summary>
    public IReadOnlyDictionary<string, ReplicaStatistics> Statistics => _replicaStats.AsReadOnly();

    /// <summary>
    /// Main execution loop for the replication monitor
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReplicationMonitor started - monitoring {ReplicaCount} replicas", 
            _settings.ReplicaConnections.Count);

        // Initialize replica statistics
        foreach (var replica in _settings.ReplicaConnections)
        {
            _replicaStats[replica.Name] = new ReplicaStatistics
            {
                Name = replica.Name,
                ConnectionString = replica.ConnectionString,
                Status = ConnectionStatus.Unknown
            };
        }

        // Start individual monitoring tasks for each replica
        foreach (var replica in _settings.ReplicaConnections)
        {
            var monitoringTask = MonitorReplicaAsync(replica, stoppingToken);
            _monitoringTasks[replica.Name] = monitoringTask;
        }

        // Wait for all monitoring tasks to complete
        try
        {
            await Task.WhenAll(_monitoringTasks.Values);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }

        _logger.LogInformation("ReplicationMonitor stopped");
    }

    /// <summary>
    /// Monitor a single replica database continuously
    /// </summary>
    private async Task MonitorReplicaAsync(ReplicaConnectionSettings replica, CancellationToken stoppingToken)
    {
        var stats = _replicaStats[replica.Name];
        var checkInterval = TimeSpan.FromMilliseconds(_settings.Settings.UIRefreshIntervalMs);
        var maxRetryDelay = TimeSpan.FromSeconds(30);

        _logger.LogInformation("Starting monitor for replica: {ReplicaName}", replica.Name);

        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTime.Now;
            stats.LastAttemptedCheck = startTime;

            try
            {
                // Get the latest record from the primary database
                var (latestPrimaryId, latestPrimaryTimestamp) = await GetLatestPrimaryRecordAsync(stoppingToken);
                
                if (latestPrimaryId.HasValue && latestPrimaryTimestamp.HasValue)
                {
                    // Check the replica for the same record and calculate lag
                    await CheckReplicaLagAsync(replica, stats, latestPrimaryId.Value, latestPrimaryTimestamp.Value, stoppingToken);
                }
                else
                {
                    // No records in primary yet
                    stats.Status = ConnectionStatus.Connected;
                    stats.TimeLag = null;
                    stats.RecordLag = null;
                    stats.ErrorMessage = "No records in primary database yet";
                }

                // Update UI
                UpdateReplicaDisplay(stats);
                
                // Reset error count on success
                stats.ConsecutiveErrors = 0;
                stats.LastSuccessfulCheck = DateTime.Now;
                stats.ResponseTime = DateTime.Now - startTime;

                // Wait for next check
                var elapsed = DateTime.Now - startTime;
                var remainingTime = checkInterval - elapsed;
                if (remainingTime > TimeSpan.Zero)
                {
                    await Task.Delay(remainingTime, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                stats.ConsecutiveErrors++;
                stats.Status = ConnectionStatus.Error;
                stats.ErrorMessage = ex.Message;
                stats.ResponseTime = DateTime.Now - startTime;

                _logger.LogWarning(ex, "Failed to check replica {ReplicaName} (attempt {ErrorCount})", 
                    replica.Name, stats.ConsecutiveErrors);

                // Update UI with error
                UpdateReplicaDisplay(stats);

                // Progressive backoff on consecutive errors
                var retryDelay = TimeSpan.FromSeconds(Math.Min(5 * stats.ConsecutiveErrors, maxRetryDelay.TotalSeconds));
                await Task.Delay(retryDelay, stoppingToken);
            }
        }

        _logger.LogInformation("Monitor stopped for replica: {ReplicaName}", replica.Name);
    }

    /// <summary>
    /// Get the latest record from the primary database
    /// </summary>
    private async Task<(long? Id, DateTime? Timestamp)> GetLatestPrimaryRecordAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var latestRecord = await context.TestRecords
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return latestRecord != null ? (latestRecord.Id, latestRecord.Timestamp) : (null, null);
    }

    /// <summary>
    /// Check replica lag against a specific primary record
    /// </summary>
    private async Task CheckReplicaLagAsync(
        ReplicaConnectionSettings replica, 
        ReplicaStatistics stats, 
        long primaryRecordId, 
        DateTime primaryTimestamp,
        CancellationToken cancellationToken)
    {
        // Create a separate DbContext for this replica
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(replica.ConnectionString)
            .Options;

        using var replicaContext = new DatabaseContext(options);
        
        // Test connection first
        await replicaContext.Database.OpenConnectionAsync(cancellationToken);
        
        // Get the latest record from the replica
        var latestReplicaRecord = await replicaContext.TestRecords
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestReplicaRecord == null)
        {
            // Replica has no records yet
            stats.Status = ConnectionStatus.Connected;
            stats.LatestReplicaRecordId = null;
            stats.LatestReplicaTimestamp = null;
            stats.RecordLag = primaryRecordId;
            stats.TimeLag = DateTime.UtcNow - primaryTimestamp;
            stats.ErrorMessage = null;
        }
        else
        {
            // Calculate lag metrics
            stats.Status = ConnectionStatus.Connected;
            stats.LatestReplicaRecordId = latestReplicaRecord.Id;
            stats.LatestReplicaTimestamp = latestReplicaRecord.Timestamp;
            stats.RecordLag = Math.Max(0, primaryRecordId - latestReplicaRecord.Id);
            
            // Calculate time lag using the latest replica record's timestamp
            stats.TimeLag = DateTime.UtcNow - latestReplicaRecord.Timestamp;
            stats.ErrorMessage = null;

            _logger.LogDebug("Replica {ReplicaName}: Latest ID {ReplicaId} vs Primary {PrimaryId}, Time lag: {TimeLag}ms", 
                replica.Name, latestReplicaRecord.Id, primaryRecordId, stats.TimeLag?.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Update the right pane display with current replica statistics
    /// </summary>
    private void UpdateReplicaDisplay(ReplicaStatistics stats)
    {
        var replicaInfo = new ReplicaInfo
        {
            Name = stats.Name,
            ConnectionString = stats.ConnectionString,
            Status = stats.Status,
            TimeLag = stats.TimeLag,
            RecordLag = stats.RecordLag,
            LastChecked = stats.LastAttemptedCheck,
            ErrorMessage = stats.ErrorMessage
        };

        _rightPane.UpdateReplica(replicaInfo);
    }

    /// <summary>
    /// Get a summary of all replica statuses
    /// </summary>
    public ReplicationSummary GetReplicationSummary()
    {
        var summary = new ReplicationSummary();
        
        foreach (var stats in _replicaStats.Values)
        {
            summary.TotalReplicas++;
            
            switch (stats.Status)
            {
                case ConnectionStatus.Connected:
                    summary.ConnectedReplicas++;
                    if (stats.TimeLag.HasValue)
                    {
                        summary.MaxTimeLag = summary.MaxTimeLag.HasValue 
                            ? TimeSpan.FromMilliseconds(Math.Max(summary.MaxTimeLag.Value.TotalMilliseconds, stats.TimeLag.Value.TotalMilliseconds))
                            : stats.TimeLag.Value;
                        
                        summary.AverageTimeLag = summary.AverageTimeLag.HasValue
                            ? TimeSpan.FromMilliseconds((summary.AverageTimeLag.Value.TotalMilliseconds + stats.TimeLag.Value.TotalMilliseconds) / 2)
                            : stats.TimeLag.Value;
                    }
                    break;
                case ConnectionStatus.Error:
                    summary.ErrorReplicas++;
                    break;
                case ConnectionStatus.Disconnected:
                    summary.DisconnectedReplicas++;
                    break;
            }
        }
        
        return summary;
    }
}

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