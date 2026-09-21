using SmartX.Core.Telemetry;

namespace SmartX.Core.Registry;


/// Identifies a signal by its position in the fleet -> site -> device -> signal hierarchy
/// the console drills through.
public readonly record struct SignalKey(string Site, string DeviceId, string Signal)
{
    public override string ToString() => $"{Site}/{DeviceId}/{Signal}";

    public static bool TryParse(string? path, out SignalKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(path)) return false;

        var parts = path.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || parts.Any(string.IsNullOrEmpty)) return false;

        key = new SignalKey(parts[0], parts[1], parts[2]);
        return true;
    }
}

/// The schema for one signal. The declared <see cref="Kind"/> is authoritative: a reading
/// whose kind disagrees with it is rejected at validation rather than converted.

public sealed record SignalDescriptor(
    int Id,
    SignalKey Key,
    SignalKind Kind,
    string? Unit = null,
    double? MinPlausible = null,
    double? MaxPlausible = null,
    int RingCapacity = 4096)
{
   
    /// Expected publish period in milliseconds. Drives the liveness tracker's gap threshold
    /// once the MQTT keep-alive lands in PR-2.
    public int ExpectedPeriodMs { get; init; } = 1000;
}
