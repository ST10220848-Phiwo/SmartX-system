using SmartX.Core.Telemetry;

namespace SmartX.Core.Storage;

/// Fixed-capacity circular buffer of readings for one signal.
/// Single-writer, many-reader. The ingest pipeline owns the write side; HTTP reads and the
/// SignalR delta pump only read. Capacity is rounded up to a power of two so the index
/// wrap is a mask rather than a division.
/// Readers use a sequence check rather than a lock: take the write cursor, copy, then
/// confirm the writer has not lapped the region that was copied. Under the load profile
/// this system is meant to survive, a reader-writer lock on the hot path is the thing that
/// falls over first.
public sealed class TelemetryRingBuffer
{
    private const int MaxRetries = 8;

    private readonly Reading[] _slots;
    private readonly int _mask;
    private long _writeCursor;

    public TelemetryRingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        var rounded = RoundUpToPowerOfTwo(capacity);
        _slots = new Reading[rounded];
        _mask = rounded - 1;
    }

    public int Capacity => _slots.Length;

    public long TotalWritten => Volatile.Read(ref _writeCursor);

    public int Count => (int)Math.Min(TotalWritten, _slots.Length);

    ///Appends a reading. Overwrites the oldest slot once full. Never allocates.
    public void Write(in Reading reading)
    {
        var cursor = _writeCursor;
        _slots[(int)(cursor & _mask)] = reading;
        Volatile.Write(ref _writeCursor, cursor + 1);
    }

    ///Returns the most recent reading, if any.
    public bool TryPeekLatest(out Reading reading)
    {
        var cursor = Volatile.Read(ref _writeCursor);
        if (cursor == 0)
        {
            reading = default;
            return false;
        }

        reading = _slots[(int)((cursor - 1) & _mask)];
        return true;
    }

    /// Copies up to <paramref name="destination"/>.Length readings, oldest first, into the
    /// caller's span. Returns the number written.
    public int CopyLatest(Span<Reading> destination)
    {
        if (destination.IsEmpty) return 0;

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var cursor = Volatile.Read(ref _writeCursor);
            if (cursor == 0) return 0;

            var available = (int)Math.Min(cursor, _slots.Length);
            var take = Math.Min(available, destination.Length);
            var start = cursor - take;

            for (var i = 0; i < take; i++)
            {
                destination[i] = _slots[(int)((start + i) & _mask)];
            }

            // If the writer has not lapped the region we just read, the copy is coherent.
            if (Volatile.Read(ref _writeCursor) - start <= _slots.Length)
            {
                return take;
            }
        }

        return 0;
    }

    /// Copies readings whose timestamp falls in [fromUnixMs, toUnixMs], oldest first.
    /// Readings are appended in arrival order, which is near-sorted but not guaranteed
    /// sorted, so this scans rather than binary-searches. Capacity is bounded, so the scan
    /// is bounded too.
    public int CopyRange(long fromUnixMs, long toUnixMs, Span<Reading> destination)
    {
        if (destination.IsEmpty) return 0;

        var scratch = destination.Length <= 512
            ? stackalloc Reading[destination.Length]
            : new Reading[destination.Length];

        // Walk newest-to-oldest so a narrow window near "now" exits early.
        var cursor = Volatile.Read(ref _writeCursor);
        var available = (int)Math.Min(cursor, _slots.Length);
        var written = 0;

        for (var offset = 1; offset <= available && written < scratch.Length; offset++)
        {
            ref readonly var candidate = ref _slots[(int)((cursor - offset) & _mask)];
            if (candidate.TimestampUnixMs < fromUnixMs) break;
            if (candidate.TimestampUnixMs > toUnixMs) continue;
            scratch[written++] = candidate;
        }

        // Reverse into the caller's span so the result reads oldest-first.
        for (var i = 0; i < written; i++)
        {
            destination[i] = scratch[written - 1 - i];
        }

        return written;
    }

    // Rounds up to the next power of two. If value is already a power of two, returns it unchanged.
    private static int RoundUpToPowerOfTwo(int value)
    {
        var result = 2;
        while (result < value) result <<= 1;
        return result;
    }
}
