using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;
using SmartX.Api.Features;
using SmartX.Core.Json;
using SmartX.Core.Registry;
using SmartX.Core.Storage;
using SmartX.Ingest.Pipeline;
using SmartX.Ingest.Seeding;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));
builder.Services.Configure<SeederOptions>(builder.Configuration.GetSection(SeederOptions.SectionName));
builder.Services.Configure<SmartXFeatures>(builder.Configuration.GetSection(SmartXFeatures.SectionName));

// ---------------------------------------------------------------------------
// JSON. The converter is what keeps telemetry types intact across the wire: values
// serialise as bare JSON scalars and their kind comes from the token, not from a parse.
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new SignalValueJsonConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ---------------------------------------------------------------------------
// Core services. All singletons: the registries and the store are process-wide state,
// and the pipeline is a single long-lived consumer.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SignalRegistry>();
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<IngestMetrics>();
builder.Services.AddSingleton<IngestQueue>();
builder.Services.AddSingleton<TelemetryValidator>();
builder.Services.AddHostedService<IngestPipelineService>();

// The seeder is registered unconditionally but no-ops unless SmartX:Seeder:Enabled is true,
// so the seeder control endpoints can find it without a second registration path.
builder.Services.AddSingleton<MockTelemetrySeeder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MockTelemetrySeeder>());

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<IngestQueueHealthCheck>("ingest-queue", tags: ["ready"]);

// ---------------------------------------------------------------------------
// CORS. One named policy shared by all three shells: React in the browser needs it,
// MAUI and WPF do not, but they use the identical contract.
// ---------------------------------------------------------------------------
const string ConsoleCors = "smartx-console";
builder.Services.AddCors(options => options.AddPolicy(ConsoleCors, policy =>
{
    var origins = builder.Configuration.GetSection("SmartX:Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:5173"];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(ConsoleCors);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ---------------------------------------------------------------------------
// Routes
// ---------------------------------------------------------------------------
app.MapTelemetryEndpoints();      // Deliverable 1 - live
app.MapFleetEndpoints();          // Deliverable 1 - live
app.MapSeederEndpoints();         // Development only
app.MapCommandEndpoints();        // Deliverable 2 - gated, answers 501
app.MapTopologyEndpoints();       // Deliverable 3 - gated, answers 501

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Smart-X Telemetry Gateway",
    version = "0.1.0",
    deliverables = new
    {
        telemetryIngestion = "live",
        commandStream = "planned (PR-5)",
        meshTopology = "planned (PR-6)"
    },
    openApi = "/openapi/v1.json"
})).ExcludeFromDescription();

app.Run();

/// <summary>Exposed so the integration test host can reference the entry point assembly.</summary>
public partial class Program;
