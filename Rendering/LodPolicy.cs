using System;

namespace DoodleSharp.Rendering;

/// <summary>How much of a shape is worth drawing at the current zoom.</summary>
public enum LodLevel
{
    /// <summary>Too small to register on screen at all. Draw nothing.</summary>
    Skip = 0,
    /// <summary>Smaller than a few pixels. Draw a single mark; its outline could not be told apart.</summary>
    Dot = 1,
    /// <summary>Full geometry.</summary>
    Full = 2,
}

/// <summary>
/// Decides how much detail a shape earns, from its size on screen.
///
/// <para>
/// This is the mechanism that makes "millions in the document" tractable, and it is worth being
/// precise about why. Culling answers <i>which</i> shapes are in view; at a wide zoom over a large
/// drawing the answer is "most of them", and they are all still drawn — a 60-unit building outline
/// at a zoom of 0.004 is a quarter of a pixel across, costs a full tessellation and a stroke, and
/// contributes one indistinguishable dot. Level of detail is what stops the frame cost from
/// tracking the document size once culling has stopped helping.
/// </para>
///
/// <para>
/// The thresholds are in device pixels and deliberately conservative. <see cref="RejectPixels"/> sits
/// just below one pixel rather than at, say, three: dropping a shape the user can still faintly make
/// out reads as a rendering bug, and the difference in saved work between 0.7px and 3px is small
/// because the population of shapes at any given screen size falls off steeply.
/// </para>
/// </summary>
public static class LodPolicy
{
    /// <summary>
    /// Below this many pixels across, a shape cannot produce a distinguishable mark, so nothing is
    /// drawn. Just under one pixel — anything that would light a pixel is still drawn.
    /// </summary>
    public const double RejectPixels = 0.7;

    /// <summary>
    /// Below this, a shape's outline is indistinguishable from a filled blob, so a single mark is
    /// drawn instead of its geometry. This is the level that pays for itself on dense scenes: a
    /// dot costs one rectangle, where the real shape might cost a 64-segment tessellation.
    /// </summary>
    public const double DotPixels = 2.5;

    /// <summary>
    /// Chooses a level from a shape's largest world-space extent and the current view scale.
    /// Non-finite extents (semi-infinite shapes such as <c>VRay</c>) always draw in full.
    /// </summary>
    public static LodLevel Classify(double worldExtent, double scale)
    {
        if (!double.IsFinite(worldExtent)) return LodLevel.Full;

        var pixels = worldExtent * scale;
        if (pixels < RejectPixels) return LodLevel.Skip;
        if (pixels < DotPixels) return LodLevel.Dot;
        return LodLevel.Full;
    }

    /// <summary>
    /// Segments to flatten a curve into, given its on-screen radius. Proportional to the square
    /// root because the error of a polygonal approximation falls off with the square of the segment
    /// count — so doubling the segments on a circle four times the size keeps the same smoothness,
    /// and a fixed count (the renderer's hard-coded 32) is simultaneously wasteful when zoomed out
    /// and visibly faceted when zoomed in.
    /// </summary>
    public static int SegmentsForRadius(double radiusPixels)
    {
        if (!double.IsFinite(radiusPixels) || radiusPixels <= 0) return MinSegments;
        return (int)Math.Clamp(Math.Sqrt(radiusPixels) * 2.5, MinSegments, MaxSegments);
    }

    public const int MinSegments = 6;
    public const int MaxSegments = 256;
}
