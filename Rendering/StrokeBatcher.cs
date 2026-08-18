using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using C2VGeometry;

namespace DoodleSharp.Rendering;

/// <summary>
/// Accumulates stroke-only shapes into one geometry per pen, so a thousand lines of the same colour
/// and weight cost one draw call instead of a thousand.
///
/// <para>
/// This is the single biggest lever left inside WPF. A frame of 100,000 visible shapes costs about
/// 88 ms drawn one at a time — roughly 880 ns each, which is the per-primitive overhead of
/// <c>DrawingVisual</c> plus MilCore, not the cost of the pixels. Technical drawings are made
/// overwhelmingly of lines and rectangles in a handful of pens, so bucketing collapses that count by
/// orders of magnitude.
/// </para>
///
/// <para>
/// <b>The z-order rule, which is what makes this safe.</b> Batching reorders whatever it batches, so
/// the batch is <i>flushed</i> the moment anything unbatchable appears. Ordering is therefore exact
/// with respect to filled shapes, text, hatches, regions and images; only consecutive runs of
/// stroke-only shapes are reordered among themselves, and unfilled hairlines do not occlude each
/// other in any way a user can see. Anything with a fill, an opacity, a rotation, a partial
/// <c>DrawFactor</c> or a dash pattern is excluded outright and drawn normally.
/// </para>
/// </summary>
public sealed class StrokeBatcher
{
    /// <summary>
    /// Below this many shapes in a run, batching costs more than it saves — building a
    /// <see cref="StreamGeometry"/> has its own overhead, and a handful of <c>DrawLine</c> calls is
    /// already cheap.
    /// </summary>
    public const int MinRunToBatch = 8;

    private readonly struct Segment
    {
        public readonly Point A;
        public readonly Point B;
        public Segment(Point a, Point b) { A = a; B = b; }
    }

    private readonly Dictionary<Pen, List<Segment>> _buckets = new();
    private readonly List<Pen> _order = new();
    private int _pending;

    /// <summary>Segments held but not yet drawn.</summary>
    public int PendingSegments => _pending;

    /// <summary>
    /// Whether a shape can join a batch. Deliberately conservative: every excluded case is one where
    /// batching would change what the user sees, and a shape drawn normally is merely slower, never
    /// wrong.
    /// </summary>
    public static bool CanBatch(Shape shape)
    {
        if (shape.Opacity < 1.0) return false;          // needs its own opacity layer
        if (shape.DrawFactor < 1.0) return false;       // partially drawn by an animation
        if (Math.Abs(shape.RotationAngle) > 1e-9) return false;  // needs its own transform
        if (shape.LineType != LineType.Continuous) return false; // dashes come from the pen

        return shape switch
        {
            VLine => true,
            VRectangle r => IsUnfilled(r.FillColor),
            VPolyline => true,
            VPolygon p => IsUnfilled(p.FillColor),
            _ => false,
        };
    }

    private static bool IsUnfilled(string? fill) =>
        string.IsNullOrEmpty(fill)
        || fill.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
        || fill.Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Holds one segment for a pen.
    ///
    /// <para>
    /// <b>Enrolment in <see cref="_order"/> keys off the bucket being empty, not off the bucket
    /// being new</b>, and the difference is the whole correctness of this class. The buckets are
    /// deliberately kept across flushes so a frame does not allocate a list per pen; only their
    /// contents are cleared. Enrolling only when the dictionary entry was created therefore worked
    /// exactly once per pen: from the second flush onward <c>TryGetValue</c> succeeded, the pen was
    /// never re-added, and <see cref="Flush"/> — which iterates <see cref="_order"/> — walked an
    /// empty list. Every batched stroke after the first flush was dropped without a trace, and
    /// because the drawing loop is what clears the segment lists, they also grew without bound.
    /// </para>
    /// </summary>
    public void Add(Pen pen, Point a, Point b)
    {
        if (!_buckets.TryGetValue(pen, out var list))
        {
            list = new List<Segment>(64);
            _buckets[pen] = list;
        }

        if (list.Count == 0) _order.Add(pen);

        list.Add(new Segment(a, b));
        _pending++;
    }

    /// <summary>
    /// Draws everything held, one geometry per pen, then empties. Pens are emitted in the order they
    /// were first seen, so the result is at least stable frame to frame rather than dependent on
    /// dictionary iteration order.
    /// </summary>
    public void Flush(DrawingContext dc)
    {
        if (_pending == 0) return;

        foreach (var pen in _order)
        {
            var segments = _buckets[pen];
            if (segments.Count == 0) continue;

            // Below the threshold, a StreamGeometry costs more to build and freeze than the handful
            // of DrawLine calls it replaces. Mixed drawings produce short runs constantly — a cell
            // of four lines then an arc — so without this, batching is a net loss on exactly the
            // content that needs it most.
            if (segments.Count < MinRunToBatch)
            {
                foreach (var seg in segments) dc.DrawLine(pen, seg.A, seg.B);
                segments.Clear();
                continue;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var seg in segments)
                {
                    ctx.BeginFigure(seg.A, false, false);
                    ctx.LineTo(seg.B, true, false);
                }
            }
            geometry.Freeze();

            dc.DrawGeometry(null, pen, geometry);
            segments.Clear();
        }

        _order.Clear();
        _pending = 0;
    }

    /// <summary>Drops everything held without drawing. For the error path only.</summary>
    public void Reset()
    {
        foreach (var list in _buckets.Values) list.Clear();
        _order.Clear();
        _pending = 0;
    }
}
