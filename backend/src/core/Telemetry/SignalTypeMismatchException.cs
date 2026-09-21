namespace SmartX.Core.Telemetry;

/// <summary>
/// Thrown when a caller reads a <see cref="SignalValue"/> as the wrong type.
/// Deliberately an exception rather than a silent conversion: a mismatch here means the
/// signal schema and the consuming code have drifted apart, which we want to fail loudly
/// in tests rather than absorb at runtime.
/// </summary>
public sealed class SignalTypeMismatchException : InvalidOperationException
{
    private SignalTypeMismatchException(string message) : base(message) { }

    public static SignalTypeMismatchException For(SignalKind requested, SignalKind actual) =>
        new($"Signal value is of kind '{actual}' and was read as '{requested}'. " +
            "Smart-X does not coerce telemetry types; check the signal descriptor.");
}
