using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Resolves a colour string to a packed premultiplied BGRA value, once.
///
/// <para>
/// Shape colours are strings — a WPF colour name like "DodgerBlue", or a hex literal. The legacy
/// renderer resolved them through <c>ColorConverter</c> into a cached <c>Brush</c>; a rasterizer
/// needs an <see cref="int"/> it can write straight into a pixel buffer.
/// </para>
///
/// <para>
/// It delegates the actual parse to <c>ColorConverter</c> rather than reimplementing the ~140-entry
/// name table, so there is exactly one definition of what "Cyan" means and no possibility of the
/// two renderers disagreeing. The cache is what makes that affordable. <b>The fallback on an
/// unparseable colour is white</b>, matching the legacy <c>GetCachedBrush</c> — sketches
/// unknowingly depend on a typo'd colour still drawing something.
/// </para>
/// </summary>
public static class ColorTable
{
    private static readonly Dictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    /// <summary>Opaque white — what an unrecognised colour resolves to.</summary>
    public const int Fallback = unchecked((int)0xFFFFFFFF);

    /// <summary>
    /// Packs to premultiplied BGRA, the layout of <c>PixelFormats.Pbgra32</c>, so a resolved value
    /// can be written to a <c>WriteableBitmap</c> back buffer with no per-pixel conversion.
    /// </summary>
    public static int Resolve(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return Fallback;

        lock (_lock)
        {
            if (_cache.TryGetValue(color, out var cached)) return cached;
        }

        var packed = Parse(color);

        lock (_lock)
        {
            // Unbounded, but the key space is the set of distinct colour strings in one drawing —
            // bounded by what a human typed, not by anything that grows per frame.
            _cache[color] = packed;
        }
        return packed;
    }

    /// <summary>Applies a shape's separate <c>Opacity</c> on top of the colour's own alpha.</summary>
    public static int WithOpacity(int premultipliedBgra, double opacity)
    {
        if (opacity >= 1.0) return premultipliedBgra;
        if (opacity <= 0) return 0;

        var a = (premultipliedBgra >> 24) & 0xFF;
        var r = (premultipliedBgra >> 16) & 0xFF;
        var g = (premultipliedBgra >> 8) & 0xFF;
        var b = premultipliedBgra & 0xFF;

        // Already premultiplied, so every channel scales together.
        a = (int)(a * opacity);
        r = (int)(r * opacity);
        g = (int)(g * opacity);
        b = (int)(b * opacity);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    public static bool IsFullyTransparent(int premultipliedBgra) =>
        ((premultipliedBgra >> 24) & 0xFF) == 0;

    private static int Parse(string color)
    {
        try
        {
            var converted = ColorConverter.ConvertFromString(color);
            if (converted is not Color c) return Fallback;

            // Premultiply. Pbgra32 stores colour channels already scaled by alpha; writing
            // straight (non-premultiplied) values produces washed-out edges wherever alpha < 255.
            var a = c.A;
            var r = (byte)(c.R * a / 255);
            var g = (byte)(c.G * a / 255);
            var b = (byte)(c.B * a / 255);

            return (a << 24) | (r << 16) | (g << 8) | b;
        }
        catch
        {
            // Matches the legacy renderer, which swallowed the same exception and returned white.
            // A drawing with one bad colour string still renders.
            return Fallback;
        }
    }

    /// <summary>For tests: clears the memo so a run is not influenced by an earlier one.</summary>
    internal static void Clear()
    {
        lock (_lock) _cache.Clear();
    }

    /// <summary>Formats a packed value as #AARRGGBB, for test failure messages.</summary>
    internal static string Describe(int packed) =>
        "#" + packed.ToString("X8", CultureInfo.InvariantCulture);
}
