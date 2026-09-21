namespace SmartX.Contracts;

/// <summary>
/// Stable rejection codes. The console shows these verbatim in the debugging pane, so they
/// are part of the contract and must not be reworded without a version bump.
/// </summary>
public static class RejectionCodes
{
    public const string MissingIdentity = "missing_identity";
    public const string UndefinedValue = "undefined_value";
    public const string TypeMismatch = "type_mismatch";
    public const string ValueOutOfRange = "out_of_range";
    public const string PrecisionLoss = "precision_loss";
    public const string TimestampImplausible = "timestamp_implausible";
    public const string QueueSaturated = "queue_saturated";
    public const string BatchTooLarge = "batch_too_large";
}
