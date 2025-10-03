using DatabaseGrinder.Configuration;
using DatabaseGrinder.Data;
using DatabaseGrinder.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

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
	public long? LatestReplicaSequenceNumber { get; set; }
	public TimeSpan? TimeLag { get; set; }
	public long? RecordLag { get; set; }
	public long? SequenceLag { get; set; }
	public int MissingSequenceCount { get; set; }
	public int PreviousMissingCount { get; set; }
	public List<long> MissingSequences { get; set; } = [];
	public string? ErrorMessage { get; set; }
	public int ConsecutiveErrors { get; set; }
	public TimeSpan ResponseTime { get; set; }
}

/// <summary>
/// Background service that monitors replication lag across multiple replica databases
/// </summary>
public class ReplicationMonitor(
	IServiceProvider serviceProvider,
	ILogger<ReplicationMonitor> logger,
	IOptions<DatabaseGrinderSettings> settings,
	RightPane rightPane,
	LeftPane leftPane) : BackgroundService
{
	private readonly IServiceProvider _serviceProvider = serviceProvider;
	private readonly ILogger<ReplicationMonitor> _logger = logger;
	private readonly DatabaseGrinderSettings _settings = settings.Value;
	private readonly RightPane _rightPane = rightPane;
	private readonly LeftPane _leftPane = leftPane;
	private readonly ConcurrentDictionary<string, ReplicaStatistics> _replicaStats = new();
	private readonly ConcurrentDictionary<string, Task> _monitoringTasks = new();

	/// <summary>
	/// Get current replica statistics
	/// </summary>
	public IReadOnlyDictionary<string, ReplicaStatistics> Statistics => _replicaStats.AsReadOnly();

	/// <summary>
	/// Main execution loop for the replication monitor
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("ReplicationMonitor started - monitoring {ReplicaCount} replicas with sequence gap detection",
			_settings.ReplicaConnections.Count);
		_leftPane.AddLogEntry("Replication monitor started with sequence tracking");

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

		_leftPane.AddLogEntry($"Started monitoring {_settings.ReplicaConnections.Count} replica(s)");

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
		_leftPane.AddLogEntry("Replication monitor stopped");
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
		_leftPane.AddLogEntry($"Starting monitor for {replica.Name}");

		while (!stoppingToken.IsCancellationRequested)
		{
			var startTime = DateTime.Now;
			stats.LastAttemptedCheck = startTime;

			try
			{
				// Get the latest record from the primary database
				var (latestPrimaryId, latestPrimaryTimestamp, latestPrimarySequence) = await GetLatestPrimaryRecordAsync(stoppingToken);

				if (latestPrimaryId.HasValue && latestPrimaryTimestamp.HasValue && latestPrimarySequence.HasValue)
				{
					// Check the replica for lag and missing sequences
					await CheckReplicaLagAndMissingSequencesAsync(replica, stats, latestPrimaryId.Value, latestPrimaryTimestamp.Value, latestPrimarySequence.Value, stoppingToken);
				}
				else
				{
					// No records in primary yet
					stats.Status = ConnectionStatus.Connected;
					stats.TimeLag = null;
					stats.RecordLag = null;
					stats.SequenceLag = null;
					stats.MissingSequenceCount = 0;
					stats.MissingSequences.Clear();
					stats.ErrorMessage = "No records in primary database yet";
				}

				// Update UI
				UpdateReplicaDisplay(stats);

				// Reset error count on success
				if (stats.ConsecutiveErrors > 0)
				{
					_leftPane.AddLogEntry($"{replica.Name} connection restored");
				}

				stats.ConsecutiveErrors = 0;
				stats.LastSuccessfulCheck = DateTime.Now;
				stats.ResponseTime = DateTime.Now - startTime;

				// Wait for next check
				var elapsed = DateTime.Now - startTime;
				var remainingTime = checkInterval - elapsed;
				if (remainingTime > TimeSpan.Zero)
				{
					try
					{
						await Task.Delay(remainingTime, stoppingToken);
					}
					catch (OperationCanceledException)
					{
						// Expected when stopping
						break;
					}
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

				// Only log to UI on first error or every 10th consecutive error to avoid spam
				if (stats.ConsecutiveErrors == 1 || stats.ConsecutiveErrors % 10 == 0)
				{
					_leftPane.AddLogEntry($"{replica.Name} error (attempt {stats.ConsecutiveErrors}): {ex.Message}");
				}

				// Update UI with error
				UpdateReplicaDisplay(stats);

				// Progressive backoff on consecutive errors
				var retryDelay = TimeSpan.FromSeconds(Math.Min(5 * stats.ConsecutiveErrors, maxRetryDelay.TotalSeconds));
				try
				{
					await Task.Delay(retryDelay, stoppingToken);
				}
				catch (OperationCanceledException)
				{
					// Expected when stopping
					break;
				}
			}
		}

		_logger.LogInformation("Monitor stopped for replica: {ReplicaName}", replica.Name);
		_leftPane.AddLogEntry($"Monitor stopped for {replica.Name}");
	}

	/// <summary>
	/// Get the latest record from the primary database
	/// </summary>
	private async Task<(long? Id, DateTime? Timestamp, long? SequenceNumber)> GetLatestPrimaryRecordAsync(CancellationToken cancellationToken)
	{
		using var scope = _serviceProvider.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

		var latestRecord = await context.TestRecords
			.OrderByDescending(r => r.Id)
			.FirstOrDefaultAsync(cancellationToken);

		return latestRecord != null ? (latestRecord.Id, latestRecord.Timestamp, latestRecord.SequenceNumber) : (null, null, null);
	}

	/// <summary>
	/// Check replica lag and detect missing sequence numbers
	/// </summary>
	private async Task CheckReplicaLagAndMissingSequencesAsync(
		ReplicaConnectionSettings replica,
		ReplicaStatistics stats,
		long primaryRecordId,
		DateTime primaryTimestamp,
		long primarySequenceNumber,
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
			stats.LatestReplicaSequenceNumber = null;
			stats.RecordLag = primaryRecordId;
			stats.SequenceLag = primarySequenceNumber;
			stats.TimeLag = DateTime.UtcNow - primaryTimestamp;
			stats.MissingSequenceCount = 0;
			stats.MissingSequences.Clear();
			stats.ErrorMessage = null;
		}
		else
		{
			// Calculate lag metrics
			stats.Status = ConnectionStatus.Connected;
			stats.LatestReplicaRecordId = latestReplicaRecord.Id;
			stats.LatestReplicaTimestamp = latestReplicaRecord.Timestamp;
			stats.LatestReplicaSequenceNumber = latestReplicaRecord.SequenceNumber;
			stats.RecordLag = Math.Max(0, primaryRecordId - latestReplicaRecord.Id);
			stats.SequenceLag = Math.Max(0, primarySequenceNumber - latestReplicaRecord.SequenceNumber);

			// Calculate time lag using the latest replica record's timestamp
			stats.TimeLag = DateTime.UtcNow - latestReplicaRecord.Timestamp;

			// Check for missing sequence numbers (only check recent sequences to avoid performance issues)
			await CheckForMissingSequencesAsync(replicaContext, stats, primarySequenceNumber, cancellationToken);

			stats.ErrorMessage = null;

			_logger.LogDebug("Replica {ReplicaName}: Latest ID {ReplicaId} vs Primary {PrimaryId}, Seq {ReplicaSeq} vs {PrimarySeq}, Time lag: {TimeLag}ms, Missing: {MissingCount}",
				replica.Name, latestReplicaRecord.Id, primaryRecordId, latestReplicaRecord.SequenceNumber, primarySequenceNumber, stats.TimeLag?.TotalMilliseconds, stats.MissingSequenceCount);

			// Log significant missing sequences to UI (only when count changes)
			if (stats.MissingSequenceCount > 0 && stats.MissingSequenceCount != stats.PreviousMissingCount)
			{
				_leftPane.AddLogEntry($"{replica.Name} has {stats.MissingSequenceCount} missing sequences");
				stats.PreviousMissingCount = stats.MissingSequenceCount;
			}
			else if (stats.MissingSequenceCount == 0 && stats.PreviousMissingCount > 0)
			{
				_leftPane.AddLogEntry($"{replica.Name} missing sequences resolved");
				stats.PreviousMissingCount = 0;
			}
		}
	}

	/// <summary>
	/// Check for missing sequence numbers in the replica
	/// </summary>
	private async Task CheckForMissingSequencesAsync(DatabaseContext replicaContext, ReplicaStatistics stats, long primarySequenceNumber, CancellationToken cancellationToken)
	{
		try
		{
			// Only check the last 100 sequence numbers to avoid performance issues
			var startSequence = Math.Max(1, primarySequenceNumber - 100);
			var endSequence = primarySequenceNumber;

			// Get all sequence numbers in the range from the replica
			var existingSequences = await replicaContext.TestRecords
				.Where(r => r.SequenceNumber >= startSequence && r.SequenceNumber <= endSequence)
				.Select(r => r.SequenceNumber)
				.ToListAsync(cancellationToken);

			// Find missing sequences
			var missingSequences = new List<long>();
			for (long seq = startSequence; seq <= endSequence; seq++)
			{
				if (!existingSequences.Contains(seq))
				{
					missingSequences.Add(seq);
				}
			}

			stats.MissingSequences = [.. missingSequences.Take(10)]; // Keep only first 10 for display
			stats.MissingSequenceCount = missingSequences.Count;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to check for missing sequences in replica {ReplicaName}", stats.Name);
			stats.MissingSequenceCount = -1; // Indicate error in checking
			stats.MissingSequences.Clear();
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
			SequenceLag = stats.SequenceLag,
			MissingSequenceCount = stats.MissingSequenceCount,
			MissingSequences = stats.MissingSequences,
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
