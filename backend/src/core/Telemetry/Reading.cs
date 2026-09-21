using System.Runtime.InteropServices;

namespace SmartX.Core.Telemetry;

/// <summary>Provenance and quality markers attached to a stored reading.</summary>
[Flags]
public enum ReadingFlags : ushort
{
    None = 0,

    /// <summary>Arrived with a timestamp older than the previous reading on this signal.</summary>
    OutOfOrder = 1 << 0,

    /// <summary>Device timestamp was absent or implausible; gateway receipt time was used.</summary>
    GatewayTimestamped = 1 << 1,

    /// <summary>Produced by the mock seeder rather than a device.</summary>
    Synthetic = 1 << 2,

    /// <summary>Deliberately injected fault, for load and detection testing.</summary>
    InjectedFault = 1 << 3,

    /// <summary>First reading after a liveness gap on this signal.</summary>
    ResumedAfterGap = 1 << 4
}

/// <summary>
/// One stored sample. 32 bytes, blittable, no references — so a ring buffer of these
/// is a single contiguous array that the GC never has to walk.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Reading
{
    /// <summary>Unix milliseconds UTC. Device clock where trusted, gateway clock otherwise.</summary>
    public readonly long TimestampUnixMs;

    /// <summary>Per-device monotonic counter, used to detect loss and replay.</summary>
    public readonly uint Sequence;

    public readonly ReadingFlags Flags;

    private readonly ushort _reserved;

    public readonly SignalValue Value;

    public Reading(long timestampUnixMs, uint sequence, SignalValue value, ReadingFlags flags = ReadingFlags.None)
    {
        TimestampUnixMs = timestampUnixMs;
        Sequence = sequence;
        Value = value;
        Flags = flags;
        _reserved = 0;
    }

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMs);

    public bool IsEmpty => Value.IsUndefined && TimestampUnixMs == 0;
}
