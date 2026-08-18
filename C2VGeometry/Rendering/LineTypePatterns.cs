using System;

namespace C2VGeometry.Rendering;

/// <summary>
/// The one definition of what each <see cref="LineType"/> looks like.
///
/// <para>
/// There used to be two, and they disagreed. <c>RenderCanvas.GetDashStyle</c> expressed patterns as
/// multiples of the pen thickness (WPF's convention, e.g. Dashed = <c>{4, 2}</c>) while
/// <c>RasterPrimitiveSink.DashPatternFor</c> used device pixels (Dashed = <c>{8, 4}</c>) — and its
/// <c>_ =&gt; null</c> arm quietly rendered <see cref="LineType.Center"/>, <see cref="LineType.Phantom"/>
/// and <see cref="LineType.Hidden"/> as solid lines. Its comment claimed it mirrored the other table.
/// So the same dashed line looked different, or wasn't dashed at all, depending only on which backend
/// happened to draw the frame — the exact shape of the arrowhead defect in note 92.
/// </para>
///
/// <para>
/// <b>The canonical unit is device pixels at a line type scale of 1.</b> That unit was chosen
/// because the two old tables already agreed there: WPF's <c>{4, 2}</c> at the default
/// <c>LineWeight = 2</c> renders as 8 px and 4 px, which is exactly what the raster table used. So
/// a drawing with default line weights looks the same as it always did.
/// </para>
///
/// <para>
/// A consumer that measures dashes in pixels uses these numbers directly. A consumer whose dash
/// lengths are multiples of something else divides that something out — WPF multiplies the pattern
/// by the pen thickness, so <c>GetDashStyle</c> divides by thickness first. One consequence worth
/// knowing: dash lengths no longer vary with line weight on the WPF path. They used to, because the
/// pattern was a thickness multiple, so a heavy line got long dashes and a hairline got short ones.
/// Dash length is now a property of the line type alone, which is both what the raster backend
/// already did and what a CAD package does.
/// </para>
/// </summary>
public static class LineTypePatterns
{
    private static readonly double[] Empty = Array.Empty<double>();

    // Alternating run lengths — dash, gap, dash, gap… — in device pixels at scale 1.
    private static readonly double[] Dashed = { 8, 4 };
    private static readonly double[] Dotted = { 2, 4 };
    private static readonly double[] DashDot = { 8, 4, 2, 4 };
    private static readonly double[] DashDotDot = { 8, 4, 2, 4, 2, 4 };
    private static readonly double[] Center = { 12, 4, 4, 4 };
    private static readonly double[] Phantom = { 12, 4, 4, 4, 4, 4 };
    private static readonly double[] Hidden = { 4, 4 };

    /// <summary>
    /// The dash/gap run lengths for <paramref name="lineType"/>, in device pixels at scale 1, or an
    /// empty span for <see cref="LineType.Continuous"/>.
    ///
    /// <para>
    /// Returned as a <see cref="ReadOnlySpan{T}"/> over a shared array: this is called per shape per
    /// frame, so it must not allocate, and callers must not write to it. Scale and unit-convert into
    /// your own buffer.
    /// </para>
    /// </summary>
    public static ReadOnlySpan<double> DevicePixels(LineType lineType) => lineType switch
    {
        LineType.Dashed => Dashed,
        LineType.Dotted => Dotted,
        LineType.DashDot => DashDot,
        LineType.DashDotDot => DashDotDot,
        LineType.Center => Center,
        LineType.Phantom => Phantom,
        LineType.Hidden => Hidden,
        _ => Empty,
    };

    /// <summary>
    /// True when <paramref name="lineType"/> draws as a solid line — either because it is
    /// <see cref="LineType.Continuous"/>, or because the scale is degenerate. A non-positive scale
    /// would collapse every run to zero length, which rasterises as nothing at all rather than as a
    /// line, so it is treated as solid.
    /// </summary>
    public static bool IsSolid(LineType lineType, double scale)
        => DevicePixels(lineType).IsEmpty || scale <= 0 || !double.IsFinite(scale);

    /// <summary>Lower clamp on a line type scale; below this the pattern is treated as solid.</summary>
    public const double MinScale = 0.01;

    /// <summary>Upper clamp on a line type scale, so one shape cannot produce a runaway pattern.</summary>
    public const double MaxScale = 1000.0;

    /// <summary>Clamps a caller-supplied scale into the supported range.</summary>
    public static double ClampScale(double scale)
        => !double.IsFinite(scale) || scale <= 0 ? 1.0 : Math.Clamp(scale, MinScale, MaxScale);
}
