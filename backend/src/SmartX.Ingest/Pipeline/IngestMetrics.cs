namespace SmartX.Ingest.Pipeline;

/// <summary>
/// Interlocked counters, read by the console's ingest health panel. Deliberately not a
/// dictionary of named counters — these are on the hot path and the set is closed.
/// </summary>
public sealed class IngestMetrics
{
    private long _received;
    private long _accepted;
    private long _rejectedValidation;
    private long _rejectedBackpressure;
    private long _dropped;

    public long Received => Interlocked.Read(ref _received);
    public long Accepted => Interlocked.Read(ref _accepted);
    public long RejectedValidation => Interlocked.Read(ref _rejectedValidation);
    public long RejectedBackpressure => Interlocked.Read(ref _rejectedBackpressure);
    public long Dropped => Interlocked.Read(ref _dropped);

    public void MarkReceived(long count = 1) => Interlocked.Add(ref _received, count);
    public void MarkAccepted(long count = 1) => Interlocked.Add(ref _accepted, count);
    public void MarkRejectedValidation() => Interlocked.Increment(ref _rejectedValidation);
    public void MarkRejectedBackpressure() => Interlocked.Increment(ref _rejectedBackpressure);
    public void MarkDropped() => Interlocked.Increment(ref _dropped);
}
