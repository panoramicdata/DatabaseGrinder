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
public class PostgreSQLReplicationMonitor : BackgroundService
{
	private readonly IServiceProvider _serviceProvider;
	private readonly ILogger<PostgreSQLReplicationMonitor> _logger;
	private readonly DatabaseGrinderSettings _settings;
	private readonly ReplicationStatsPane _replicationStatsPane;
	private readonly LeftPane _leftPane;

	public PostgreSQLReplicationMonitor(
		IServiceProvider serviceProvider,
		ILogger<PostgreSQLReplicationMonitor> logger,
		IOptions<DatabaseGrinderSettings> settings,
		ReplicationStatsPane replicationStatsPane,
		LeftPane leftPane)
	{
		_serviceProvider = serviceProvider;
		_logger = logger;
		_settings = settings.Value;
		_replicationStatsPane = replicationStatsPane;
		_leftPane = leftPane;
	}

	/// <summary>
	/// Main execution loop for PostgreSQL replication monitoring
	/// </summary>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_logger.LogInformation("PostgreSQL Replication Monitor started - querying native replication statistics");
		_leftPane.AddLogEntry("PostgreSQL replication monitor started");

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
				using var scope = _serviceProvider.CreateScope();
				var statsService = scope.ServiceProvider.GetRequiredService<PostgreSQLReplicationStatsService>();

				// Query primary database statistics
				var primaryStats = await statsService.QueryPrimaryReplicationStatsAsync(stoppingToken);
				_replicationStatsPane.UpdatePrimaryStats(primaryStats);

				// Query replica statistics (optional - for more comprehensive monitoring)
				foreach (var replica in _settings.ReplicaConnections)
				{
					try
					{
						var replicaStats = await statsService.QueryReplicaReplicationStatsAsync(replica.Name, replica.ConnectionString, stoppingToken);
						_replicationStatsPane.UpdateReplicaStats(replica.Name, replicaStats);
					}
					catch (Exception ex)
					{
						_logger.LogWarning(ex, "Failed to query PostgreSQL replication stats from replica {ReplicaName}", replica.Name);
						// Continue with other replicas - don't fail the entire monitoring loop for one replica
					}
				}

				// Reset error count on success
				if (consecutiveErrors > 0)
				{
					_leftPane.AddLogEntry("PostgreSQL replication stats monitoring restored");
					consecutiveErrors = 0;
				}

				_logger.LogDebug("PostgreSQL replication statistics updated successfully");

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
				_logger.LogError(ex, "Failed to query PostgreSQL replication statistics (attempt {ErrorCount})", consecutiveErrors);

				// Only log to UI on first error or every 10th consecutive error to avoid spam
				if (consecutiveErrors == 1 || consecutiveErrors % 10 == 0)
				{
					_leftPane.AddLogEntry($"PostgreSQL replication stats error (attempt {consecutiveErrors}): {ex.Message}");
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

		_logger.LogInformation("PostgreSQL Replication Monitor stopped");
		_leftPane.AddLogEntry("PostgreSQL replication monitor stopped");
	}
}