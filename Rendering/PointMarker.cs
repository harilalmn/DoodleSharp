namespace DoodleSharp.Rendering;

/// <summary>
/// How big a <see cref="C2VGeometry.VPoint"/> is on screen. A point has no size of its own — its
/// bounds are a single coordinate — so its marker is a fixed number of device pixels, and every
/// backend has to draw the same one (note 145). The vector path draws an ellipse of this radius with
/// a one-pixel pen centred on its edge; the managed rasterizer draws the matching disc.
/// </summary>
public static class PointMarker
{
    /// <summary>Radius of the default marker, a small solid dot, in device pixels.</summary>
    public const double DotRadius = 1.5;

    /// <summary>Radius of the marker when Settings → "Draw point as patch" is on, in device pixels.</summary>
    public const double PatchRadius = 5;
}
