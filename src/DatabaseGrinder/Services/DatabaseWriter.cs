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

namespace DatabaseGrinder.Services;

/// <summary>
/// Statistics for database write operations
/// </summary>
public class WriteStatistics
{
    public long TotalRecords { get; set; }
    public long RecordsPerSecond { get; set; }
    public long ErrorCount { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime StartTime { get; set; }
    public string? LastError { get; set; }
    public bool IsConnected { get; set; } = true;

    public TimeSpan UpTime => DateTime.Now - StartTime;
}

/// <summary>
/// Background service that continuously writes timestamp records to the database
/// </summary>
public class DatabaseWriter : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseWriter> _logger;
    private readonly DatabaseGrinderSettings _settings;
    private readonly LeftPane _leftPane;
    private readonly WriteStatistics _statistics;
    private readonly Timer _cleanupTimer;
    private readonly ConcurrentQueue<DateTime> _recentWrites = new();

    public DatabaseWriter(
        IServiceProvider serviceProvider,
        ILogger<DatabaseWriter> logger, 
        IOptions<DatabaseGrinderSettings> settings,
        LeftPane leftPane)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _leftPane = leftPane;
        _statistics = new WriteStatistics 
        { 
            StartTime = DateTime.Now 
        };

        // Set up cleanup timer to run every minute
        _cleanupTimer = new Timer(
            callback: _ => _ = CleanupOldRecordsAsync(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Get current write statistics
    /// </summary>
    public WriteStatistics Statistics => _statistics;

    /// <summary>
    /// Main execution loop for the database writer
    /// </summary>
    /// <param name="stoppingToken">Cancellation token</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseWriter started - writing every {IntervalMs}ms", _settings.Settings.WriteIntervalMs);
        _leftPane.AddLogEntry("DatabaseWriter service started", LogLevel.Information);

        var writeInterval = TimeSpan.FromMilliseconds(_settings.Settings.WriteIntervalMs);
        var statsUpdateInterval = TimeSpan.FromSeconds(1);
        var lastStatsUpdate = DateTime.Now;

        while (!stoppingToken.IsCancellationRequested)
        {
            var startTime = DateTime.Now;

            try
            {
                // Write a new record
                await WriteRecordAsync(stoppingToken);
                
                // Update statistics
                _statistics.TotalRecords++;
                _statistics.LastWriteTime = DateTime.Now;
                _statistics.IsConnected = true;
                
                // Add to recent writes for rate calculation
                _recentWrites.Enqueue(DateTime.Now);
                
                // Update UI with successful write
                _leftPane.AddLogEntry($"Record #{_statistics.TotalRecords} inserted successfully", LogLevel.Information);
                
                // Update statistics display periodically
                if (DateTime.Now - lastStatsUpdate >= statsUpdateInterval)
                {
                    UpdateStatistics();
                    lastStatsUpdate = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                _statistics.ErrorCount++;
                _statistics.LastError = ex.Message;
                _statistics.IsConnected = false;
                
                _logger.LogError(ex, "Failed to write record to database");
                _leftPane.AddLogEntry($"ERROR: Write failed - {ex.Message}", LogLevel.Error);
                
                // Wait longer on error before retrying
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            // Calculate how long to wait for the next write
            var elapsed = DateTime.Now - startTime;
            var remainingTime = writeInterval - elapsed;
            
            if (remainingTime > TimeSpan.Zero)
            {
                await Task.Delay(remainingTime, stoppingToken);
            }
        }

        _logger.LogInformation("DatabaseWriter stopped");
        _leftPane.AddLogEntry("DatabaseWriter service stopped", LogLevel.Warning);
    }

    /// <summary>
    /// Write a single timestamp record to the database
    /// </summary>
    private async Task WriteRecordAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        
        var record = new TestRecord(DateTime.UtcNow);
        
        context.TestRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        
        _logger.LogDebug("Inserted record with ID {RecordId} at {Timestamp}", record.Id, record.Timestamp);
    }

    /// <summary>
    /// Update write rate statistics based on recent writes
    /// </summary>
    private void UpdateStatistics()
    {
        var cutoffTime = DateTime.Now.AddSeconds(-60); // Last 60 seconds
        
        // Remove writes older than 60 seconds and count remaining
        while (_recentWrites.TryPeek(out var writeTime) && writeTime < cutoffTime)
        {
            _recentWrites.TryDequeue(out _);
        }
        
        // Calculate writes per second (average over last 60 seconds)
        _statistics.RecordsPerSecond = _recentWrites.Count;
        
        // Update UI with current statistics
        var statsMessage = $"Stats: {_statistics.RecordsPerSecond}/sec | Total: {_statistics.TotalRecords} | Errors: {_statistics.ErrorCount} | Up: {_statistics.UpTime:hh\\:mm\\:ss}";
        _leftPane.AddLogEntry(statsMessage, LogLevel.Information);
    }

    /// <summary>
    /// Clean up records older than the retention period
    /// </summary>
    private async Task CleanupOldRecordsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            
            var cutoffTime = DateTime.UtcNow.AddMinutes(-_settings.Settings.DataRetentionMinutes);
            
            var oldRecords = await context.TestRecords
                .Where(r => r.Timestamp < cutoffTime)
                .ToListAsync();
            
            if (oldRecords.Count > 0)
            {
                context.TestRecords.RemoveRange(oldRecords);
                await context.SaveChangesAsync();
                
                _logger.LogInformation("Cleaned up {Count} old records older than {CutoffTime}", oldRecords.Count, cutoffTime);
                _leftPane.AddLogEntry($"Cleaned up {oldRecords.Count} old records", LogLevel.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old records");
            _leftPane.AddLogEntry($"Cleanup failed: {ex.Message}", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public override void Dispose()
    {
        _cleanupTimer?.Dispose();
        base.Dispose();
    }
}