namespace SmartX.Core.Telemetry;

/// <summary>
/// The closed set of telemetry value types Smart-X devices may publish.
/// Stored as a single byte so it fits inside <see cref="SignalValue"/> without
/// growing the struct past 16 bytes.
/// </summary>
public enum SignalKind : byte
{
    /// <summary>Reserved. A value of this kind never survives validation.</summary>
    Undefined = 0,

    /// <summary>IEEE-754 double. Soil moisture, temperature, voltage.</summary>
    Float = 1,

    /// <summary>Signed 64-bit integer. Power wattage, packet counters, RSSI.</summary>
    Integer = 2,

    /// <summary>Boolean. Valve state, relay state, door contact.</summary>
    Boolean = 3
}
