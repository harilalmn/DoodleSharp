using System;
using System.Globalization;

namespace C2VGeometry;

/// <summary>
/// How much room a viewport row or column takes, written the way XAML writes a grid length.
///
/// <para>
/// <c>"*"</c> means one share of whatever is left, <c>"3*"</c> means three shares, and a plain
/// number is a fixed size in device pixels. Shares are relative, so <c>"3*"</c> beside <c>"*"</c>
/// gives three quarters of the space to the first — the same arithmetic a XAML <c>Grid</c> does.
/// </para>
/// </summary>
/// <remarks>
/// A parsed value rather than a raw string so that a typo fails where it is written, with a message
/// naming the spelling that was rejected, instead of silently collapsing a cell to nothing several
/// layers away in the renderer.
/// </remarks>
public readonly struct ViewportLength : IEquatable<ViewportLength>
{
    /// <summary>One share of the remaining space — the default for every row and column.</summary>
    public static readonly ViewportLength Star = new(1, true);

    private ViewportLength(double value, bool isStar)
    {
        Value = value;
        IsStar = isStar;
    }

    /// <summary>The number of shares when <see cref="IsStar"/>, otherwise a size in device pixels.</summary>
    public double Value { get; }

    /// <summary>True for <c>"*"</c> forms, false for a fixed pixel size.</summary>
    public bool IsStar { get; }

    /// <summary>
    /// Reads <c>"*"</c>, <c>"3*"</c>, <c>"1.5*"</c> or a plain number such as <c>"240"</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The text is not one of those forms. <c>"Auto"</c> is rejected by name: a canvas has no
    /// natural size, so an auto-sized viewport would collapse to nothing and look like the drawing
    /// had vanished.
    /// </exception>
    public static ViewportLength Parse(string? text)
    {
        var trimmed = text?.Trim() ?? "";

        if (trimmed.Length == 0)
            throw new ArgumentException("A viewport size cannot be empty. Use \"*\", \"3*\", or a number of pixels.", nameof(text));

        if (string.Equals(trimmed, "Auto", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "\"Auto\" is not a viewport size. A canvas has no natural size of its own, so an " +
                "auto-sized viewport would collapse to nothing. Use \"*\" for a share of the space, " +
                "or a number for a fixed size in pixels.", nameof(text));

        if (trimmed == "*") return Star;

        if (trimmed.EndsWith("*", StringComparison.Ordinal))
        {
            var sharesText = trimmed[..^1];
            if (!double.TryParse(sharesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var shares)
                || shares <= 0 || double.IsInfinity(shares))
            {
                throw new ArgumentException(
                    $"\"{trimmed}\" is not a valid viewport size. A star size is \"*\" or a positive " +
                    $"number followed by \"*\", such as \"3*\".", nameof(text));
            }
            return new ViewportLength(shares, true);
        }

        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels)
            || pixels < 0 || double.IsInfinity(pixels))
        {
            throw new ArgumentException(
                $"\"{trimmed}\" is not a valid viewport size. Use \"*\" for a share of the space, " +
                $"\"3*\" for three shares, or a non-negative number for a fixed size in pixels.", nameof(text));
        }

        return new ViewportLength(pixels, false);
    }

    /// <summary>The canonical spelling — what the property reads back after being set.</summary>
    public override string ToString() =>
        IsStar
            ? (Value == 1 ? "*" : Value.ToString("0.####", CultureInfo.InvariantCulture) + "*")
            : Value.ToString("0.####", CultureInfo.InvariantCulture);

    public bool Equals(ViewportLength other) => IsStar == other.IsStar && Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is ViewportLength other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, IsStar);
    public static bool operator ==(ViewportLength a, ViewportLength b) => a.Equals(b);
    public static bool operator !=(ViewportLength a, ViewportLength b) => !a.Equals(b);
}
