using System.ComponentModel.DataAnnotations;

namespace DatabaseGrinder.Models;

/// <summary>
/// Represents a test record written to the database for replication monitoring
/// </summary>
public class TestRecord
{
	/// <summary>
	/// Auto-incrementing primary key for unique record identification
	/// </summary>
	[Key]
	public long Id { get; set; }

	/// <summary>
	/// Sequential number assigned by the application to detect missing records
	/// This helps identify gaps in replication beyond just lag
	/// </summary>
	public long SequenceNumber { get; set; }

	/// <summary>
	/// UTC timestamp when the record was created
	/// </summary>
	public DateTime Timestamp { get; set; }

	/// <summary>
	/// Creates a new test record with the current UTC time
	/// </summary>
	public TestRecord()
	{
		Timestamp = DateTime.UtcNow;
	}

	/// <summary>
	/// Creates a new test record with the specified timestamp and sequence number
	/// </summary>
	/// <param name="timestamp">The timestamp for this record</param>
	/// <param name="sequenceNumber">The sequence number for this record</param>
	public TestRecord(DateTime timestamp, long sequenceNumber)
	{
		Timestamp = timestamp;
		SequenceNumber = sequenceNumber;
	}
}