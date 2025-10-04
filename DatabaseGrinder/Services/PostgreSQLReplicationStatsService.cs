using DatabaseGrinder.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Collections.Concurrent;

namespace DatabaseGrinder.Services;

/// <summary>
/// PostgreSQL replication statistics from pg_stat_replication
/// </summary>
public class PostgreSQLReplicationStats
{
	public string ApplicationName { get; set; } = string.Empty;
	public string ClientAddr { get; set; } = string.Empty;
	public string State { get; set; } = string.Empty;
	public string SentLsn { get; set; } = string.Empty;
	public string WriteLsn { get; set; } = string.Empty;
	public string FlushLsn { get; set; } = string.Empty;
	public string ReplayLsn { get; set; } = string.Empty;
	public long WriteLag { get; set; } // microseconds
	public long FlushLag { get; set; } // microseconds  
	public long ReplayLag { get; set; } // microseconds
	public DateTime? BackendStart { get; set; }
	public string SyncState { get; set; } = string.Empty;
	public int SyncPriority { get; set; }
}

/// <summary>
/// PostgreSQL WAL receiver statistics from pg_stat_wal_receiver
/// </summary>
public class PostgreSQLWalReceiverStats
{
	public int Pid { get; set; }
	public string Status { get; set; } = string.Empty;
	public string ReceiveStartLsn { get; set; } = string.Empty;
	public string ReceiveStartTli { get; set; } = string.Empty;
	public string WrittenLsn { get; set; } = string.Empty;
	public string FlushedLsn { get; set; } = string.Empty;
	public string ReceivedTli { get; set; } = string.Empty;
	public DateTime? LastMsgSendTime { get; set; }
	public DateTime? LastMsgReceiptTime { get; set; }
	public string LatestEndLsn { get; set; } = string.Empty;
	public DateTime? LatestEndTime { get; set; }
	public string SlotName { get; set; } = string.Empty;
	public string SenderHost { get; set; } = string.Empty;
	public int SenderPort { get; set; }
	public string ConnInfo { get; set; } = string.Empty;
}

/// <summary>
/// PostgreSQL replication slot information from pg_replication_slots
/// </summary>
public class PostgreSQLReplicationSlot
{
	public string SlotName { get; set; } = string.Empty;
	public string Plugin { get; set; } = string.Empty;
	public string SlotType { get; set; } = string.Empty;
	public string Database { get; set; } = string.Empty;
	public bool Temporary { get; set; }
	public bool Active { get; set; }
	public int? ActivePid { get; set; }
	public string? XMin { get; set; }
	public string? CatalogXMin { get; set; }
	public string? RestartLsn { get; set; }
	public string? ConfirmedFlushLsn { get; set; }
	public string? WalStatus { get; set; }
	public long? SafeWalSize { get; set; }
	public bool? TwoPhase { get; set; }
}

/// <summary>
/// Combined PostgreSQL replication statistics summary
/// </summary>
public class PostgreSQLReplicationSummary
{
	public string CurrentLsn { get; set; } = string.Empty;
	public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
	public List<PostgreSQLReplicationStats> ReplicationStats { get; set; } = [];
	public PostgreSQLWalReceiverStats? WalReceiverStats { get; set; }
	public List<PostgreSQLReplicationSlot> ReplicationSlots { get; set; } = [];
	public bool IsStandby { get; set; }
	public string? PrimaryConnInfo { get; set; }
	public long MaxReplayLag { get; set; } // microseconds
	public int ActiveReplicas { get; set; }
	public int TotalSlots { get; set; }
	public int ActiveSlots { get; set; }
}

/// <summary>
/// Service for querying PostgreSQL native replication statistics
/// </summary>
public class PostgreSQLReplicationStatsService
{
	private readonly ILogger<PostgreSQLReplicationStatsService> _logger;
	private readonly DatabaseGrinderSettings _settings;
	private readonly ConcurrentDictionary<string, PostgreSQLReplicationSummary> _replicationStats = new();

	public PostgreSQLReplicationStatsService(
		ILogger<PostgreSQLReplicationStatsService> logger,
		IOptions<DatabaseGrinderSettings> settings)
	{
		_logger = logger;
		_settings = settings.Value;
	}

	/// <summary>
	/// Get the latest PostgreSQL replication statistics for all configured connections
	/// </summary>
	public IReadOnlyDictionary<string, PostgreSQLReplicationSummary> GetLatestStats()
	{
		return _replicationStats.AsReadOnly();
	}

	/// <summary>
	/// Query PostgreSQL replication statistics from the primary database
	/// </summary>
	public async Task<PostgreSQLReplicationSummary> QueryPrimaryReplicationStatsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new NpgsqlConnection(_settings.PrimaryConnection.ConnectionString);
			await connection.OpenAsync(cancellationToken);

			var summary = new PostgreSQLReplicationSummary();

			// Get current LSN
			summary.CurrentLsn = await GetCurrentLsnAsync(connection, cancellationToken);
			
			// Check if this is a standby server
			summary.IsStandby = await IsStandbyServerAsync(connection, cancellationToken);
			
			if (summary.IsStandby)
			{
				// If this is a standby, get WAL receiver stats
				summary.WalReceiverStats = await GetWalReceiverStatsAsync(connection, cancellationToken);
				summary.PrimaryConnInfo = summary.WalReceiverStats?.ConnInfo;
			}
			else
			{
				// If this is a primary, get replication stats
				summary.ReplicationStats = await GetReplicationStatsAsync(connection, cancellationToken);
				summary.ActiveReplicas = summary.ReplicationStats.Count(r => r.State == "streaming");
				summary.MaxReplayLag = summary.ReplicationStats.Count > 0 ? summary.ReplicationStats.Max(r => r.ReplayLag) : 0;
			}

			// Get replication slots (available on both primary and standby)
			summary.ReplicationSlots = await GetReplicationSlotsAsync(connection, cancellationToken);
			summary.TotalSlots = summary.ReplicationSlots.Count;
			summary.ActiveSlots = summary.ReplicationSlots.Count(s => s.Active);

			summary.LastUpdated = DateTime.UtcNow;

			// Cache the results
			_replicationStats["primary"] = summary;

			_logger.LogDebug("Retrieved PostgreSQL replication stats: IsStandby={IsStandby}, ActiveReplicas={ActiveReplicas}, ActiveSlots={ActiveSlots}",
				summary.IsStandby, summary.ActiveReplicas, summary.ActiveSlots);

			return summary;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to query PostgreSQL replication statistics from primary");
			throw;
		}
	}

	/// <summary>
	/// Query PostgreSQL replication statistics from a replica database
	/// </summary>
	public async Task<PostgreSQLReplicationSummary> QueryReplicaReplicationStatsAsync(string replicaName, string connectionString, CancellationToken cancellationToken = default)
	{
		try
		{
			using var connection = new NpgsqlConnection(connectionString);
			await connection.OpenAsync(cancellationToken);

			var summary = new PostgreSQLReplicationSummary();

			// Get current LSN
			summary.CurrentLsn = await GetCurrentLsnAsync(connection, cancellationToken);
			
			// Check if this is a standby server
			summary.IsStandby = await IsStandbyServerAsync(connection, cancellationToken);
			
			if (summary.IsStandby)
			{
				// Get WAL receiver stats for standby
				summary.WalReceiverStats = await GetWalReceiverStatsAsync(connection, cancellationToken);
				summary.PrimaryConnInfo = summary.WalReceiverStats?.ConnInfo;
			}

			// Get replication slots
			summary.ReplicationSlots = await GetReplicationSlotsAsync(connection, cancellationToken);
			summary.TotalSlots = summary.ReplicationSlots.Count;
			summary.ActiveSlots = summary.ReplicationSlots.Count(s => s.Active);

			summary.LastUpdated = DateTime.UtcNow;

			// Cache the results
			_replicationStats[replicaName] = summary;

			_logger.LogDebug("Retrieved PostgreSQL replication stats for {ReplicaName}: IsStandby={IsStandby}, ActiveSlots={ActiveSlots}",
				replicaName, summary.IsStandby, summary.ActiveSlots);

			return summary;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to query PostgreSQL replication statistics from replica {ReplicaName}", replicaName);
			throw;
		}
	}

	/// <summary>
	/// Get the current LSN (Log Sequence Number)
	/// </summary>
	private async Task<string> GetCurrentLsnAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			// Try to get current WAL LSN - this requires superuser or pg_monitor role
			const string query = "SELECT pg_current_wal_lsn();";
			using var command = new NpgsqlCommand(query, connection);
			var result = await command.ExecuteScalarAsync(cancellationToken);
			return result?.ToString() ?? "0/0";
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to get current LSN - may need superuser or pg_monitor role");
			// Return a default value if we can't access LSN
			return "N/A";
		}
	}

	/// <summary>
	/// Check if this server is a standby
	/// </summary>
	private async Task<bool> IsStandbyServerAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			const string query = "SELECT pg_is_in_recovery();";
			using var command = new NpgsqlCommand(query, connection);
			var result = await command.ExecuteScalarAsync(cancellationToken);
			return result is bool isRecovery && isRecovery;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to check if server is in recovery");
			return false; // Assume primary if we can't determine
		}
	}

	/// <summary>
	/// Get replication statistics from pg_stat_replication (primary server only)
	/// </summary>
	private async Task<List<PostgreSQLReplicationStats>> GetReplicationStatsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			const string query = @"
				SELECT 
					application_name,
					client_addr::text,
					state,
					sent_lsn::text,
					write_lsn::text,
					flush_lsn::text,
					replay_lsn::text,
					COALESCE(EXTRACT(microseconds FROM write_lag), 0)::bigint as write_lag,
					COALESCE(EXTRACT(microseconds FROM flush_lag), 0)::bigint as flush_lag,
					COALESCE(EXTRACT(microseconds FROM replay_lag), 0)::bigint as replay_lag,
					backend_start,
					sync_state,
					sync_priority
				FROM pg_stat_replication 
				ORDER BY application_name;";

			var stats = new List<PostgreSQLReplicationStats>();

			using var command = new NpgsqlCommand(query, connection);
			using var reader = await command.ExecuteReaderAsync(cancellationToken);

			while (await reader.ReadAsync(cancellationToken))
			{
				stats.Add(new PostgreSQLReplicationStats
				{
					ApplicationName = reader.GetString(0),
					ClientAddr = reader.IsDBNull(1) ? "" : reader.GetString(1),
					State = reader.GetString(2),
					SentLsn = reader.GetString(3),
					WriteLsn = reader.GetString(4),
					FlushLsn = reader.GetString(5),
					ReplayLsn = reader.GetString(6),
					WriteLag = reader.GetInt64(7),
					FlushLag = reader.GetInt64(8),
					ReplayLag = reader.GetInt64(9),
					BackendStart = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
					SyncState = reader.GetString(11),
					SyncPriority = reader.GetInt32(12)
				});
			}

			return stats;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to query pg_stat_replication - may need superuser or pg_monitor role, or no replication configured");
			return []; // Return empty list if we can't access replication stats
		}
	}

	/// <summary>
	/// Get WAL receiver statistics from pg_stat_wal_receiver (standby server only)
	/// </summary>
	private async Task<PostgreSQLWalReceiverStats?> GetWalReceiverStatsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			const string query = @"
				SELECT 
					pid,
					status,
					receive_start_lsn::text,
					receive_start_tli::text,
					written_lsn::text,
					flushed_lsn::text,
					received_tli::text,
					last_msg_send_time,
					last_msg_receipt_time,
					latest_end_lsn::text,
					latest_end_time,
					slot_name,
					sender_host,
					sender_port,
					conninfo
				FROM pg_stat_wal_receiver;";

			using var command = new NpgsqlCommand(query, connection);
			using var reader = await command.ExecuteReaderAsync(cancellationToken);

			if (await reader.ReadAsync(cancellationToken))
			{
				return new PostgreSQLWalReceiverStats
				{
					Pid = reader.GetInt32(0),
					Status = reader.GetString(1),
					ReceiveStartLsn = reader.GetString(2),
					ReceiveStartTli = reader.GetString(3),
					WrittenLsn = reader.GetString(4),
					FlushedLsn = reader.GetString(5),
					ReceivedTli = reader.GetString(6),
					LastMsgSendTime = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
					LastMsgReceiptTime = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
					LatestEndLsn = reader.GetString(9),
					LatestEndTime = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
					SlotName = reader.IsDBNull(11) ? "" : reader.GetString(11),
					SenderHost = reader.GetString(12),
					SenderPort = reader.GetInt32(13),
					ConnInfo = reader.GetString(14)
				};
			}

			return null;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to query pg_stat_wal_receiver - may need superuser or pg_monitor role, or not a standby server");
			return null;
		}
	}

	/// <summary>
	/// Get replication slot information from pg_replication_slots
	/// </summary>
	private async Task<List<PostgreSQLReplicationSlot>> GetReplicationSlotsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
	{
		try
		{
			const string query = @"
				SELECT 
					slot_name,
					plugin,
					slot_type,
					datname as database,
					temporary,
					active,
					active_pid,
					xmin::text,
					catalog_xmin::text,
					restart_lsn::text,
					confirmed_flush_lsn::text,
					wal_status,
					safe_wal_size,
					two_phase
				FROM pg_replication_slots
				LEFT JOIN pg_database ON pg_replication_slots.database = pg_database.oid
				ORDER BY slot_name;";

			var slots = new List<PostgreSQLReplicationSlot>();

			using var command = new NpgsqlCommand(query, connection);
			using var reader = await command.ExecuteReaderAsync(cancellationToken);

			while (await reader.ReadAsync(cancellationToken))
			{
				slots.Add(new PostgreSQLReplicationSlot
				{
					SlotName = reader.GetString(0),
					Plugin = reader.IsDBNull(1) ? "" : reader.GetString(1),
					SlotType = reader.GetString(2),
					Database = reader.IsDBNull(3) ? "" : reader.GetString(3),
					Temporary = reader.GetBoolean(4),
					Active = reader.GetBoolean(5),
					ActivePid = reader.IsDBNull(6) ? null : reader.GetInt32(6),
					XMin = reader.IsDBNull(7) ? null : reader.GetString(7),
					CatalogXMin = reader.IsDBNull(8) ? null : reader.GetString(8),
					RestartLsn = reader.IsDBNull(9) ? null : reader.GetString(9),
					ConfirmedFlushLsn = reader.IsDBNull(10) ? null : reader.GetString(10),
					WalStatus = reader.IsDBNull(11) ? null : reader.GetString(11),
					SafeWalSize = reader.IsDBNull(12) ? null : reader.GetInt64(12),
					TwoPhase = reader.IsDBNull(13) ? null : reader.GetBoolean(13)
				});
			}

			return slots;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to query pg_replication_slots - may need superuser or pg_monitor role");
			return []; // Return empty list if we can't access replication slots
		}
	}
}