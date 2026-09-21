namespace SmartX.Ingest.Pipeline;

public sealed class IngestOptions
{
    public const string SectionName = "SmartX:Ingest";

    
    /// Bounded queue depth. Bounded on purpose: an unbounded channel converts a traffic
    /// spike into an out-of-memory kill, which is a worse failure than shedding load.
    public int QueueCapacity { get; set; } = 65_536;

    /// How long a producer waits for queue space before the push is rejected.
    public int EnqueueTimeoutMs { get; set; } = 50;

    /// Maximum readings accepted in a single HTTP batch.   
    public int MaxBatchSize { get; set; } = 1000;

    /// Readings drained per pipeline iteration before yielding.
    public int DrainBatchSize { get; set; } = 512;

    /// Default ring buffer depth per signal.
    public int DefaultRingCapacity { get; set; } = 4096;

    /// Device timestamps further ahead of the gateway clock than this are replaced.
    public int MaxClockSkewAheadMs { get; set; } = 30_000;

    /// Device timestamps older than this are replaced with gateway receipt time.
    public int MaxClockSkewBehindMs { get; set; } = 86_400_000;
}
