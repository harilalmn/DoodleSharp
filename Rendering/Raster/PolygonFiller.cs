using System;
using System.Collections.Generic;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Fills closed outlines by scanline, with holes.
///
/// <para>
/// A classic active-edge approach without the active-edge <i>table</i>: for each scanline it
/// collects the x-crossings of every edge, sorts them, and fills between pairs. Rebuilding the
/// crossing list per row is O(rows x edges) rather than the AET's O(rows + edges log edges), which
/// sounds worse and is not, at the sizes involved — a filled shape in a technical drawing has tens
/// of edges, not thousands, and the flat scan has no per-edge bookkeeping, no allocation, and no
/// sorted-insert. Hatches and regions are where edge counts get large, and those arrive already
/// decomposed into segments.
/// </para>
///
/// <para>
/// Both fill rules are supported because they genuinely differ for self-intersecting outlines, and
/// <c>Region</c> holes rely on even-odd.
/// </para>
/// </summary>
public sealed class PolygonFiller
{
    private double[] _crossings = new double[64];
    private int[] _winding = new int[64];

    /// <summary>
    /// Fills <paramref name="loops"/> into <paramref name="pixels"/>. The first loop is the outer
    /// boundary; the rest are holes. Coordinates are device pixels.
    /// </summary>
    public void Fill(int[] pixels, int stride, int height,
                     IReadOnlyList<IReadOnlyList<ScreenPoint>> loops,
                     int color, bool evenOdd, int clipTop, int clipBottom)
    {
        if (loops.Count == 0) return;
        if (clipTop < 0) clipTop = 0;
        if (clipBottom > height - 1) clipBottom = height - 1;
        if (clipTop > clipBottom) return;

        // Vertical extent of the whole outline, so rows nowhere near it are never visited.
        var minY = double.MaxValue;
        var maxY = double.MinValue;
        var edgeCount = 0;

        // Indexed loops throughout, never foreach: iterating an interface-typed collection
        // allocates an enumerator on every call, and this runs per filled shape per frame. The
        // allocation test in RasterizerTests exists to keep it that way.
        for (int li = 0; li < loops.Count; li++)
        {
            var loop = loops[li];
            if (loop.Count < 3) continue;
            edgeCount += loop.Count;
            for (int i = 0; i < loop.Count; i++)
            {
                var p = loop[i];
                if (!double.IsFinite(p.Y)) return;   // a single bad vertex invalidates the fill
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        if (edgeCount == 0) return;

        EnsureCapacity(edgeCount);

        var y0 = Math.Max(clipTop, (int)Math.Ceiling(minY));
        var y1 = Math.Min(clipBottom, (int)Math.Floor(maxY));

        for (int y = y0; y <= y1; y++)
        {
            // Sample at pixel centres. Using the integer row instead makes a horizontal edge lying
            // exactly on a scanline register twice, which flips the parity for the whole rest of
            // the row and leaves a visible tear.
            var scan = y + 0.5;
            var count = 0;

            for (int li = 0; li < loops.Count; li++)
            {
                var loop = loops[li];
                if (loop.Count < 3) continue;

                for (int i = 0; i < loop.Count; i++)
                {
                    var a = loop[i];
                    var b = loop[(i + 1) % loop.Count];

                    if (!double.IsFinite(a.X) || !double.IsFinite(b.X)) continue;

                    // Half-open in y: a vertex shared by two edges is counted once, which is what
                    // keeps parity correct at every corner.
                    var down = a.Y <= scan && b.Y > scan;
                    var up = b.Y <= scan && a.Y > scan;
                    if (!down && !up) continue;

                    var t = (scan - a.Y) / (b.Y - a.Y);
                    _crossings[count] = a.X + t * (b.X - a.X);
                    _winding[count] = down ? 1 : -1;
                    count++;
                }
            }

            if (count < 2) continue;

            SortCrossings(count);

            if (evenOdd)
            {
                for (int i = 0; i + 1 < count; i += 2)
                    FillSpan(pixels, stride, y, _crossings[i], _crossings[i + 1], color);
            }
            else
            {
                var winding = 0;
                for (int i = 0; i + 1 < count; i++)
                {
                    winding += _winding[i];
                    if (winding != 0)
                        FillSpan(pixels, stride, y, _crossings[i], _crossings[i + 1], color);
                }
            }
        }
    }

    private static void FillSpan(int[] pixels, int stride, int y, double xa, double xb, int color)
    {
        var x0 = (int)Math.Ceiling(Math.Min(xa, xb) - 0.5);
        var x1 = (int)Math.Floor(Math.Max(xa, xb) - 0.5);

        if (x1 < 0 || x0 > stride - 1) return;
        if (x0 < 0) x0 = 0;
        if (x1 > stride - 1) x1 = stride - 1;

        var row = y * stride;
        for (int x = x0; x <= x1; x++) pixels[row + x] = color;
    }

    /// <summary>
    /// Insertion sort over crossings, carrying the winding directions with them. Insertion sort
    /// because the counts are small — a handful per scanline — and it needs no allocation, where
    /// <c>Array.Sort</c> with a paired key array would allocate a comparer per call.
    /// </summary>
    private void SortCrossings(int count)
    {
        for (int i = 1; i < count; i++)
        {
            var x = _crossings[i];
            var w = _winding[i];
            var j = i - 1;
            while (j >= 0 && _crossings[j] > x)
            {
                _crossings[j + 1] = _crossings[j];
                _winding[j + 1] = _winding[j];
                j--;
            }
            _crossings[j + 1] = x;
            _winding[j + 1] = w;
        }
    }

    private void EnsureCapacity(int needed)
    {
        if (_crossings.Length >= needed) return;
        var size = Math.Max(needed, _crossings.Length * 2);
        _crossings = new double[size];
        _winding = new int[size];
    }
}

/// <summary>A device-pixel coordinate. A struct so a filled outline is a flat array, not pointers.</summary>
public readonly struct ScreenPoint
{
    public readonly double X;
    public readonly double Y;
    public ScreenPoint(double x, double y) { X = x; Y = y; }
}
