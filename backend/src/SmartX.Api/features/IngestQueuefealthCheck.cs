using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartX.Ingest.Pipeline;

namespace SmartX.Api.Features;

// A gateway sitting at 90% queue depth is still serving reads correctly but
/// should stop receiving new ingest traffic from the load balancer.

public sealed class IngestQueueHealthCheck : IHealthCheck
{
    private readonly IngestQueue _queue;

    public IngestQueueHealthCheck(IngestQueue queue) => _queue = queue;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var utilisation = _queue.Utilisation;
        var data = new Dictionary<string, object>
        {
            ["depth"] = _queue.Depth,
            ["capacity"] = _queue.Capacity,
            ["utilisation"] = Math.Round(utilisation, 4)
        };

        var result = utilisation switch
        {
            >= 0.98 => HealthCheckResult.Unhealthy("Ingest queue is saturated; readings are being shed.", data: data),
            >= 0.90 => HealthCheckResult.Degraded("Ingest queue is above 90% capacity.", data: data),
            _ => HealthCheckResult.Healthy("Ingest queue has headroom.", data)
        };

        return Task.FromResult(result);
    }
}
