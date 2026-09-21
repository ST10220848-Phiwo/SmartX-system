using System.Text.Json.Serialization;
using SmartX.Core.Telemetry;

namespace SmartX.Contracts;

/// <summary>
/// One reading as it arrives on the wire. Field names are short because an ESP32 pays for
/// every byte it publishes, and at fleet scale so does the gateway.
/// </summary>
/// <param name="Site">Site code, e.g. "jhb-north".</param>
/// <param name="DeviceId">Stable device identifier, e.g. "esp32-0417".</param>
/// <param name="Signal">Signal name within the device, e.g. "soil_moisture".</param>
/// <param name="Value">The value, typed by its JSON token. Never a quoted string.</param>
/// <param name="TimestampUnixMs">Device clock in Unix ms UTC. Omit to use gateway receipt time.</param>
/// <param name="Sequence">Per-device monotonic counter used for loss detection.</param>
/// <param name="Kind">
/// Optional explicit type tag. Supply it for float signals that can report whole numbers,
/// otherwise the gateway resolves the kind against the registered signal descriptor.
/// </param>
public sealed record TelemetryReadingDto(
    [property: JsonPropertyName("site")] string Site,
    [property: JsonPropertyName("dev")] string DeviceId,
    [property: JsonPropertyName("sig")] string Signal,
    [property: JsonPropertyName("v")] SignalValue Value,
    [property: JsonPropertyName("ts")] long? TimestampUnixMs = null,
    [property: JsonPropertyName("seq")] uint Sequence = 0,
    [property: JsonPropertyName("k")] SignalKind? Kind = null
);

/// <summary>A batch push. Batching is how a device amortises TLS and header cost.</summary>
public sealed record TelemetryBatchDto(
    [property: JsonPropertyName("readings")] IReadOnlyList<TelemetryReadingDto> Readings);

/// <summary>Per-reading outcome, returned only for the readings that failed.</summary>
public sealed record IngestRejectionDto(int Index, string Code, string Detail);

/// <summary>
/// Result of a push. Accepted means queued for the pipeline, not yet durably stored —
/// the endpoint answers 202, never 200, so the contract cannot be misread as a write ack.
/// </summary>
public sealed record IngestResultDto(
    int Accepted,
    int Rejected,
    long QueueDepth,
    IReadOnlyList<IngestRejectionDto> Rejections);

public sealed record ReadingDto(
    [property: JsonPropertyName("ts")] long TimestampUnixMs,
    [property: JsonPropertyName("seq")] uint Sequence,
    [property: JsonPropertyName("v")] SignalValue Value,
    [property: JsonPropertyName("f")] string[] Flags
);

public sealed record SignalWindowDto(
    string Site,
    string DeviceId,
    string Signal,
    SignalKind Kind,
    string? Unit,
    int Count,
    IReadOnlyList<ReadingDto> Readings
);

public sealed record DeviceSummaryDto(
    string DeviceId,
    string Site,
    string Status,
    long LastSeenUnixMs,
    long MessagesReceived,
    long MessagesRejected,
    int SignalCount
);

public sealed record SiteSummaryDto(
    string Site,
    int DeviceCount,
    int Online,
    int Suspect,
    int Offline
);

public sealed record FleetSummaryDto(
    int DeviceCount,
    int SignalCount,
    long ReadingsStored,
    IReadOnlyList<SiteSummaryDto> Sites
);

public sealed record IngestStatsDto(
    long Received,
    long Accepted,
    long RejectedValidation,
    long RejectedBackpressure,
    long Dropped,
    long QueueDepth,
    int QueueCapacity,
    double QueueUtilisation
);
