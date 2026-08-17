using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DoodleSharp.Rendering;

/// <summary>
/// The stage a frame's time was spent in. Kept deliberately coarse: the point is to answer
/// "is this a culling problem, a tessellation problem, or a rasterization problem?" without
/// instrumenting so finely that the instrumentation shows up in the measurement.
/// </summary>
public enum FrameStage
{
    /// <summary>Deciding what is visible — spatial query plus the visibility walk.</summary>
    Cull = 0,
    /// <summary>Turning shapes into segments — curve flattening, hatch generation, LOD.</summary>
    Tessellate = 1,
    /// <summary>Turning segments into pixels or draw instructions.</summary>
    Raster = 2,
    /// <summary>Getting the result on screen — the bitmap copy, or WPF's composition handoff.</summary>
    Present = 3,
}

/// <summary>
/// Per-frame timings and counters for the render pipeline, plus percentile summaries.
///
/// <para>
/// This is the instrument every phase gate is measured with, so it has to be cheap enough to leave
/// on: a frame costs four <see cref="Stopwatch"/> reads, a handful of integer writes, and one slot
/// in a pre-allocated ring buffer. Nothing allocates while recording, because measuring allocation
/// is one of the things it is for.
/// </para>
///
/// <para>
/// Recording is off by default (<see cref="IsEnabled"/>) so the shipping app pays nothing until the
/// HUD or the benchmark harness turns it on.
/// </para>
/// </summary>
public sealed class FrameMetrics
{
    /// <summary>How many frames of history to keep. 600 = ten seconds at 60fps, and matches the
    /// benchmark camera-path length so a whole run fits without wrapping.</summary>
    public const int Capacity = 600;

    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    /// <summary>The instance the app and the benchmark both write to.</summary>
    public static FrameMetrics Instance { get; } = new();

    /// <summary>
    /// When false, every method on this class is a no-op beyond a branch. The HUD and the bench
    /// harness set it; normal use never pays.
    /// </summary>
    public bool IsEnabled { get; set; }

    // Ring buffer, pre-allocated. _count saturates at Capacity; _next wraps.
    private readonly double[] _frameMs = new double[Capacity];
    private readonly double[,] _stageMs = new double[Capacity, 4];
    private readonly int[] _visibleShapes = new int[Capacity];
    private readonly int[] _consideredShapes = new int[Capacity];
    private readonly long[] _segments = new long[Capacity];
    private readonly long[] _allocatedBytes = new long[Capacity];
    private readonly int[] _gen0 = new int[Capacity];

    private int _next;
    private int _count;

    private long _frameStartTicks;
    private long _stageStartTicks;
    private FrameStage _stage;

    private long _frameStartAllocated;
    private int _frameStartGen0;

    private readonly double[] _pendingStageMs = new double[4];
    private int _pendingVisible;
    private int _pendingConsidered;
    private long _pendingSegments;

    /// <summary>Frames recorded since the last <see cref="Reset"/>, capped at <see cref="Capacity"/>.</summary>
    public int Count => _count;

    /// <summary>Total frames begun since the last <see cref="Reset"/>, uncapped.</summary>
    public long TotalFrames { get; private set; }

    public void Reset()
    {
        _next = 0;
        _count = 0;
        TotalFrames = 0;
    }

    /// <summary>Starts timing a frame. Pairs with <see cref="EndFrame"/>.</summary>
    public void BeginFrame()
    {
        if (!IsEnabled) return;

        Array.Clear(_pendingStageMs, 0, _pendingStageMs.Length);
        _pendingVisible = 0;
        _pendingConsidered = 0;
        _pendingSegments = 0;

        _frameStartAllocated = GC.GetAllocatedBytesForCurrentThread();
        _frameStartGen0 = GC.CollectionCount(0);
        _frameStartTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Starts timing a stage. Stages do not nest; beginning one ends the previous. This is why
    /// stage times sum to less than the frame time rather than more — untimed work in between is
    /// visible as the gap, which is exactly the signal you want when hunting a regression.
    /// </summary>
    public void BeginStage(FrameStage stage)
    {
        if (!IsEnabled) return;

        var now = Stopwatch.GetTimestamp();
        if (_stageStartTicks != 0)
            _pendingStageMs[(int)_stage] += (now - _stageStartTicks) * TicksToMs;

        _stage = stage;
        _stageStartTicks = now;
    }

    /// <summary>Ends the current stage without starting another.</summary>
    public void EndStage()
    {
        if (!IsEnabled || _stageStartTicks == 0) return;

        _pendingStageMs[(int)_stage] += (Stopwatch.GetTimestamp() - _stageStartTicks) * TicksToMs;
        _stageStartTicks = 0;
    }

    /// <summary>
    /// Records how many shapes the frame drew and how many it had to look at to decide.
    /// The ratio is the culling gate: when they are equal, culling is doing nothing.
    /// </summary>
    public void RecordVisibility(int visible, int considered)
    {
        if (!IsEnabled) return;
        _pendingVisible = visible;
        _pendingConsidered = considered;
    }

    /// <summary>Records line segments submitted to the backend. The number LOD exists to bound.</summary>
    public void AddSegments(long count)
    {
        if (!IsEnabled) return;
        _pendingSegments += count;
    }

    public void EndFrame()
    {
        if (!IsEnabled) return;

        EndStage();
        var elapsedMs = (Stopwatch.GetTimestamp() - _frameStartTicks) * TicksToMs;

        var i = _next;
        _frameMs[i] = elapsedMs;
        for (int s = 0; s < 4; s++) _stageMs[i, s] = _pendingStageMs[s];
        _visibleShapes[i] = _pendingVisible;
        _consideredShapes[i] = _pendingConsidered;
        _segments[i] = _pendingSegments;
        _allocatedBytes[i] = GC.GetAllocatedBytesForCurrentThread() - _frameStartAllocated;
        _gen0[i] = GC.CollectionCount(0) - _frameStartGen0;

        _next = (_next + 1) % Capacity;
        if (_count < Capacity) _count++;
        TotalFrames++;
    }

    /// <summary>Snapshots the current history. Allocates — call it to report, never per frame.</summary>
    public FrameSummary Summarize()
    {
        if (_count == 0) return FrameSummary.Empty;

        var frames = new double[_count];
        Array.Copy(_frameMs, frames, _count);
        Array.Sort(frames);

        var stages = new double[4];
        for (int s = 0; s < 4; s++)
        {
            double total = 0;
            for (int i = 0; i < _count; i++) total += _stageMs[i, s];
            stages[s] = total / _count;
        }

        long allocated = 0, segments = 0;
        int gen0 = 0, visible = 0, considered = 0;
        for (int i = 0; i < _count; i++)
        {
            allocated += _allocatedBytes[i];
            segments += _segments[i];
            gen0 += _gen0[i];
            visible += _visibleShapes[i];
            considered += _consideredShapes[i];
        }

        return new FrameSummary
        {
            Frames = _count,
            P50Ms = Percentile(frames, 0.50),
            P95Ms = Percentile(frames, 0.95),
            P99Ms = Percentile(frames, 0.99),
            MeanMs = frames.Average(),
            MaxMs = frames[^1],
            CullMs = stages[(int)FrameStage.Cull],
            TessellateMs = stages[(int)FrameStage.Tessellate],
            RasterMs = stages[(int)FrameStage.Raster],
            PresentMs = stages[(int)FrameStage.Present],
            MeanVisibleShapes = visible / (double)_count,
            MeanConsideredShapes = considered / (double)_count,
            MeanSegments = segments / (double)_count,
            MeanAllocatedBytes = allocated / (double)_count,
            Gen0Collections = gen0,
        };
    }

    /// <summary>
    /// Nearest-rank percentile over a pre-sorted array. Nearest-rank rather than interpolated
    /// because a p99 that never equals an observed frame time is hard to argue with a profiler about.
    /// </summary>
    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        var rank = (int)Math.Ceiling(q * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }
}

/// <summary>An immutable snapshot of a run, suitable for a HUD line or a baseline JSON file.</summary>
public sealed class FrameSummary
{
    public static readonly FrameSummary Empty = new();

    public int Frames { get; init; }
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public double MeanMs { get; init; }
    public double MaxMs { get; init; }

    public double CullMs { get; init; }
    public double TessellateMs { get; init; }
    public double RasterMs { get; init; }
    public double PresentMs { get; init; }

    public double MeanVisibleShapes { get; init; }
    public double MeanConsideredShapes { get; init; }
    public double MeanSegments { get; init; }
    public double MeanAllocatedBytes { get; init; }
    public int Gen0Collections { get; init; }

    /// <summary>Frames per second implied by the p95 — the number the gates are written against.</summary>
    public double P95Fps => P95Ms > 0 ? 1000.0 / P95Ms : 0;

    /// <summary>
    /// Shapes examined per shape drawn. 1.0 means culling is perfect; equal to the document size
    /// means there is effectively no culling at all.
    /// </summary>
    public double CullRatio => MeanVisibleShapes > 0 ? MeanConsideredShapes / MeanVisibleShapes : 0;

    public string ToHudLine() =>
        $"p50 {P50Ms,6:F2}ms  p95 {P95Ms,6:F2}ms ({P95Fps,5:F1} fps)  " +
        $"cull {CullMs,5:F2}  tess {TessellateMs,5:F2}  rast {RasterMs,5:F2}  pres {PresentMs,5:F2}  " +
        $"vis {MeanVisibleShapes,8:N0}/{MeanConsideredShapes,-9:N0}  seg {MeanSegments,9:N0}  " +
        $"alloc {MeanAllocatedBytes / 1024.0,7:F1}KB  gen0 {Gen0Collections}";
}
