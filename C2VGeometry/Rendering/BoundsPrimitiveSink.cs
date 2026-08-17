using System;
using System.Collections.Generic;

namespace C2VGeometry.Rendering;

/// <summary>
/// Accumulates the bounding box of whatever it is given.
///
/// <para>
/// Zoom-to-extents used to carry its own <c>switch (shape)</c> — a fourth copy inside the renderer
/// alone — which meant a shape type the switch had never heard of was simply left out of the extents
/// and could sit off screen after a zoom-to-fit. Measuring through the tessellator means it sees
/// exactly what the renderer draws, by construction.
/// </para>
/// </summary>
public sealed class BoundsPrimitiveSink : IPrimitiveSink
{
    private double _minX = double.MaxValue, _minY = double.MaxValue;
    private double _maxX = double.MinValue, _maxY = double.MinValue;
    private double _offsetX, _offsetY;

    public TessellationHints Hints { get; } = new();

    public bool HasBounds => _minX <= _maxX;

    public double MinX => _minX;
    public double MinY => _minY;
    public double MaxX => _maxX;
    public double MaxY => _maxY;

    public void Reset()
    {
        _minX = _minY = double.MaxValue;
        _maxX = _maxY = double.MinValue;
    }

    public bool BeginShape(Shape shape, in PenSpec pen)
    {
        // Animation offsets move a shape on screen without touching its geometry, so extents that
        // ignored them would frame the wrong place mid-animation.
        _offsetX = shape.OffsetX;
        _offsetY = shape.OffsetY;
        return true;
    }

    public void EndShape() { }

    public void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed)
    {
        for (int i = 0; i < points.Count; i++) Include(points[i]);
    }

    public void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule)
    {
        for (int l = 0; l < loops.Count; l++)
        {
            var loop = loops[l];
            for (int i = 0; i < loop.Count; i++) Include(loop[i]);
        }
    }

    public void EmitPoint(VXYZ point) => Include(point);

    public void EmitText(VText text)
    {
        // Text has no outline here; its own bounds are the best available answer.
        try
        {
            var b = text.GetBounds();
            Include(b.Min);
            Include(b.Max);
        }
        catch { }
    }

    /// <summary>Folds a shape the tessellator declined into the extents, using its own bounds.</summary>
    public void IncludeBounds(Shape shape)
    {
        try
        {
            var b = shape.GetBounds();
            if (b?.Min == null || b.Max == null) return;
            _offsetX = shape.OffsetX;
            _offsetY = shape.OffsetY;
            Include(b.Min);
            Include(b.Max);
        }
        catch { }
    }

    private void Include(VXYZ p)
    {
        var x = p.X + _offsetX;
        var y = p.Y + _offsetY;
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        if (x < _minX) _minX = x;
        if (x > _maxX) _maxX = x;
        if (y < _minY) _minY = y;
        if (y > _maxY) _maxY = y;
    }
}
