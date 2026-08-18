using System.Windows;
using System.Windows.Media;
using DoodleSharp.Rendering;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The batcher holds segments between draw calls, so its bookkeeping is the one place a shape can
/// be culled in, tessellated, and then silently never drawn. These tests exist because it shipped
/// doing exactly that: every batched stroke after the first flush disappeared, and the segments
/// piled up in the buckets frame after frame.
/// </summary>
public class StrokeBatcherTests
{
    private static Pen MakePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    /// <summary>Drawing instructions actually recorded into a visual.</summary>
    private static int Draw(StrokeBatcher batcher, DrawingVisual visual)
    {
        using (var dc = visual.RenderOpen())
        {
            batcher.Flush(dc);
        }

        return visual.Drawing is DrawingGroup group ? group.Children.Count
             : visual.Drawing != null ? 1
             : 0;
    }

    [Fact]
    public void EveryFrameDrawsItsBatch_NotOnlyTheFirst()
    {
        var batcher = new StrokeBatcher();
        var pen = MakePen(Colors.Cyan, 2.0);
        var visual = new DrawingVisual();

        // Well above MinRunToBatch, so each frame takes the single-geometry path.
        for (int frame = 0; frame < 3; frame++)
        {
            for (int i = 0; i < 20; i++)
                batcher.Add(pen, new Point(0, i), new Point(100, i));

            Assert.Equal(20, batcher.PendingSegments);
            Assert.Equal(1, Draw(batcher, visual));
        }
    }

    [Fact]
    public void ASecondRunInTheSameFrameIsDrawnToo()
    {
        // The render loop flushes the moment an unbatchable shape appears, so a single frame
        // routinely flushes several times. Only the first run used to survive.
        var batcher = new StrokeBatcher();
        var pen = MakePen(Colors.Cyan, 2.0);
        var visual = new DrawingVisual();

        for (int run = 0; run < 3; run++)
        {
            for (int i = 0; i < 12; i++)
                batcher.Add(pen, new Point(0, i), new Point(50, i));

            Assert.Equal(1, Draw(batcher, visual));
        }
    }

    [Fact]
    public void ShortRunsAreDrawnEveryFrameAsWell()
    {
        // Below MinRunToBatch the pen still has to be enrolled, or the individual DrawLine path
        // is never reached either.
        var batcher = new StrokeBatcher();
        var pen = MakePen(Colors.Cyan, 2.0);
        var visual = new DrawingVisual();

        for (int frame = 0; frame < 3; frame++)
        {
            for (int i = 0; i < 4; i++)
                batcher.Add(pen, new Point(0, i), new Point(30, i));

            Assert.Equal(4, Draw(batcher, visual));
        }
    }

    [Fact]
    public void FlushLeavesNothingHeld()
    {
        // Segments are cleared by the drawing loop, so a pen that never gets drawn also never gets
        // emptied — the unbounded leak that came with the dropped strokes.
        var batcher = new StrokeBatcher();
        var cyan = MakePen(Colors.Cyan, 2.0);
        var red = MakePen(Colors.Red, 1.0);
        var visual = new DrawingVisual();

        for (int frame = 0; frame < 5; frame++)
        {
            for (int i = 0; i < 20; i++) batcher.Add(cyan, new Point(0, i), new Point(100, i));
            for (int i = 0; i < 3; i++) batcher.Add(red, new Point(0, i), new Point(10, i));

            // One geometry for the cyan run, three DrawLine calls for the short red one.
            Assert.Equal(4, Draw(batcher, visual));
            Assert.Equal(0, batcher.PendingSegments);
        }
    }

    [Fact]
    public void PensAreDrawnInTheOrderTheirFirstSegmentArrived()
    {
        var batcher = new StrokeBatcher();
        var cyan = MakePen(Colors.Cyan, 2.0);
        var red = MakePen(Colors.Red, 2.0);
        var visual = new DrawingVisual();

        // Prime both buckets, then reverse which pen is seen first on the next frame.
        batcher.Add(cyan, new Point(0, 0), new Point(1, 1));
        batcher.Add(red, new Point(0, 0), new Point(1, 1));
        Draw(batcher, visual);

        batcher.Add(red, new Point(0, 0), new Point(1, 1));
        batcher.Add(cyan, new Point(0, 0), new Point(1, 1));

        using (var dc = visual.RenderOpen())
        {
            batcher.Flush(dc);
        }

        var group = Assert.IsType<DrawingGroup>(visual.Drawing);
        var first = Assert.IsType<GeometryDrawing>(group.Children[0]);
        Assert.Equal(Colors.Red, ((SolidColorBrush)first.Pen.Brush).Color);
    }

    [Fact]
    public void ResetDropsEverythingWithoutDrawing()
    {
        var batcher = new StrokeBatcher();
        var pen = MakePen(Colors.Cyan, 2.0);
        var visual = new DrawingVisual();

        for (int i = 0; i < 20; i++) batcher.Add(pen, new Point(0, i), new Point(100, i));
        batcher.Reset();

        Assert.Equal(0, batcher.PendingSegments);
        Assert.Equal(0, Draw(batcher, visual));

        // And the batcher still works afterwards.
        for (int i = 0; i < 20; i++) batcher.Add(pen, new Point(0, i), new Point(100, i));
        Assert.Equal(1, Draw(batcher, visual));
    }
}
