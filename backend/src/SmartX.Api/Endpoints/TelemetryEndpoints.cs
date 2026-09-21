using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SmartX.Contracts;
using SmartX.Core.Registry;
using SmartX.Core.Storage;
using SmartX.Core.Telemetry;
using SmartX.Ingest.Pipeline;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static RouteGroupBuilder MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/telemetry").WithTags("Telemetry");

        group.MapPost("/", IngestBatchAsync)
            .WithName("IngestTelemetryBatch")
            .WithSummary("Push a batch of readings")
            .WithDescription(
                "Returns 202 once the readings are queued, not once they are stored. " +
                "Returns 429 with Retry-After when the ingest queue is saturated: that is " +
                "backpressure, and a well-behaved device should slow down rather than retry immediately.")
            .Produces<IngestResultDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/single", IngestSingleAsync)
            .WithName("IngestTelemetryReading")
            .WithSummary("Push one reading")
            .WithDescription("Convenience route for bench testing a single device from the console.")
            .Produces<IngestResultDto>(StatusCodes.Status202Accepted);

        group.MapGet("/{site}/{deviceId}/{signal}", GetWindow)
            .WithName("GetSignalWindow")
            .WithSummary("Read a time window for one signal")
            .Produces<SignalWindowDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/signals", ListSignals)
            .WithName("ListSignals")
            .WithSummary("List every signal the gateway has seen");

        return group;
    }

    private static async Task<IResult> IngestBatchAsync(
        TelemetryBatchDto batch,
        IngestQueue queue,
        IngestMetrics metrics,
        IOptions<IngestOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        var opts = options.Value;

        if (batch.Readings.Count > opts.MaxBatchSize)
        {
            return TypedResults.Problem(
                title: "Batch too large",
                detail: $"This batch has {batch.Readings.Count} readings; the limit is {opts.MaxBatchSize}. " +
                        "Split it, or publish over MQTT instead.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                extensions: new Dictionary<string, object?> { ["code"] = RejectionCodes.BatchTooLarge });
        }

        var received = time.GetUtcNow().ToUnixTimeMilliseconds();
        var rejections = new List<IngestRejectionDto>();
        var accepted = 0;

        metrics.MarkReceived(batch.Readings.Count);

        for (var i = 0; i < batch.Readings.Count; i++)
        {
            var dto = batch.Readings[i];
            var envelope = new IngestEnvelope(
                dto.Site, dto.DeviceId, dto.Signal, dto.Value,
                dto.TimestampUnixMs, dto.Sequence, dto.Kind, received, ReadingFlags.None);

            var result = await queue.EnqueueAsync(envelope, ct);

            if (result == EnqueueResult.Accepted)
            {
                accepted++;
            }
            else
            {
                metrics.MarkRejectedBackpressure();
                rejections.Add(new IngestRejectionDto(i, RejectionCodes.QueueSaturated,
                    "Ingest queue is at capacity. Retry after the interval in the Retry-After header."));
            }
        }

        var payload = new IngestResultDto(accepted, rejections.Count, queue.Depth, rejections);

        // Any shedding at all is reported as 429 for the whole batch, so a device does not
        // have to inspect the body to learn it should back off.
        if (rejections.Count > 0 && accepted == 0)
        {
            return TypedResults.Json(payload, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return TypedResults.Accepted((string?)null, payload);
    }

    private static Task<IResult> IngestSingleAsync(
        TelemetryReadingDto reading,
        IngestQueue queue,
        IngestMetrics metrics,
        IOptions<IngestOptions> options,
        TimeProvider time,
        CancellationToken ct) =>
        IngestBatchAsync(new TelemetryBatchDto([reading]), queue, metrics, options, time, ct);

    private static Results<Ok<SignalWindowDto>, NotFound<string>> GetWindow(
        string site,
        string deviceId,
        string signal,
        SignalRegistry registry,
        TelemetryStore store,
        TimeProvider time,
        long? from = null,
        long? to = null,
        int max = 500)
    {
        max = Math.Clamp(max, 1, 5000);
        var key = new SignalKey(site, deviceId, signal);

        if (!registry.TryGet(key, out var descriptor) || !store.TryGet(descriptor.Id, out var buffer))
        {
            return TypedResults.NotFound($"No telemetry has been received for '{key}'.");
        }

        var readings = new Reading[max];
        int count;

        if (from is null && to is null)
        {
            count = buffer.CopyLatest(readings);
        }
        else
        {
            var now = time.GetUtcNow().ToUnixTimeMilliseconds();
            count = buffer.CopyRange(from ?? 0, to ?? now, readings);
        }

        var window = new SignalWindowDto(
            site, deviceId, signal, descriptor.Kind, descriptor.Unit, count,
            readings.Take(count).Select(ToDto).ToArray());

        return TypedResults.Ok(window);
    }

    private static IResult ListSignals(SignalRegistry registry) =>
        TypedResults.Ok(registry.Snapshot().Select(d => new
        {
            d.Id,
            Path = d.Key.ToString(),
            d.Key.Site,
            d.Key.DeviceId,
            Signal = d.Key.Signal,
            Kind = d.Kind.ToString(),
            d.Unit,
            d.RingCapacity
        }));

    private static ReadingDto ToDto(Reading reading) =>
        new(reading.TimestampUnixMs, reading.Sequence, reading.Value, DescribeFlags(reading.Flags));

    private static string[] DescribeFlags(ReadingFlags flags) =>
        flags == ReadingFlags.None
            ? []
            : Enum.GetValues<ReadingFlags>()
                .Where(f => f != ReadingFlags.None && flags.HasFlag(f))
                .Select(f => f.ToString())
                .ToArray();
}
