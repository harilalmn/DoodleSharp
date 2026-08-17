using System;
using System.Globalization;

namespace C2VGeometry;

/// <summary>
/// The value returned by <see cref="GlobalParameters.Get(string)"/>. It is a thin wrapper around the
/// stored object that implicitly converts to the parameter's underlying type, so a parameter reads
/// naturally at the use site without a type argument:
/// <code>
/// double half   = GlobalParameters.Get("String Length") * 0.5;
/// string status = GlobalParameters.Get("String Broken") ? " " : " not ";
/// </code>
/// <para>
/// Ambiguity caveat: because <c>ParamValue</c> converts implicitly to both <see cref="double"/> and
/// <see cref="string"/>, the <c>+</c> operator cannot pick an overload (<c>double + double</c> vs
/// <c>string + object</c>) and <c>Get("n") + 1</c> is a compile error. Every other operator is fine.
/// Use <see cref="Num"/>/<see cref="Text"/> or <see cref="GlobalParameters.Get{T}(string)"/> there:
/// <c>Get("n").Num + 1</c>. <see cref="int"/> is deliberately an <em>explicit</em> conversion for the
/// same reason — an implicit one would make <c>Get("n") * 2</c> ambiguous between <c>int * int</c>
/// and <c>double * double</c>.
/// </para>
/// </summary>
public readonly struct ParamValue
{
    private readonly string _name;
    private readonly object? _value;

    internal ParamValue(string name, object? value)
    {
        _name = name;
        _value = value;
    }

    /// <summary>True when the parameter exists in the registry.</summary>
    public bool Exists => _value is not null;

    /// <summary>The boxed underlying value (double, bool, string or DateTime), or null if undeclared.</summary>
    public object? Raw => _value;

    /// <summary>The parameter name this value was read from.</summary>
    public string Name => _name;

    // ── Explicitly named accessors — always unambiguous. ──
    public double Num => As<double>();
    public bool Flag => As<bool>();
    public string Text => As<string>();
    public DateTime Date => As<DateTime>();

    public static implicit operator double(ParamValue p) => p.As<double>();
    public static implicit operator bool(ParamValue p) => p.As<bool>();
    public static implicit operator string(ParamValue p) => p.As<string>();
    public static implicit operator DateTime(ParamValue p) => p.As<DateTime>();

    /// <summary>Explicit on purpose — see the ambiguity note on <see cref="ParamValue"/>.</summary>
    public static explicit operator int(ParamValue p) => (int)Math.Round(p.As<double>());

    /// <summary>Explicit on purpose — see the ambiguity note on <see cref="ParamValue"/>.</summary>
    public static explicit operator float(ParamValue p) => (float)p.As<double>();

    /// <summary>
    /// Reads the value as <typeparamref name="T"/>. Throws a descriptive
    /// <see cref="InvalidOperationException"/> when the parameter is undeclared or holds another type.
    /// </summary>
    public T As<T>()
    {
        if (_value is T typed)
            return typed;

        if (_value is null)
            throw new InvalidOperationException(
                $"Global parameter '{_name}' has not been declared. " +
                $"Declare it first with GlobalParameters.Set<{typeof(T).Name}>(\"{_name}\", ...). " +
                GlobalParameters.DescribeKnownNames());

        // Widening for the numeric family: everything numeric is stored as double.
        if (_value is double d)
        {
            if (typeof(T) == typeof(int)) return (T)(object)(int)Math.Round(d);
            if (typeof(T) == typeof(float)) return (T)(object)(float)d;
            if (typeof(T) == typeof(long)) return (T)(object)(long)Math.Round(d);
        }

        throw new InvalidOperationException(
            $"Global parameter '{_name}' is {_value.GetType().Name}, not {typeof(T).Name}.");
    }

    /// <summary>The value as a display string; empty when undeclared. Never throws.</summary>
    public override string ToString() => _value switch
    {
        null => "",
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture),
        _ => _value.ToString() ?? ""
    };
}
