using System;
using System.Collections.Generic;

namespace C2VGeometry.Rendering;

/// <summary>How a filled outline decides what is inside it.</summary>
public enum FillRule
{
    EvenOdd = 0,
    NonZero = 1,
}

/// <summary>
/// Everything a renderer needs to know about how one shape is painted, lifted out of the shape so a
/// sink doesn't have to reach back into <see cref="Shape"/> for it.
/// </summary>
public readonly struct PenSpec
{
    public readonly string Color;
    public readonly string FillColor;
    public readonly double LineWeight;
    public readonly LineType LineType;
    public readonly double LineTypeScale;
    public readonly double Opacity;

    public PenSpec(string color, string fillColor, double lineWeight,
                   LineType lineType, double lineTypeScale, double opacity)
    {
        Color = color;
        FillColor = fillColor;
        LineWeight = lineWeight;
        LineType = lineType;
        LineTypeScale = lineTypeScale;
        Opacity = opacity;
    }

    public static PenSpec From(Shape shape) => new(
        shape.Color, shape.FillColor, shape.LineWeight,
        shape.LineType, shape.LineTypeScale, shape.Opacity);

    /// <summary>Whether the fill is genuinely absent, rather than merely a colour that looks like it.</summary>
    public bool HasFill =>
        !string.IsNullOrEmpty(FillColor)
        && !FillColor.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
        && !FillColor.Equals("None", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// How finely curves should be flattened, and at what view scale.
/// </summary>
public sealed class TessellationHints
{
    /// <summary>
    /// <b>Screen pixels per world unit</b> — the view's zoom, the same quantity as
    /// <c>MouseInfo.Scale</c>. A world size <i>multiplied</i> by this gives a size on screen, which is
    /// exactly how <c>ShapeTessellator</c> uses it (<c>radiusPx = radius * Scale</c>).
    /// <para>
    /// This comment used to say "world units per device pixel", which is the reciprocal, and the
    /// error had already propagated into the F1 Help text before anyone noticed the tessellator
    /// multiplying rather than dividing. State the direction, not just the two units.
    /// </para>
    /// <para>
    /// Curve segment counts are chosen from a shape's size in <i>pixels</i>, not world units — a
    /// circle of radius 1 needs a different number of segments depending entirely on how far you
    /// have zoomed in.
    /// </para>
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// Set by sinks that can express a circle as a circle — DXF, SVG, PDF. When true, the
    /// tessellator offers native forms first and only flattens what the sink declines.
    /// </summary>
    public bool PreferNative { get; set; }
}

/// <summary>
/// Where <see cref="ShapeTessellator"/> sends the primitives it produces.
///
/// <para>
/// This exists to collapse a duplication that had grown to six copies: the geometry-to-graphics
/// <c>switch (shape)</c> was written separately in the renderer's dispatch, its group-child
/// recursion, its drawing-tool preview, its zoom-extents, and again in each of the SVG, PDF and DXF
/// exporters. Adding a shape type meant six edits, and the exporters had silently fallen behind —
/// each one drops types the renderer handles.
/// </para>
///
/// <para>
/// <see cref="TryEmitNative"/> is what lets one tessellator serve both kinds of consumer. A
/// rasterizer wants everything as line segments; DXF wants a circle to stay a <c>CIRCLE</c> entity
/// rather than becoming sixty-four chords. A sink returns true from it for whatever it can express
/// directly, and inherits flattening for everything else.
/// </para>
/// </summary>
public interface IPrimitiveSink
{
    TessellationHints Hints { get; }

    /// <summary>
    /// Called before a shape's primitives. Returning false skips the shape entirely — how a sink
    /// declines shape types it cannot handle, so the caller can fall back to another renderer.
    /// </summary>
    bool BeginShape(Shape shape, in PenSpec pen);

    void EndShape();

    /// <summary>An open or closed run of connected points, stroked.</summary>
    void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed);

    /// <summary>
    /// A filled area: the first loop is the outer boundary, any others are holes.
    /// </summary>
    void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule);

    /// <summary>A zero-area mark.</summary>
    void EmitPoint(VXYZ point);

    /// <summary>
    /// Text, unflattened. Glyph outlines are available through
    /// <see cref="VText.GlyphOutlineProvider"/>, but a sink with a real text stack should use its
    /// own — the outlines lose hinting and cost far more.
    /// </summary>
    void EmitText(VText text);

    /// <summary>
    /// Offered before flattening when <see cref="TessellationHints.PreferNative"/> is set. Return
    /// true to claim the shape and suppress tessellation.
    /// </summary>
    bool TryEmitNative(Shape shape, in PenSpec pen) => false;
}
