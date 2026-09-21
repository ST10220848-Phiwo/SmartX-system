using SmartX.Contracts;
using SmartX.Core.Registry;
using SmartX.Core.Storage;
using SmartX.Ingest.Pipeline;

namespace SmartX.Api.Endpoints;

// This class is a collection of the endpoints that make up the fleet management API. It is not a controller, but rather a set of route mappings that are registered with the ASP.NET Core routing system. The endpoints are grouped under the "/api/v1" path and tagged with "Fleet" for documentation purposes.

public static class FleetEndpoints
{
    public static IEndpointRouteBuilder MapFleetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Fleet");

        group.MapGet("/fleet/summary", (DeviceRegistry devices, SignalRegistry signals, TelemetryStore store) =>
        {
            var sites = devices.BySite()
                .Select(g => new SiteSummaryDto(
                    g.Key,
                    g.Count(),
                    g.Count(d => d.Status == DeviceStatus.Online),
                    g.Count(d => d.Status == DeviceStatus.Suspect),
                    g.Count(d => d.Status is DeviceStatus.Offline or DeviceStatus.CorrelatedOffline)))
                .OrderBy(s => s.Site)
                .ToArray();

            return TypedResults.Ok(new FleetSummaryDto(
                devices.Count, signals.Count, store.TotalReadingsStored(), sites));
        })
        .WithName("GetFleetSummary")
        .WithSummary("Fleet roll-up: the console's landing tier");

        group.MapGet("/devices", (DeviceRegistry devices, SignalRegistry signals, string? site) =>
        {
            var signalsByDevice = signals.Snapshot()
                .GroupBy(s => s.Key.DeviceId)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var result = devices.Snapshot()
                .Where(d => site is null || d.Site == site)
                .OrderBy(d => d.DeviceId, StringComparer.Ordinal)
                .Select(d => new DeviceSummaryDto(
                    d.DeviceId, d.Site, d.Status.ToString(), d.LastSeenUnixMs,
                    d.MessagesReceived, d.MessagesRejected,
                    signalsByDevice.GetValueOrDefault(d.DeviceId)))
                .ToArray();

            return TypedResults.Ok(result);
        })
        .WithName("ListDevices")
        .WithSummary("Devices, optionally filtered to one site");

        group.MapGet("/devices/{deviceId}", (string deviceId, DeviceRegistry devices, SignalRegistry signals) =>
        {
            if (!devices.TryGet(deviceId, out var device))
            {
                return Results.NotFound($"Device '{deviceId}' has not reported to this gateway.");
            }

            var deviceSignals = signals.Snapshot()
                .Where(s => s.Key.DeviceId == deviceId)
                .Select(s => new { s.Id, s.Key.Signal, Kind = s.Kind.ToString(), s.Unit })
                .ToArray();

            return Results.Ok(new
            {
                device.DeviceId,
                device.Site,
                Status = device.Status.ToString(),
                device.LastSeenUnixMs,
                device.MessagesReceived,
                device.MessagesRejected,
                Signals = deviceSignals
            });
        })
        .WithName("GetDevice")
        .WithSummary("One device and the signals it publishes");

        group.MapGet("/ingest/stats", (IngestMetrics metrics, IngestQueue queue) =>
            TypedResults.Ok(new IngestStatsDto(
                metrics.Received, metrics.Accepted, metrics.RejectedValidation,
                metrics.RejectedBackpressure, metrics.Dropped,
                queue.Depth, queue.Capacity, Math.Round(queue.Utilisation, 4))))
        .WithName("GetIngestStats")
        .WithSummary("Ingest throughput, rejections and queue pressure")
        .WithDescription(
            "The counters behind the console's ingest health strip. Queue utilisation " +
            "climbing toward 1.0 is the early warning that the gateway is about to shed load.");

        return app;
    }
}
