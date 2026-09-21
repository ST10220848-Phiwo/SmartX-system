using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SmartX.Core.Registry;
/// <summary>
/// This class tracks the state of devices that have reported to this gateway, including their liveness and message statistics. It is used to provide fleet summaries and device-level diagnostics.
/// It is thread-safe and lock-free, designed for high-concurrency scenarios where many devices may be reporting simultaneously.
/// </summary>
public enum DeviceStatus : byte
{
    Unknown = 0,

    ///Reporting within its expected period.
    Online = 1,

    ///Missed its window but not yet past the disconnect threshold (anti-flapping).
    Suspect = 2,

    ///Confirmed absent, either by timeout or by MQTT Last Will and Testament.
    Offline = 3,

    /// Offline alongside enough of its site to be treated as one correlated event rather
    /// than N independent alerts. Set by the correlation pass in PR-3.
    CorrelatedOffline = 4
}

public sealed class DeviceState
{
    private long _lastSeenUnixMs;
    private long _messagesReceived;
    private long _messagesRejected;
    private int _status;
    private long _lastSequence = -1;

    public DeviceState(string deviceId, string site)
    {
        DeviceId = deviceId;
        Site = site;
    }

    public string DeviceId { get; }
    public string Site { get; }
    public long LastSeenUnixMs => Volatile.Read(ref _lastSeenUnixMs);
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);
    public long MessagesRejected => Interlocked.Read(ref _messagesRejected);
    public DeviceStatus Status => (DeviceStatus)Volatile.Read(ref _status);

    public void MarkSeen(long unixMs)
    {
        // Clocks skew; never move lastSeen backwards.
        long current;
        while ((current = Volatile.Read(ref _lastSeenUnixMs)) < unixMs)
        {
            if (Interlocked.CompareExchange(ref _lastSeenUnixMs, unixMs, current) == current) break;
        }

        Interlocked.Increment(ref _messagesReceived);
        Volatile.Write(ref _status, (int)DeviceStatus.Online);
    }

    public void MarkRejected() => Interlocked.Increment(ref _messagesRejected);

    public void SetStatus(DeviceStatus status) => Volatile.Write(ref _status, (int)status);

    // This method is called from the telemetry ingestion path, which is highly concurrent. It must be lock-free and fast.
    // It returns true if the sequence is out of order (i.e., a gap was detected), and false otherwise. The gap is returned as an out parameter.

    public bool ObserveSequence(uint sequence, out long gap)
    {
        var previous = Interlocked.Exchange(ref _lastSequence, sequence);
        gap = 0;
        if (previous < 0) return false;
        var delta = sequence - previous;
        if (delta <= 1) return false;
        gap = delta - 1;
        return true;
    }
}

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices = new(StringComparer.Ordinal);

    public int Count => _devices.Count;

    public DeviceState GetOrAdd(string deviceId, string site) =>
        _devices.GetOrAdd(deviceId, static (id, s) => new DeviceState(id, s), site);

    public bool TryGet(string deviceId, [NotNullWhen(true)] out DeviceState? state) =>
        _devices.TryGetValue(deviceId, out state);

    public IReadOnlyCollection<DeviceState> Snapshot() => _devices.Values.ToArray();

    public IEnumerable<IGrouping<string, DeviceState>> BySite() => _devices.Values.GroupBy(d => d.Site);
}
