using SmartX.Api.Features;
using SmartX.Ingest.Seeding;

namespace SmartX.Api.Endpoints;

/// Load and fault controls. Gated behind <see cref="SmartXFeatures.SeederControls"/>, which
/// is off outside development — these routes can knock the gateway over on purpose.
public static class SeederEndpoints
{
    public static IEndpointRouteBuilder MapSeederEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/seed")
            .WithTags("Seeder")
            .RequireFeature(f => f.SeederControls, "Seeder controls", "development only");

        group.MapPost("/fault", (FaultInjection injection, IEnumerable<IHostedService> hosted) =>
        {
            var seeder = hosted.OfType<MockTelemetrySeeder>().FirstOrDefault();
            if (seeder is null)
            {
                return Results.Problem("The mock seeder is not running.", statusCode: StatusCodes.Status409Conflict);
            }

            var handle = seeder.Inject(injection);
            return Results.Accepted($"/api/v1/seed/fault/{handle}", new { handle, injection });
        })
        .WithName("InjectFault")
        .WithSummary("Arm a fault class against the simulated fleet")
        .WithDescription(
            "Spike and Drift exercise the anomaly scorer. Dropout and SiteWideOutage exercise " +
            "liveness and correlation. TypeViolation proves readings are rejected rather than " +
            "coerced. Flood drives the queue into backpressure.");

        group.MapDelete("/fault/{handle}", (string handle, IEnumerable<IHostedService> hosted) =>
        {
            var seeder = hosted.OfType<MockTelemetrySeeder>().FirstOrDefault();
            return seeder?.Clear(handle) == true ? Results.NoContent() : Results.NotFound();
        })
        .WithName("ClearFault")
        .WithSummary("Clear an armed fault early");

        group.MapGet("/faults", (IEnumerable<IHostedService> hosted) =>
        {
            var seeder = hosted.OfType<MockTelemetrySeeder>().FirstOrDefault();
            return Results.Ok(seeder?.ActiveFaults ?? new Dictionary<string, FaultInjection>());
        })
        .WithName("ListActiveFaults")
        .WithSummary("Faults currently armed");

        return app;
    }
}
