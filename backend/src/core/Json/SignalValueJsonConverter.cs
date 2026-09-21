using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Core.Telemetry;

namespace SmartX.Core.Json;

// This class is used to convert SignalValue to and from JSON. It is used by the System.Text.Json serializer.
// SignalValue is a discriminated union of float, integer, and boolean values, and this converter handles the serialization and deserialization of these types.

public sealed class SignalValueJsonConverter : JsonConverter<SignalValue>
{
    public override SignalValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => SignalValue.FromBoolean(true),
            JsonTokenType.False => SignalValue.FromBoolean(false),
            JsonTokenType.Number => reader.TryGetInt64(out var i)
                ? SignalValue.FromInteger(i)
                : SignalValue.FromFloat(reader.GetDouble()),
            JsonTokenType.Null => default,
            _ => throw new JsonException(
                $"Telemetry values must be a number or a boolean; found '{reader.TokenType}'.")
        };

    public override void Write(Utf8JsonWriter writer, SignalValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case SignalKind.Float:
                writer.WriteNumberValue(value.AsFloat());
                break;
            case SignalKind.Integer:
                writer.WriteNumberValue(value.AsInteger());
                break;
            case SignalKind.Boolean:
                writer.WriteBooleanValue(value.AsBoolean());
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
