namespace SmartX.Ingest.Seeding;


/// The fault classes the seeder can inject on demand. Each one maps to a detection path
/// that has to be proven: without injectable faults there is no way to show that the
/// anomaly surface actually fires.
public enum FaultClass
{
    ///A single large excursion. Exercises the rolling median/MAD scorer.
    Spike,

    ///A slow ramp that never trips a fixed threshold. The hard case.
    Drift,

    ///One device stops publishing. Exercises the liveness tracker.
    Dropout,

    ///A device repeats its last value forever. A stuck sensor, not a calm one.
    FlatLine,

    /// Every device on a site drops at once. This is the load-shedding case, and the
    /// console must collapse it into one event rather than N alerts.
    SiteWideOutage,

    ///A device publishes the wrong type for its signal. Must be rejected, not coerced.
    TypeViolation,

    ///A burst far above the configured rate. Exercises queue backpressure.
    Flood
}

/// A request to inject a fault class into the simulated fleet. The seeder will return a
public sealed record FaultInjection(
    FaultClass Class,
    string? Site = null,
    string? DeviceId = null,
    string? Signal = null,
    int DurationMs = 15_000,
    double Magnitude = 8.0);
