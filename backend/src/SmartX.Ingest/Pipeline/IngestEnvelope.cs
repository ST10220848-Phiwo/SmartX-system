using SmartX.Core.Telemetry;

namespace SmartX.Ingest.Pipeline;


/// What actually travels through the channel. Identity stays as strings here because the
/// registry lookup happens on the pipeline thread, not on the request thread — the HTTP
/// handler's job is to get the bytes off the socket and return.
public readonly record struct IngestEnvelope(
    string Site,
    string DeviceId,
    string Signal,
    SignalValue Value,
    long? DeviceTimestampUnixMs,
    uint Sequence,
    SignalKind? DeclaredKind,
    long ReceivedUnixMs,
    ReadingFlags Flags);
