using System;
using System.Collections.Generic;

namespace C2VGeometry.Rendering;

/// <summary>
/// Reduces any shape to plain polylines and filled loops, for a consumer that has no native form
/// for it.
///
/// <para>
/// The three exporters — SVG, PDF and DXF — each carry their own <c>switch (shape)</c> that maps a
/// type to that format's native construct: a <c>&lt;circle&gt;</c>, a <c>CIRCLE</c> entity, a PDF
/// arc. That is right, and worth keeping: flattening a circle to sixty-four chords in a DXF would
/// make it useless to open in a CAD package.
/// </para>
///
/// <para>
/// What was wrong is what those switches did with a type they did not recognise: nothing at all.
/// They fell off the end and the shape vanished from the export with no error, and because each
/// switch was written separately they had drifted to cover different subsets — a drawing could
/// export correctly to SVG and silently lose the same shapes in DXF. This gives each of them a
/// floor: whatever they cannot express natively still comes out as geometry.
/// </para>
/// </summary>
public sealed class PolylineFallbackSink : IPrimitiveSink
{
    /// <summary>Receives an open or closed run of world-space points.</summary>
    public Action<IReadOnlyList<VXYZ>, bool, PenSpec>? OnPolyline { get; set; }

    /// <summary>Receives a filled outline: first loop is the boundary, the rest are holes.</summary>
    public Action<IReadOnlyList<IReadOnlyList<VXYZ>>, PenSpec>? OnFilled { get; set; }

    /// <summary>Receives a bare point.</summary>
    public Action<VXYZ, PenSpec>? OnPoint { get; set; }

    /// <summary>Receives text, which has no polyline form here.</summary>
    public Action<VText>? OnText { get; set; }

    private PenSpec _pen;

    public TessellationHints Hints { get; } = new();

    /// <summary>Shapes even the tessellator could not reduce. Empty means the export was complete.</summary>
    public List<Shape> Unhandled { get; } = new();

    public void Reset() => Unhandled.Clear();

    public bool BeginShape(Shape shape, in PenSpec pen)
    {
        _pen = pen;
        return true;
    }

    public void EndShape() { }

    public void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed)
        => OnPolyline?.Invoke(points, closed, _pen);

    public void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule)
        => OnFilled?.Invoke(loops, _pen);

    public void EmitPoint(VXYZ point) => OnPoint?.Invoke(point, _pen);

    public void EmitText(VText text) => OnText?.Invoke(text);
}
