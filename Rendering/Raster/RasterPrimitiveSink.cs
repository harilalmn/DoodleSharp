using System;
using System.Collections.Generic;
using C2VGeometry;
using C2VGeometry.Rendering;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Turns tessellated primitives into pixels.
///
/// <para>
/// It records into a <see cref="RasterCommandBuffer"/> rather than writing pixels directly, so the
/// scene is tessellated exactly once and the resulting primitives are replayed across tiles. The
/// first design had each tile tessellate the scene itself, which made the backend slower than the
/// WPF path it replaces — see the buffer's remarks.
/// </para>
///
/// <para>
/// <b>Dashes are expanded here, in screen space.</b> Not because it is convenient but because it is
/// the requirement: dash lengths are specified in device pixels and must stay fixed as you zoom, so
/// they can only be measured once the geometry is in screen coordinates. It also keeps the
/// rasterizer's inner loop to solid segments, with no pattern state to carry.
/// </para>
/// </summary>
public sealed class RasterPrimitiveSink : IPrimitiveSink
{
    private readonly List<ScreenPoint> _loopScratch = new(256);

    private RasterCommandBuffer _buffer = null!;

    private Func<double, double, (double x, double y)> _worldToScreen = (x, y) => (x, y);

    /// <summary>
    /// A dashed segment longer than this is drawn solid. At high zoom a line's endpoints can be
    /// millions of pixels off-screen, and stepping the pattern along its full length would walk
    /// millions of iterations to produce a few visible dashes.
    /// </summary>
    private const double MaxDashedLengthPixels = 20_000;

    private int _strokeColor;
    private int _fillColor;
    private bool _hasFill;
    private double[]? _dashPattern;

    public TessellationHints Hints { get; } = new();

    /// <summary>Shapes this sink declined, for the caller to draw another way.</summary>
    public List<Shape> Deferred { get; } = new();

    /// <summary>Segments actually submitted, for the frame metrics.</summary>
    public long SegmentsSubmitted { get; private set; }

    public void Begin(RasterCommandBuffer buffer, double scale,
                      Func<double, double, (double, double)> worldToScreen)
    {
        _buffer = buffer;
        _worldToScreen = worldToScreen;
        Hints.Scale = scale;
        Deferred.Clear();
        SegmentsSubmitted = 0;
    }

    public bool BeginShape(Shape shape, in PenSpec pen)
    {
        // Text is deferred to WPF rather than drawn from glyph outlines. Outline extraction loses
        // hinting and costs far more than the text layer it would replace, and text is thousands of
        // labels, never the hundred thousand primitives this path exists for.
        if (shape is VText)
        {
            Deferred.Add(shape);
            return false;
        }

        _strokeColor = ColorTable.WithOpacity(ColorTable.Resolve(pen.Color), pen.Opacity);
        _fillColor = ColorTable.WithOpacity(ColorTable.Resolve(pen.FillColor), pen.Opacity);
        _hasFill = pen.HasFill && !ColorTable.IsFullyTransparent(_fillColor);
        _dashPattern = DashPatternFor(pen.LineType, pen.LineTypeScale);

        return true;
    }

    public void EndShape() { }

    public void EmitPoint(VXYZ point)
    {
        var (x, y) = _worldToScreen(point.X, point.Y);
        _buffer.AddPoint(x, y, _strokeColor);
        SegmentsSubmitted++;
    }

    public void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed)
    {
        if (points == null || points.Count < 2) return;

        var count = points.Count;
        var last = closed ? count : count - 1;

        for (int i = 0; i < last; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % count];

            var (x0, y0) = _worldToScreen(a.X, a.Y);
            var (x1, y1) = _worldToScreen(b.X, b.Y);

            if (_dashPattern == null)
            {
                _buffer.AddSegment(x0, y0, x1, y1, _strokeColor);
                SegmentsSubmitted++;
            }
            else
            {
                DrawDashed(x0, y0, x1, y1);
            }
        }
    }

    public void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule)
    {
        if (!_hasFill || loops.Count == 0) return;

        _buffer.BeginFill(_fillColor, rule == FillRule.EvenOdd);

        for (int li = 0; li < loops.Count; li++)
        {
            var loop = loops[li];
            if (loop.Count < 3) continue;

            _loopScratch.Clear();
            for (int i = 0; i < loop.Count; i++)
            {
                var (x, y) = _worldToScreen(loop[i].X, loop[i].Y);
                _loopScratch.Add(new ScreenPoint(x, y));
            }
            _buffer.AddFillLoop(_loopScratch);
        }

        _buffer.EndFill();
    }

    public void EmitText(VText text) => Deferred.Add(text);

    /// <summary>
    /// Walks a segment turning the dash pattern on and off. Lengths are already in device pixels,
    /// so the pattern is fixed on screen regardless of zoom.
    /// </summary>
    private void DrawDashed(double x0, double y0, double x1, double y1)
    {
        var pattern = _dashPattern!;
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = Math.Sqrt(dx * dx + dy * dy);

        if (!double.IsFinite(length) || length <= 0) return;

        // A segment far longer than the viewport would otherwise be walked dash by dash across its
        // entire off-screen length. Solid is the right answer well before that becomes visible.
        if (length > MaxDashedLengthPixels)
        {
            _buffer.AddSegment(x0, y0, x1, y1, _strokeColor);
            SegmentsSubmitted++;
            return;
        }

        var ux = dx / length;
        var uy = dy / length;

        double travelled = 0;
        var index = 0;
        var on = true;

        while (travelled < length)
        {
            var run = Math.Min(pattern[index % pattern.Length], length - travelled);
            if (run <= 0) { index++; on = !on; continue; }

            if (on)
            {
                _buffer.AddSegment(
                    x0 + ux * travelled, y0 + uy * travelled,
                    x0 + ux * (travelled + run), y0 + uy * (travelled + run),
                    _strokeColor);
                SegmentsSubmitted++;
            }

            travelled += run;
            index++;
            on = !on;
        }
    }

    /// <summary>
    /// Dash runs in device pixels, or null for a solid line.
    ///
    /// <para>
    /// The pattern comes from <see cref="LineTypePatterns"/>, whose canonical unit is device pixels —
    /// so this consumes it directly. It used to be a second, hand-written table that claimed to
    /// mirror the legacy renderer's and did not: the numbers differed, and Center, Phantom and Hidden
    /// fell through to <c>null</c> and drew solid.
    /// </para>
    /// </summary>
    private static double[]? DashPatternFor(LineType lineType, double scale)
    {
        if (LineTypePatterns.IsSolid(lineType, scale)) return null;

        var pattern = LineTypePatterns.DevicePixels(lineType);
        var s = LineTypePatterns.ClampScale(scale);

        var runs = new double[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
            runs[i] = pattern[i] * s;

        return runs;
    }

}
