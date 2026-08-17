using System;
using System.Collections.Generic;

namespace DoodleSharp.Rendering.Raster;

/// <summary>
/// One frame's drawing, flattened to screen-space primitives, recorded once and replayed per tile.
///
/// <para>
/// <b>This exists because the obvious tiling design was wrong, measurably.</b> The first attempt had
/// each band walk the whole scene and let the rasterizer's clip reject what fell outside its rows —
/// no binning pass, no synchronisation. But clipping happens <i>after</i> tessellation, so a circle
/// was sampled into segments once per band: seventeen times over at 1080p. The managed backend came
/// out slower than the WPF path it was meant to replace, and collapsing to a single band was
/// immediately faster (mixed-cad pan 6.2 ms → 3.9 ms, zoom 196 ms → 104 ms).
/// </para>
///
/// <para>
/// So: tessellate once into this buffer, then replay it across bands in parallel. Replay is a
/// bounds check and a DDA walk — cheap enough that every band scanning every command is fine, where
/// every band re-tessellating every shape was not.
/// </para>
///
/// <para>
/// Commands keep their original order, and fills and strokes share one sequence, so a later shape's
/// fill correctly covers an earlier shape's stroke. Structure-of-arrays throughout, reused between
/// frames, so a steady-state frame allocates nothing.
/// </para>
/// </summary>
public sealed class RasterCommandBuffer
{
    private const byte CmdSegment = 0;
    private const byte CmdFill = 1;
    private const byte CmdPoint = 2;

    // Command sequence: what, and where its payload lives.
    private byte[] _kind = new byte[4096];
    private int[] _payload = new int[4096];
    private int _count;

    // Segment payload.
    private double[] _x0 = new double[4096];
    private double[] _y0 = new double[4096];
    private double[] _x1 = new double[4096];
    private double[] _y1 = new double[4096];
    private int[] _segColor = new int[4096];
    private int _segCount;

    // Fill payload: loops flattened into one point array, with per-fill loop ranges.
    private double[] _px = new double[4096];
    private double[] _py = new double[4096];
    private int _ptCount;

    private int[] _loopStart = new int[256];
    private int[] _loopLength = new int[256];
    private int _loopCount;

    private int[] _fillFirstLoop = new int[128];
    private int[] _fillLoopCount = new int[128];
    private int[] _fillColor = new int[128];
    private bool[] _fillEvenOdd = new bool[128];
    private int _fillCount;

    /// <summary>Segments recorded, for the frame metrics.</summary>
    public int SegmentCount => _segCount;

    public void Clear()
    {
        _count = 0;
        _segCount = 0;
        _ptCount = 0;
        _loopCount = 0;
        _fillCount = 0;
    }

    public void AddSegment(double x0, double y0, double x1, double y1, int color)
    {
        Grow(ref _x0, _segCount); Grow(ref _y0, _segCount);
        Grow(ref _x1, _segCount); Grow(ref _y1, _segCount);
        Grow(ref _segColor, _segCount);

        _x0[_segCount] = x0; _y0[_segCount] = y0;
        _x1[_segCount] = x1; _y1[_segCount] = y1;
        _segColor[_segCount] = color;

        Push(CmdSegment, _segCount);
        _segCount++;
    }

    public void AddPoint(double x, double y, int color)
    {
        Grow(ref _x0, _segCount); Grow(ref _y0, _segCount);
        Grow(ref _x1, _segCount); Grow(ref _y1, _segCount);
        Grow(ref _segColor, _segCount);

        _x0[_segCount] = x; _y0[_segCount] = y;
        _segColor[_segCount] = color;

        Push(CmdPoint, _segCount);
        _segCount++;
    }

    /// <summary>Begins a fill. Follow with <see cref="AddFillLoop"/> then <see cref="EndFill"/>.</summary>
    public void BeginFill(int color, bool evenOdd)
    {
        Grow(ref _fillFirstLoop, _fillCount); Grow(ref _fillLoopCount, _fillCount);
        Grow(ref _fillColor, _fillCount); Grow(ref _fillEvenOdd, _fillCount);

        _fillFirstLoop[_fillCount] = _loopCount;
        _fillLoopCount[_fillCount] = 0;
        _fillColor[_fillCount] = color;
        _fillEvenOdd[_fillCount] = evenOdd;
    }

    public void AddFillLoop(IReadOnlyList<ScreenPoint> points)
    {
        if (points.Count < 3) return;

        Grow(ref _loopStart, _loopCount); Grow(ref _loopLength, _loopCount);
        _loopStart[_loopCount] = _ptCount;
        _loopLength[_loopCount] = points.Count;
        _loopCount++;

        for (int i = 0; i < points.Count; i++)
        {
            Grow(ref _px, _ptCount); Grow(ref _py, _ptCount);
            _px[_ptCount] = points[i].X;
            _py[_ptCount] = points[i].Y;
            _ptCount++;
        }

        _fillLoopCount[_fillCount]++;
    }

    public void EndFill()
    {
        if (_fillLoopCount[_fillCount] == 0) return;   // nothing usable; drop it
        Push(CmdFill, _fillCount);
        _fillCount++;
    }

    /// <summary>
    /// Replays every command into one band's rows. Called concurrently, once per band — it only
    /// reads the buffer, and each band writes a disjoint run of pixels, so no locking is needed.
    /// </summary>
    public void Replay(int[] pixels, int stride, int height, int clipTop, int clipBottom,
                       PolygonFiller filler, List<IReadOnlyList<ScreenPoint>> loopScratch)
    {
        for (int i = 0; i < _count; i++)
        {
            var payload = _payload[i];

            switch (_kind[i])
            {
                case CmdSegment:
                {
                    // Cheap rejection before the clip: most commands miss most bands, and a band is
                    // a narrow slice of the frame.
                    var lo = Math.Min(_y0[payload], _y1[payload]);
                    var hi = Math.Max(_y0[payload], _y1[payload]);
                    if (hi < clipTop || lo > clipBottom) continue;

                    HairlineRasterizer.DrawLine(pixels, stride, height,
                        _x0[payload], _y0[payload], _x1[payload], _y1[payload],
                        _segColor[payload], clipTop, clipBottom);
                    break;
                }

                case CmdPoint:
                    HairlineRasterizer.DrawPoint(pixels, stride, height,
                        _x0[payload], _y0[payload], _segColor[payload], clipTop, clipBottom);
                    break;

                case CmdFill:
                {
                    loopScratch.Clear();
                    var first = _fillFirstLoop[payload];
                    var loops = _fillLoopCount[payload];
                    for (int l = 0; l < loops; l++)
                        loopScratch.Add(new LoopView(this, first + l));

                    filler.Fill(pixels, stride, height, loopScratch,
                                _fillColor[payload], _fillEvenOdd[payload], clipTop, clipBottom);
                    break;
                }
            }
        }
    }

    private void Push(byte kind, int payload)
    {
        Grow(ref _kind, _count);
        Grow(ref _payload, _count);
        _kind[_count] = kind;
        _payload[_count] = payload;
        _count++;
    }

    private static void Grow<T>(ref T[] array, int index)
    {
        if (index < array.Length) return;
        Array.Resize(ref array, Math.Max(index + 1, array.Length * 2));
    }

    /// <summary>
    /// A read-only view of one loop inside the flat point arrays, so the filler can consume it
    /// without the buffer having to materialise a list per loop per band.
    /// </summary>
    private sealed class LoopView : IReadOnlyList<ScreenPoint>
    {
        private readonly RasterCommandBuffer _owner;
        private readonly int _loop;

        public LoopView(RasterCommandBuffer owner, int loop) { _owner = owner; _loop = loop; }

        public int Count => _owner._loopLength[_loop];

        public ScreenPoint this[int index]
        {
            get
            {
                var at = _owner._loopStart[_loop] + index;
                return new ScreenPoint(_owner._px[at], _owner._py[at]);
            }
        }

        public IEnumerator<ScreenPoint> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
