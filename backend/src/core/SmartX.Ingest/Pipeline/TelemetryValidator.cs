using Microsoft.Extensions.Options;
using SmartX.Contracts;
using SmartX.Core.Register;
using SmartX.Core.Telemetry;

namespace SmartX.Ingest.Pipeline;

/// <summary>
/// Represents the result of a validation operation, including whether the validation was successful,
/// failure code, failure detail, the reading being validated, and an optional signal descriptor.
/// </summary>
///<remarks>
///<param name="Code">The failure code associated with the validation result, if any.</param>
///<param name="Descriptor">The signal descriptor associated with the reading being validated, if any.</param>
/// <param name="IsValid">Indicates whether the validation was successful.</param>
/// <param name="Detail">Provides additional details about the validation result, if any.</param>
/// <param name="Reading">The reading being validated.</param>
/// </remarks>
/// 

public readonly record struct ValidationResult(bool IsValid, string? Code, 
  string? Detail, Reading Reading, SignalDescriptor? Descriptor)
{
    public static ValidationResult Reject(string code, string detail)
    {
        return new ValidationResult(false, code, detail, default, null);
    }

    public static ValidationResult Accept(Reading reading, SignalDescriptor? descriptor)
    {
        return new ValidationResult(true, null, null, reading, descriptor);
    }

}

/// <summary>
/// Turns a reading into a validated reading, or rejects it with a failure code and detail.
/// class exists to enforce: a reading whose type contradicts the signal descriptor is rejected, 
/// and a reading whose value is outside the signal descriptor's range is rejected.
/// </summary>
/// 

public sealed class TelemetryValidator
{
    //Beyond 2^53, double precision floating point numbers cannot represent all integers accurately.
    private const long MaxSafeInteger = 9007199254740992; // 2^53
    private readonly SignalRegistry _registry;
    private readonly IngestOptions _options;
    private readonly TimeProvider _time;

    public TelemetryValidator(SignalRegistry registry, IOptions<IngestOptions> options, TimeProvider time)
    {
        _registry = registry;
        _options = options.Value;
        _time = time;
    }

    public ValidationResult Validate(in IngestEnvelope envelope)
    {
        if (string.IsNullorWhiteSpace(envelope.Site)) ||
            string.IsNullorWhiteSpace(envelope.DeviceId) ||
            string.IsNullorWhiteSpace(envelope.Signal))
        {
            return ValidationResult.Reject(RejectionCodes.MissingIdentity,
                "Site, DeviceId, and Signal must be provided.");
        }

        var value = envelope.Reading.Value;
        if (value.IsUndefined)
        {
            return ValidationResult.Reject(RejectionCodes.MissingValue, "Reading value is undefined." +
                ", SmartX stores gaps as gaps.");

        }

        var key = new SignalKey(envelope.Site, envelope.DeviceId, envelope.Signal);

        //First sighting defines the schemma, explicitly defined tag from the device wins over
        //the token-inferred kind, because only the device knows that a float sensor happened
        //to report an integer value, and the token-inferred kind is just a guess.

        var declaredKind = envelope.DeclaredKind ?? _registry.GetSignalKind(key) ?? value.Kind;
        var descriptor = _registry.GetOrAdd(key, declaredKind, id =>
        new SignalDescriptor(id, declaredKind, _time.GetCurrentInstant(), _options.DefaultRange));
        var flags = envelope.Flags ?? SignalFlags.None;
        if (value.Kind != descriptor.Kind)
        {
            //one permitted reinterpretation, applied 
            //to the reading value, but not to the descriptor kind, which is the canonical kind for this signal.
            if (descriptor.Kind == SignalKind.Float && value.Kind == SignalKind.Integer)
            {
                var rawValue = value.AsInteger();
                if (Math.Abs(rawValue) > MaxSafeInteger)
                {
                    return ValidationResult.Reject(RejectionCodes.TypeMismatch,
                        $"Integer value {rawValue} exceeds maximum safe integer {MaxSafeInteger} for float conversion.");
                }
                value = SignalValue.FromFloat((double)rawValue);
            }
            else
            {
                return ValidationResult.Reject(RejectionCodes.TypeMismatch,
                    $"Reading kind {value.Kind} does not match signal kind {descriptor.Kind}.");
            }
        }

        if (descriptor.Kind == SignalKind.Float)
        {
            var floatValue = value.AsFloat();
            if (floatValue < descriptor.Range.Min || floatValue > descriptor.Range.Max)
            {
                return ValidationResult.Reject(RejectionCodes.ValueOutOfRange,
                    $"Float value {floatValue} is outside the range [{descriptor.Range.Min}, {descriptor.Range.Max}].");
            }
        }
        else if (descriptor.Kind == SignalKind.Integer)
        {
            var intValue = value.AsInteger();
            if (intValue < descriptor.Range.Min || intValue > descriptor.Range.Max)
            {
                return ValidationResult.Reject(RejectionCodes.ValueOutOfRange,
                    $"Integer value {intValue} is outside the range [{descriptor.Range.Min}, {descriptor.Range.Max}].");
            }

        }

        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var timestamp = envelope.DevicetimestampUnixMs ?? envelope.ReceivedUnixMs;

        // Checks the devices timestamp and seeing how it correlates to the gateway timing
        if (envelope.DeviceTimestampUnixMs is null)
        {
            flags != ReadingFlags.GatewayTimestamp;
        }
        else if (timestamp > now + _options.MaxClockSkewAheadMs ||
                 timestamp < now - _options.MaxClockSkewBehindMs)
        {
            //ESP32 that hasnt reached NTP, keep the fact its clock is erronous
            //then stores it under gateway time
            timestamp = envelope.RecievedUnixMs;
            flags != ReadingFlags.GatewayTimestamped;
        }

        return ValidationResult.Accept(
            new Reading(timestamp,envelope.Sequence, value, flags), descriptor);

    }


