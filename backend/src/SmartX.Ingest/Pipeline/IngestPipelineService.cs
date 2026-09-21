using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartX.Core.Registry;
using SmartX.Core.Storage;
using SmartX.Core.Telemetry;

namespace SmartX.Ingest.Pipeline;

/// The single consumer of the ingest queue.
/// One reader on purpose. Per-signal ring buffers are single-writer structures, and a
/// single drain loop gives that for free without a lock per signal. When one core stops
/// being enough, the shard key is the signal id — partition the channel, not the buffer.
public sealed class IngestPipelineService : BackgroundService
{
    private readonly IngestQueue _queue;
    private readonly TelemetryValidator _validator;
    private readonly TelemetryStore _store;
    private readonly DeviceRegistry _devices;
    private readonly IngestMetrics _metrics;
    private readonly IngestOptions _options;
    private readonly ILogger<IngestPipelineService> _logger;

    public IngestPipelineService(
        IngestQueue queue,
        TelemetryValidator validator,
        TelemetryStore store,
        DeviceRegistry devices,
        IngestMetrics metrics,
        IOptions<IngestOptions> options,
        ILogger<IngestPipelineService> logger)
    {
        _queue = queue;
        _validator = validator;
        _store = store;
        _devices = devices;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Ingest pipeline started. Queue capacity {Capacity}, drain batch {Batch}.",
            _queue.Capacity, _options.DrainBatchSize);

        var reader = _queue.Reader;

        while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
        {
            var drained = 0;
            while (drained < _options.DrainBatchSize && reader.TryRead(out var envelope))
            {
                drained++;
                Process(in envelope);
            }

            if (drained > 0) _queue.OnDequeued(drained);
        }

        _logger.LogInformation("Ingest pipeline stopped. Drained queue depth {Depth}.", _queue.Depth);
    }

    private void Process(in IngestEnvelope envelope)
    {
        var device = _devices.GetOrAdd(envelope.DeviceId, envelope.Site);
        var outcome = _validator.Validate(in envelope);

        if (!outcome.IsValid)
        {
            device.MarkRejected();
            _metrics.MarkRejectedValidation();

            // Debug level: under a fault-injection run this fires thousands of times a
            // second, and the log is not the alerting surface. The counters are.
            _logger.LogDebug("Rejected reading from {Device}/{Signal}: {Code} {Detail}",
                envelope.DeviceId, envelope.Signal, outcome.Code, outcome.Detail);
            return;
        }

        var descriptor = outcome.Descriptor!;
        var reading = outcome.Reading;

        if (device.ObserveSequence(envelope.Sequence, out var gap) && gap > 0)
        {
            _logger.LogDebug("Sequence gap of {Gap} on {Device}.", gap, envelope.DeviceId);
        }

        _store.Append(descriptor, in reading);
        device.MarkSeen(reading.TimestampUnixMs);
        _metrics.MarkAccepted();
    }
}
