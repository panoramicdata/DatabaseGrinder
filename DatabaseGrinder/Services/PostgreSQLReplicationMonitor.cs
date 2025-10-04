using DatabaseGrinder.Configuration;
using DatabaseGrinder.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DatabaseGrinder.Services;

/// <summary>
/// Background service that periodically queries PostgreSQL native replication statistics
/// </summary>
// ReSharper disable once InconsistentNaming
public class PostgreSQLReplicationMonitor(
	IServiceProvider serviceProvider,
	ILogger<PostgreSQLReplicationMonitor> logger,
	IOptions<DatabaseGrinderSettings> settings,
	ReplicationStatsPane replicationStatsPane,
	LeftPane leftPane) : BackgroundService
{
	private readonly DatabaseGrinderSettings _settings = settings.Value;

	/// <summary>
	/// Main execution loop for PostgreSQL replication monitoring
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("PostgreSQL Replication Monitor started - querying native replication statistics");
		leftPane.AddLogEntry("PostgreSQL replication monitor started");

		// Wait a bit to let other services start up
		await Task.Delay(2000, stoppingToken);

		var checkInterval = TimeSpan.FromSeconds(5); // Query every 5 seconds
		var maxRetryDelay = TimeSpan.FromSeconds(30);
		var consecutiveErrors = 0;

		while (!stoppingToken.IsCancellationRequested)
		{
			var startTime = DateTime.Now;

			try
			{
				using var scope = serviceProvider.CreateScope();
				var statsService = scope.ServiceProvider.GetRequiredService<PostgreSQLReplicationStatsService>();

				// Query primary database statistics
				try
				{
					var primaryStats = await statsService.QueryPrimaryReplicationStatsAsync(stoppingToken);
					replicationStatsPane.UpdatePrimaryStats(primaryStats);

					logger.LogDebug("Successfully updated PostgreSQL primary stats: LSN={CurrentLsn}, IsStandby={IsStandby}",
						primaryStats.CurrentLsn, primaryStats.IsStandby);
				}
				catch (Exception primaryEx)
				{
					logger.LogError(primaryEx, "Failed to query PostgreSQL replication stats from primary database");
					leftPane.AddLogEntry($"PostgreSQL primary stats error: {primaryEx.Message}");

					// Create a fallback stats object to show we tried but failed
					var fallbackStats = new PostgreSQLReplicationSummary
					{
						CurrentLsn = "Error",
						IsStandby = false,
						LastUpdated = DateTime.UtcNow
					};
					replicationStatsPane.UpdatePrimaryStats(fallbackStats);
				}

				// Query replica statistics (optional - for more comprehensive monitoring)
				foreach (var replica in _settings.ReplicaConnections)
				{
					try
					{
						var replicaStats = await statsService.QueryReplicaReplicationStatsAsync(replica.Name, replica.ConnectionString, stoppingToken);
						replicationStatsPane.UpdateReplicaStats(replica.Name, replicaStats);

						logger.LogDebug("Successfully updated PostgreSQL replica stats for {ReplicaName}: LSN={CurrentLsn}",
							replica.Name, replicaStats.CurrentLsn);
					}
					catch (Exception replicaEx)
					{
						logger.LogWarning(replicaEx, "Failed to query PostgreSQL replication stats from replica {ReplicaName}", replica.Name);
						// Continue with other replicas - don't fail the entire monitoring loop for one replica
					}
				}

				// Reset error count on success
				if (consecutiveErrors > 0)
				{
					leftPane.AddLogEntry("PostgreSQL replication stats monitoring restored");
					consecutiveErrors = 0;
				}

				logger.LogDebug("PostgreSQL replication statistics updated successfully");

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
				// Expected when stopping
				break;
			}
			catch (Exception ex)
			{
				consecutiveErrors++;
				logger.LogError(ex, "Unexpected error in PostgreSQL replication monitoring (attempt {ErrorCount})", consecutiveErrors);

				// Only log to UI on first error or every 10th consecutive error to avoid spam
				if (consecutiveErrors == 1)
				{
					leftPane.AddLogEntry($"PostgreSQL replication monitor error: {ex.Message}");
					leftPane.AddLogEntry("Note: PostgreSQL stats require pg_monitor role or superuser privileges");
				}
				else if (consecutiveErrors % 10 == 0)
				{
					leftPane.AddLogEntry($"PostgreSQL replication stats error (attempt {consecutiveErrors}): {ex.Message}");
				}

				// Progressive backoff on consecutive errors
				var retryDelay = TimeSpan.FromSeconds(Math.Min(5 * consecutiveErrors, maxRetryDelay.TotalSeconds));
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

		logger.LogInformation("PostgreSQL Replication Monitor stopped");
		leftPane.AddLogEntry("PostgreSQL replication monitor stopped");
	}
}