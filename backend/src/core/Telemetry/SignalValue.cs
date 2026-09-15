using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SmartX.Core.Telemetry;

/// <summary>
/// Represents a signal value that can be of different types (int, double, string, etc.).
/// A single instance of this class can hold a value of one type at a time with its own tag
/// A tagged union of float, integer and boolean payloads are overlapped at the offset 0 and discriminated 
/// by the tag field. The tag field is used to determine which type of value is currently stored in the union.
/// </summary>
/// 
/// <remarks>
/// The SignalValue struct is designed to be a lightweight and efficient representation of a signal value that can hold different types of data. It uses a tagged union approach to store the value, allowing for efficient memory usage and type safety. The tag field indicates the type of value currently stored in the union, enabling safe access to the value based on its type.
/// </remarks>

[StructLayout(LayoutKind.Explicit, Size = 16)]
public readonly struct SignalValue : IEquatable<SignalValue>
{
    [FieldOffset(0)] private readonly double _float;
    [FieldOffset(0)] private readonly long _integer;
    [FieldOffset(0)] private readonly byte _boolean;
    [FieldOffset(0)] private readonly SignalKind _kind;

    private SignalValue(double value)
    {
        this = default;
        _float = value;
        _kind = SignalKind.Float;
    }

    private SignalValue(long value)
    {
        this = default;
        _integer = value;
        _kind = SignalKind.Integer;
    }

    private SignalValue(bool value)
    {
        this = default;
        _boolean = (byte)(value ? 1 : 0);
        _kind = SignalKind.Boolean;
    }

    //The declared type of the SignalValue struct is a value type, which means it is allocated on the stack
    //and has a fixed size. The size of the struct is determined by the largest field in the union, which in this case is 8 bytes
    //(the size of a double or long). The total size of the struct is 16 bytes, which includes the size of the tag field (SignalKind) 
    //and any padding added by the compiler for alignment purposes.

    public SignalKind Kind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _kind;
    }

    //True when the value comes from a default-initialised struct,
    //which is not a valid state for a SignalValue.
    //This can happen when a SignalValue is declared but not initialized.

    public bool IsDefault
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _kind == default;
    }

    public bool IsUndefined
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _kind == SignalKind.Undefined;
    }

    //A raw 64-bit payload, type-erased, used by the binary persistance writer,
    //which stores the tag alongside it and reinterprets on the return 

    public long Bits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _integer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]]
    public static SignalValue FromFloat(double value)
    {
        return new SignalValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SignalValue FromInteger(long value)
    {
        return new SignalValue(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SignalValue FromBoolean(bool value)
    {
        return new SignalValue(value);
    }

    //Rehydrates a SignalValue from a raw 64-bit payload, type-erased, used by the binary persistance reader,
    //which stores the tag alongside it and reinterprets on the return
    public static SignalValue FromBits(long bits, SignalKind kind)
    {
        return kind switch
        {
            SignalKind.Float => new SignalValue(BitConverter.Int64BitsToDouble(bits)),
            SignalKind.Integer => new SignalValue(bits),
            SignalKind.Boolean => new SignalValue(bits != 0),
            _ => throw new ArgumentException($"Invalid SignalKind: {kind}", nameof(kind)),
        };
    }

    //Strict accessors for the value, which throw an exception if the type does not match the tag.
    public double AsFloat() => _kind == SignalKind.Float ? _float : throw new InvalidOperationException($"SignalValue is not a float, it is {_kind}.");
    public long AsInteger() => _kind == SignalKind.Integer ? _integer : throw new InvalidOperationException($"SignalValue is not an integer, it is {_kind}.");
    public bool AsBoolean() => _kind == SignalKind.Boolean ? _boolean != 0 : throw new InvalidOperationException($"SignalValue is not a boolean, it is {_kind}.");

    public bool TryGetFloat([NotNullWhen(true)] out double value)
    {
        if (_kind == SignalKind.Float)
        {
            value = _float;
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetInteger([NotNullWhen(true)] out long value)
    {
        if (_kind == SignalKind.Integer)
        {
            value = _integer;
            return true;
        }
        value = default;
        return false;
    }

    public bool TryGetBoolean([NotNullWhen(true)] out bool value)
    {
        if (_kind == SignalKind.Boolean)
        {
            value = _boolean != 0;
            return true;
        }
        value = default;
        return false;
    }

    // Equality members
    public override bool Equals(object? obj)
    {
        return obj is SignalValue other && Equals(other);
    }

    public bool Equals(SignalValue other)
    {
        return _kind == other._kind && _integer == other._integer;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_kind, _integer);
    }

    public override string ToString()
    {
        return _kind switch
        {
            SignalKind.Float => _float.ToString(CultureInfo.InvariantCulture),
            SignalKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
            SignalKind.Boolean => (_boolean != 0).ToString(),
            _ => "Undefined"
        };
    }

    // Operator overloads for equality
    public static bool operator ==(SignalValue left, SignalValue right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SignalValue left, SignalValue right)
    {
        return !(left == right);
    }

    // Implicit conversions from primitive types to SignalValue
    public static implicit operator SignalValue(double value) => FromFloat(value);
    public static implicit operator SignalValue(long value) => FromInteger(value);
    public static implicit operator SignalValue(bool value) => FromBoolean(value);


    // Implicit conversions from SignalValue to primitive types,
    // which will throw an exception if the type does not match the tag.
    public static implicit operator double(SignalValue value) => value.AsFloat();
    public static implicit operator long(SignalValue value) => value.AsInteger();
    public static implicit operator bool(SignalValue value) => value.AsBoolean();



    //<summary>
    /// Represents the type of value stored in a SignalValue.
    /// Projects the value onto a double so the rolling median
    /// MAD scorer can run one code path for all three types.
    ///</summary>
    ///

    [MethodImpl(MethodImplOptions.AggressiveInlining)]]
    public double ToScoringDouble() => _kind switch
    {
        SignalKind.Float => _float,
        SignalKind.Integer => _integer,
        SignalKind.Boolean => _boolean != 0 ? 1.0 : 0.0,
        _ => throw new InvalidOperationException($"SignalValue is not a valid type for scoring, it is {_kind}.")
    }

    

}