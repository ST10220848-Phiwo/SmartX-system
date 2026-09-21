using SmartX.Api.Features;

namespace SmartX.Api.Endpoints;


/// Route surface for deliverables two and three. Gated off, documented, and shaped now so
/// the three client shells can be written against a contract that will not move.

public static class PlannedEndpoints
{
    public static IEndpointRouteBuilder MapCommandEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/commands")
            .WithTags("Commands (planned)")
            .RequireFeature(f => f.CommandStream, "Real-time command stream and history", "PR-5");

        group.MapPost("/{deviceId}", (string deviceId, object command) => Results.Accepted())
            .WithName("DispatchCommand")
            .WithSummary("Dispatch a command to a device")
            .WithDescription("Planned. Will queue a command, return a correlation id, and stream the acknowledgement.");

        group.MapGet("/{deviceId}/history", (string deviceId) => Results.Ok())
            .WithName("GetCommandHistory")
            .WithSummary("Command history for a device")
            .WithDescription("Planned. Append-only log of dispatch, acknowledgement and outcome.");

        return app;
    }

    public static IEndpointRouteBuilder MapTopologyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/topology")
            .WithTags("Topology (planned)")
            .RequireFeature(f => f.MeshTopology, "Network topology and mesh routing", "PR-6");

        group.MapGet("/", () => Results.Ok())
            .WithName("GetTopology")
            .WithSummary("Current mesh graph")
            .WithDescription("Planned. Adjacency graph of the ESP-MESH fleet with link quality per edge.");

        group.MapGet("/route/{fromDeviceId}/{toDeviceId}", (string fromDeviceId, string toDeviceId) => Results.Ok())
            .WithName("GetRoute")
            .WithSummary("Best route between two nodes")
            .WithDescription("Planned. Shortest path weighted by link quality and hop count.");

        return app;
    }
}
