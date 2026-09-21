using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartX.Core.Telemetry;
using SmartX.Ingest.Pipeline;

namespace SmartX.Ingest.Seeding;


/// Drives a simulated ESP32 fleet into the real ingest queue.
/// The seeder pushes through <see cref="IngestQueue"/>, the same path an HTTP or MQTT
/// producer uses. It gets no shortcut into the store, so a load run genuinely exercises
/// validation, backpressure and the ring buffers.

public sealed class MockTelemetrySeeder : BackgroundService
{
    private readonly IngestQueue _queue;
    private readonly IngestMetrics _metrics;
    private readonly SeederOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MockTelemetrySeeder> _logger;
    private readonly ConcurrentDictionary<string, FaultInjection> _activeFaults = new();
    private readonly ConcurrentDictionary<string, long> _faultExpiry = new();
    private readonly ConcurrentDictionary<string, double> _lastFloat = new();
    private uint _sequence;

    public MockTelemetrySeeder(
        IngestQueue queue,
        IngestMetrics metrics,
        IOptions<SeederOptions> options,
        TimeProvider time,
        ILogger<MockTelemetrySeeder> logger)
    {
        _queue = queue;
        _metrics = metrics;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    ///Arms a fault. Returns the handle the console uses to clear it early.
    public string Inject(FaultInjection injection)
    {
        var handle = $"{injection.Class}:{Guid.NewGuid():N}"[..24];
        _activeFaults[handle] = injection;
        _faultExpiry[handle] = _time.GetUtcNow().ToUnixTimeMilliseconds() + injection.DurationMs;
        _logger.LogInformation("Injected fault {Handle} ({Class}) for {Duration}ms.",
            handle, injection.Class, injection.DurationMs);
        return handle;
    }

    public bool Clear(string handle)
    {
        _faultExpiry.TryRemove(handle, out _);
        return _activeFaults.TryRemove(handle, out _);
    }

    public IReadOnlyDictionary<string, FaultInjection> ActiveFaults => _activeFaults;

    /// The main loop. Every tick, push a reading for every device in every site, unless a fault
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Mock seeder is disabled.");
            return;
        }

        var random = new Random(_options.RandomSeed);
        var deviceCount = _options.Sites * _options.DevicesPerSite;
        _logger.LogInformation(
            "Mock seeder started: {Sites} sites x {Devices} devices = {Total} devices at {Interval}ms.",
            _options.Sites, _options.DevicesPerSite, deviceCount, _options.PublishIntervalMs);

        using var ticker = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PublishIntervalMs));

        while (await ticker.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            ExpireFaults();
            var now = _time.GetUtcNow().ToUnixTimeMilliseconds();

            for (var s = 0; s < _options.Sites; s++)
            {
                var site = $"site-{s:D2}";
                if (IsSiteDark(site)) continue;

                for (var d = 0; d < _options.DevicesPerSite; d++)
                {
                    var deviceId = $"esp32-{s:D2}{d:D3}";
                    if (IsDeviceDark(deviceId)) continue;

                    var repeats = FloodMultiplier(site, deviceId);
                    for (var r = 0; r < repeats; r++)
                    {
                        await PublishDeviceAsync(site, deviceId, now, random, stoppingToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async ValueTask PublishDeviceAsync(string site, string deviceId, long now, Random random, CancellationToken ct)
    {
        // Three signals, one of each kind, so every run exercises all three union branches.
        await PublishAsync(site, deviceId, "soil_moisture", NextFloat(deviceId, 42.0, 3.0, random), now, ct).ConfigureAwait(false);
        await PublishAsync(site, deviceId, "power_watts", SignalValue.FromInteger(random.Next(180, 260)), now, ct).ConfigureAwait(false);
        await PublishAsync(site, deviceId, "valve_open", SignalValue.FromBoolean(random.NextDouble() > 0.85), now, ct).ConfigureAwait(false);
    }

    private SignalValue NextFloat(string deviceId, double centre, double spread, Random random)
    {
        var previous = _lastFloat.GetValueOrDefault(deviceId, centre);

        if (HasFault(FaultClass.FlatLine, deviceId, out _))
        {
            return SignalValue.FromFloat(previous);
        }

        // Gaussian via Box-Muller, then a gentle pull back toward the centre so the series
        // wanders like a real sensor instead of random-walking off the chart.
        var u1 = 1.0 - random.NextDouble();
        var u2 = 1.0 - random.NextDouble();
        var gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        var value = previous + (centre - previous) * 0.05 + gaussian * spread * 0.15;

        if (HasFault(FaultClass.Spike, deviceId, out var spike))
        {
            value += spread * spike.Magnitude;
        }

        if (HasFault(FaultClass.Drift, deviceId, out var drift))
        {
            value += spread * drift.Magnitude * 0.01;
        }

        _lastFloat[deviceId] = value;
        return SignalValue.FromFloat(value);
    }

    private async ValueTask PublishAsync(string site, string deviceId, string signal, SignalValue value, long now, CancellationToken ct)
    {
        var flags = ReadingFlags.Synthetic;

        if (HasFault(FaultClass.TypeViolation, deviceId, out _) && signal == "soil_moisture")
        {
            // Deliberately wrong kind for this signal. The validator must reject it.
            value = SignalValue.FromBoolean(true);
            flags |= ReadingFlags.InjectedFault;
        }

        var envelope = new IngestEnvelope(
            site, deviceId, signal, value,
            DeviceTimestampUnixMs: now,
            Sequence: Interlocked.Increment(ref _sequence),
            DeclaredKind: null,
            ReceivedUnixMs: now,
            Flags: flags);

        _metrics.MarkReceived();
        var result = await _queue.EnqueueAsync(envelope, ct).ConfigureAwait(false);
        if (result == EnqueueResult.Saturated)
        {
            _metrics.MarkDropped();
        }
    }

    private int FloodMultiplier(string site, string deviceId) =>
        HasFault(FaultClass.Flood, deviceId, out var flood) || HasSiteFault(FaultClass.Flood, site, out flood)
            ? Math.Clamp((int)flood!.Magnitude, 1, 500)
            : 1;

    private bool IsSiteDark(string site) =>
        HasSiteFault(FaultClass.SiteWideOutage, site, out _);

    private bool IsDeviceDark(string deviceId) =>
        HasFault(FaultClass.Dropout, deviceId, out _);

    private bool HasFault(FaultClass cls, string deviceId, out FaultInjection? injection)
    {
        foreach (var fault in _activeFaults.Values)
        {
            if (fault.Class != cls) continue;
            if (fault.DeviceId is null || fault.DeviceId == deviceId)
            {
                injection = fault;
                return true;
            }
        }

        injection = null;
        return false;
    }

    private bool HasSiteFault(FaultClass cls, string site, out FaultInjection? injection)
    {
        foreach (var fault in _activeFaults.Values)
        {
            if (fault.Class != cls) continue;
            if (fault.Site is null || fault.Site == site)
            {
                injection = fault;
                return true;
            }
        }

        injection = null;
        return false;
    }

    private void ExpireFaults()
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var (handle, expiry) in _faultExpiry)
        {
            if (expiry > now) continue;
            _faultExpiry.TryRemove(handle, out _);
            _activeFaults.TryRemove(handle, out _);
            _logger.LogInformation("Fault {Handle} expired.", handle);
        }
    }
}
