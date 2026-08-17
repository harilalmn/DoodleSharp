using System;
using System.Collections.Generic;
using System.Windows.Media;
using C2VGeometry;
using C2VGeometry.Rendering;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// Rasterises the scene into a bitmap, and reports what it could not draw.
///
/// <para>
/// This is the answer to the measured WPF ceiling. Drawn through <c>DrawingVisual</c>, a frame of
/// 100,000 visible shapes costs about 88 ms — roughly 880 ns per shape, which is per-primitive
/// overhead rather than the cost of the pixels, and is structural: WPF has no cosmetic pen, so
/// zoom-invariant hairlines force <c>Thickness = 1/scale</c> every frame and invalidate the CPU
/// stroke tessellation of every cached geometry. Writing pixels directly sidesteps all of it.
/// </para>
///
/// <para>
/// Two passes. The first tessellates every visible shape into a <see cref="RasterCommandBuffer"/>,
/// once, single-threaded. The second replays that buffer across horizontal bands in parallel, each
/// band clipping to its own rows so no two threads touch the same pixel. Doing it the other way
/// round — letting each band tessellate and rely on clipping — was measurably slower than the
/// renderer it replaces, because tessellation happens before clipping and so ran once per band.
/// </para>
///
/// <para>
/// <b>It is deliberately partial.</b> Shapes whose drawing rules live in the host — text,
/// dimensions, arrows, grids, infinite construction lines — are collected in <see cref="Deferred"/>
/// and drawn by the existing WPF path on top. That is a two-layer split: hairline geometry
/// underneath, annotation above. For technical drawings that ordering is what you want anyway, and
/// it lets the fast path be complete for the shapes that make up the bulk without reimplementing
/// dimension layout first.
/// </para>
/// </summary>
public sealed class ManagedRasterBackend
{
    private readonly RasterSurface _surface = new();
    private readonly RasterCommandBuffer _commands = new();
    private readonly ShapeTessellator _tessellator = new();
    private readonly RasterPrimitiveSink _sink = new();

    // One filler and scratch list per band. Both hold state, so bands cannot share them; they are
    // kept across frames so their buffers stay warm.
    private readonly List<PolygonFiller> _fillers = new();
    private readonly List<List<IReadOnlyList<ScreenPoint>>> _loopScratch = new();
    private readonly object _perBandLock = new();

    /// <summary>Shapes the raster path declined; draw these with the WPF renderer, in order.</summary>
    public IReadOnlyList<Shape> Deferred => _sink.Deferred;

    /// <summary>Line segments submitted last frame.</summary>
    public long SegmentsSubmitted { get; private set; }

    public ImageSource? Output => _surface.Bitmap;

    /// <summary>
    /// Draws the visible shapes. <paramref name="visible"/> must already be in draw order — the
    /// scene index's visibility walk yields it that way, so nothing is sorted here.
    /// </summary>
    public bool Render(int width, int height, int backgroundBgra,
                       IReadOnlyList<Shape> visible, double scale,
                       Func<double, double, (double x, double y)> worldToScreen)
    {
        if (!_surface.Resize(width, height)) return false;

        _surface.Clear(backgroundBgra);
        _commands.Clear();

        // Pass 1 — tessellate once.
        _sink.Begin(_commands, scale, worldToScreen);
        for (int i = 0; i < visible.Count; i++)
        {
            // The return value is not optional. Shapes the tessellator declines — dimensions,
            // arrows, grids, infinite construction lines — must be handed back for the vector layer
            // to draw; ignoring it makes them silently disappear, which is precisely what happened
            // the first time and cost every dimension in the scene.
            if (!_tessellator.Tessellate(visible[i], _sink))
                _sink.Deferred.Add(visible[i]);
        }

        SegmentsSubmitted = _sink.SegmentsSubmitted;

        // Pass 2 — replay across bands.
        _surface.RenderTiled((top, bottom) =>
        {
            var band = top / RasterSurface.TileRows;
            var (filler, scratch) = GetPerBand(band);
            _commands.Replay(_surface.Pixels, _surface.Width, _surface.Height,
                             top, bottom, filler, scratch);
        });

        _surface.Present();
        return true;
    }

    private (PolygonFiller, List<IReadOnlyList<ScreenPoint>>) GetPerBand(int band)
    {
        lock (_perBandLock)
        {
            while (_fillers.Count <= band)
            {
                _fillers.Add(new PolygonFiller());
                _loopScratch.Add(new List<IReadOnlyList<ScreenPoint>>(8));
            }
            return (_fillers[band], _loopScratch[band]);
        }
    }
}
