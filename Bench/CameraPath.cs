using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Bench;

/// <summary>
/// Scripted camera motion. Deterministic and frame-indexed rather than time-based, so a slow run
/// and a fast run cover exactly the same views and the numbers stay comparable.
/// </summary>
public sealed class CameraPath
{
    public string Name { get; }
    public int FrameCount { get; }
    private readonly Action<ViewportTransform, int, BoundingBox> _step;

    private CameraPath(string name, int frames, Action<ViewportTransform, int, BoundingBox> step)
    {
        Name = name;
        FrameCount = frames;
        _step = step;
    }

    public void Apply(ViewportTransform viewport, int frame, BoundingBox worldBounds)
        => _step(viewport, frame, worldBounds);

    public static readonly CameraPath[] All =
    {
        PanAcross,
        ZoomThroughDecades,
        IdleHover,
    };

    /// <summary>
    /// Sweeps left to right at a working zoom. The everyday case, and the one the old renderer was
    /// worst at: every mouse-move while panning rebuilt the entire scene.
    /// </summary>
    public static CameraPath PanAcross => new("pan-across", 600, (vp, frame, bounds) =>
    {
        // A zoom that puts a meaningful slice on screen rather than the whole drawing — panning
        // while zoomed all the way out measures nothing but the cull.
        var span = Math.Max(bounds.Width, 1e-6);
        vp.SetZoom(vp.ViewportWidth / (span * 0.05));

        var t = frame / 599.0;
        var worldX = bounds.Min.X + span * (0.05 + 0.90 * t);
        var worldY = bounds.Min.Y + Math.Max(bounds.Height, 1e-6) * 0.5;
        vp.CenterOnWorldPoint(worldX, worldY);
    });

    /// <summary>
    /// Zooms from the whole drawing down to a single detail and back. Sweeping the scale is what
    /// exercises level-of-detail — at the wide end almost every shape is sub-pixel, at the tight end
    /// a handful of curves need real tessellation.
    /// </summary>
    public static CameraPath ZoomThroughDecades => new("zoom-decades", 600, (vp, frame, bounds) =>
    {
        var centreX = (bounds.Min.X + bounds.Max.X) * 0.5;
        var centreY = (bounds.Min.Y + bounds.Max.Y) * 0.5;

        var span = Math.Max(Math.Max(bounds.Width, bounds.Height), 1e-6);
        var fitZoom = vp.ViewportWidth / span;

        // Triangle wave over five decades: in for the first half, out for the second, so the run
        // measures both directions without a discontinuity at the turn.
        var t = frame / 599.0;
        var tri = t < 0.5 ? t * 2 : (1 - t) * 2;
        vp.SetZoom(fitZoom * Math.Pow(10, 5 * tri));
        vp.CenterOnWorldPoint(centreX, centreY);
    });

    /// <summary>
    /// Holds still. Nothing about the scene changes, so every frame this costs anything at all is a
    /// frame spent redoing work that was already done — which is precisely what the static/dynamic
    /// layer split is meant to eliminate.
    /// </summary>
    public static CameraPath IdleHover => new("idle-hover", 300, (vp, frame, bounds) =>
    {
        if (frame > 0) return;

        var span = Math.Max(Math.Max(bounds.Width, bounds.Height), 1e-6);
        vp.SetZoom(vp.ViewportWidth / (span * 0.1));
        vp.CenterOnWorldPoint(
            (bounds.Min.X + bounds.Max.X) * 0.5,
            (bounds.Min.Y + bounds.Max.Y) * 0.5);
    });
}
