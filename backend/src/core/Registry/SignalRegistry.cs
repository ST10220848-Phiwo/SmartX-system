using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SmartX.Core.Telemetry;

namespace SmartX.Core.Registry;

/// Interns <see cref="SignalKey"/> triples into dense integer handles.
/// The string key is hashed exactly once, when a signal is first seen. Every stage after
/// that — ring buffer lookup, scorer state, SignalR group fan-out — indexes an array with
/// an int instead of hashing three strings per message. At the message rates this POE is
/// expected to demonstrate, that difference is the whole budget.
public sealed class SignalRegistry
{
    private readonly ConcurrentDictionary<SignalKey, int> _ids = new();
    private readonly ConcurrentDictionary<string, string> _deviceSites = new(StringComparer.Ordinal);
    private readonly object _growLock = new();
    private SignalDescriptor?[] _descriptors = new SignalDescriptor?[1024];
    private int _count;

    public int Count => Volatile.Read(ref _count);

    ///Registers a signal, or returns the existing handle if it is already known.
    public SignalDescriptor GetOrAdd(SignalKey key, SignalKind kind, Func<int, SignalDescriptor>? factory = null)
    {
        if (_ids.TryGetValue(key, out var existingId))
        {
            return _descriptors[existingId]!;
        }

        lock (_growLock)
        {
            if (_ids.TryGetValue(key, out existingId))
            {
                return _descriptors[existingId]!;
            }

            var id = _count;
            if (id >= _descriptors.Length)
            {
                var grown = new SignalDescriptor?[_descriptors.Length * 2];
                Array.Copy(_descriptors, grown, _descriptors.Length);
                Volatile.Write(ref _descriptors, grown);
            }

            var descriptor = factory?.Invoke(id) ?? new SignalDescriptor(id, key, kind);
            _descriptors[id] = descriptor;
            _ids[key] = id;
            _deviceSites[key.DeviceId] = key.Site;
            Volatile.Write(ref _count, id + 1);
            return descriptor;
        }
    }

    public bool TryGet(SignalKey key, [NotNullWhen(true)] out SignalDescriptor? descriptor)
    {
        if (_ids.TryGetValue(key, out var id))
        {
            descriptor = _descriptors[id];
            return descriptor is not null;
        }

        descriptor = null;
        return false;
    }

    public bool TryGet(int id, [NotNullWhen(true)] out SignalDescriptor? descriptor)
    {
        var snapshot = Volatile.Read(ref _descriptors);
        if ((uint)id < (uint)snapshot.Length && snapshot[id] is { } found)
        {
            descriptor = found;
            return true;
        }

        descriptor = null;
        return false;
    }

    public bool TryGetSiteForDevice(string deviceId, [NotNullWhen(true)] out string? site) =>
        _deviceSites.TryGetValue(deviceId, out site);

    public IReadOnlyList<SignalDescriptor> Snapshot()
    {
        var snapshot = Volatile.Read(ref _descriptors);
        var count = Volatile.Read(ref _count);
        var result = new List<SignalDescriptor>(count);
        for (var i = 0; i < count && i < snapshot.Length; i++)
        {
            if (snapshot[i] is { } d) result.Add(d);
        }

        return result;
    }
}
