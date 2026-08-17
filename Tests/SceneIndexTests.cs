using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using C2VGeometry;
using DoodleSharp.Rendering;

namespace DoodleSharp.Tests;

/// <summary>
/// Correctness of viewport culling. A false negative here is the worst class of rendering bug:
/// the shape is simply absent, with nothing to indicate why. So these tests lean on
/// cross-checking every query against a brute-force scan rather than on hand-picked expectations.
/// </summary>
[Collection("CanvasState")]
public class SceneIndexTests : IDisposable
{
    public SceneIndexTests() => Shape.DefaultRegistry = null;
    public void Dispose() => Shape.DefaultRegistry = null;

    private static List<IDrawable> Grid(int cols, int rows, double spacing, double size = 1.0)
    {
        var shapes = new List<IDrawable>(cols * rows);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                shapes.Add(new VRectangle(new VXYZ(x * spacing, y * spacing), size, size));
        return shapes;
    }

    /// <summary>Ground truth: an AABB overlap test over every shape, in order.</summary>
    private static List<int> BruteForce(IReadOnlyList<IDrawable> shapes,
                                        double minX, double minY, double maxX, double maxY)
    {
        var hits = new List<int>();
        for (int i = 0; i < shapes.Count; i++)
        {
            var b = ((Shape)shapes[i]).GetBounds();
            if (b.Min.X > maxX || b.Max.X < minX) continue;
            if (b.Min.Y > maxY || b.Max.Y < minY) continue;
            hits.Add(i);
        }
        return hits;
    }

    private static List<int> Ascending(SceneIndex index)
    {
        var slots = new List<int>();
        foreach (var slot in index.Visible) slots.Add(slot);
        return slots;
    }

    [Fact]
    public void EmptyScene_QueryFindsNothing()
    {
        var index = new SceneIndex();
        index.Rebuild(Array.Empty<IDrawable>());
        index.Query(-100, -100, 100, 100);

        Assert.Equal(0, index.VisibleCount);
        Assert.Empty(Ascending(index));
    }

    [Fact]
    public void MatchesBruteForce_AcrossManyWindows()
    {
        var shapes = Grid(cols: 60, rows: 40, spacing: 10);
        var index = new SceneIndex();
        index.Rebuild(shapes);

        // Deterministic sweep of windows: tiny, medium, huge, off-scene, straddling the origin.
        var windows = new (double minX, double minY, double maxX, double maxY)[]
        {
            (0, 0, 5, 5),
            (-50, -50, 50, 50),
            (95, 95, 205, 205),
            (-1000, -1000, 1000, 1000),
            (5000, 5000, 6000, 6000),
            (-30, 150, 120, 260),
            (99.5, 99.5, 100.5, 100.5),
        };

        foreach (var w in windows)
        {
            index.Query(w.minX, w.minY, w.maxX, w.maxY);
            var expected = BruteForce(shapes, w.minX, w.minY, w.maxX, w.maxY);
            Assert.Equal(expected, Ascending(index));
        }
    }

    [Fact]
    public void ShapesStraddlingCellBoundaries_AreStillFound()
    {
        // Shapes deliberately sized and placed so their lower-left corner sits in one cell while
        // their body reaches into the next — the case the query widens its cell range to catch.
        var shapes = new List<IDrawable>();
        for (int i = 0; i < 400; i++)
            shapes.Add(new VRectangle(new VXYZ(i * 7.3 - 500, (i % 20) * 11.7 - 100), 6.9, 9.1));

        var index = new SceneIndex();
        index.Rebuild(shapes);

        for (double x = -520; x < 520; x += 37)
        {
            index.Query(x, -120, x + 25, 140);
            var expected = BruteForce(shapes, x, -120, x + 25, 140);
            Assert.Equal(expected, Ascending(index));
        }
    }

    [Fact]
    public void VisibleSlots_ComeBackInDrawOrder()
    {
        var shapes = Grid(cols: 20, rows: 20, spacing: 5);
        var index = new SceneIndex();
        index.Rebuild(shapes);
        index.Query(-10, -10, 200, 200);

        var slots = Ascending(index);
        Assert.NotEmpty(slots);

        // Ascending == draw order. This is the property that lets the renderer skip sorting.
        for (int i = 1; i < slots.Count; i++)
            Assert.True(slots[i] > slots[i - 1], "Visible slots must ascend — that is the draw order.");
    }

    [Fact]
    public void VisibleTopDown_IsTheExactReverse()
    {
        var shapes = Grid(cols: 12, rows: 12, spacing: 8);
        var index = new SceneIndex();
        index.Rebuild(shapes);
        index.Query(-5, -5, 60, 60);

        var up = Ascending(index);
        var down = new List<int>();
        foreach (var slot in index.VisibleTopDown) down.Add(slot);

        // Hit-testing needs topmost-first; anything else picks the wrong shape under a stack.
        Assert.Equal(up.AsEnumerable().Reverse(), down);
    }

    [Fact]
    public void CullingActuallyReducesWork()
    {
        var shapes = Grid(cols: 200, rows: 200, spacing: 10);   // 40,000 shapes
        var index = new SceneIndex();
        index.Rebuild(shapes);

        index.Query(0, 0, 100, 100);   // ~121 shapes of 40,000

        Assert.InRange(index.VisibleCount, 100, 200);

        // The gate that matters: shapes *examined* must be a small multiple of shapes *drawn*,
        // not a fraction of the document. The old QuadTree path walked all 40,000 every frame.
        Assert.True(index.ConsideredCount < shapes.Count / 20,
            $"Considered {index.ConsideredCount} of {shapes.Count} to find {index.VisibleCount}. " +
            "Culling is not narrowing the candidate set.");
    }

    [Fact]
    public void DenseCluster_DoesNotDefeatTheIndex()
    {
        // A tight cluster plus far-flung outliers — the shape of drawing that made the QuadTree
        // bottom out at MaxDepth and degrade into a linear leaf scan.
        var shapes = new List<IDrawable>();
        for (int i = 0; i < 20_000; i++)
            shapes.Add(new VRectangle(new VXYZ((i % 141) * 0.05, (i / 141) * 0.05), 0.02, 0.02));
        for (int i = 0; i < 100; i++)
            shapes.Add(new VRectangle(new VXYZ(i * 5000, i * 5000), 10, 10));

        var index = new SceneIndex();
        index.Rebuild(shapes);
        index.Query(-1, -1, 1, 1);

        var expected = BruteForce(shapes, -1, -1, 1, 1);
        Assert.Equal(expected, Ascending(index));
    }

    [Fact]
    public void UnboundedShapes_AreAlwaysVisible()
    {
        // VRay and VXLine are semi-infinite; culling them by bounds would make them vanish.
        var ray = new VRay(new VXYZ(0, 0), new VXYZ(1, 0));
        var shapes = new List<IDrawable> { new VRectangle(new VXYZ(0, 0), 1, 1), ray };

        var index = new SceneIndex();
        index.Rebuild(shapes);

        index.Query(100_000, 100_000, 100_001, 100_001);   // nowhere near the rectangle
        Assert.Contains(1, Ascending(index));
    }

    [Fact]
    public void OversizeShapes_AreFoundFromAnywhereTheyCover()
    {
        // Far larger than a cell, so it is held out of the grid and always tested.
        var huge = new VRectangle(new VXYZ(-10_000, -10_000), 20_000, 20_000);
        var shapes = new List<IDrawable> { huge };
        shapes.AddRange(Grid(cols: 50, rows: 50, spacing: 4));

        var index = new SceneIndex();
        index.Rebuild(shapes);

        index.Query(50, 50, 60, 60);
        Assert.Contains(0, Ascending(index));

        index.Query(50_000, 50_000, 50_010, 50_010);   // outside it
        Assert.DoesNotContain(0, Ascending(index));
    }

    [Fact]
    public void AddedShapes_AreVisibleImmediately()
    {
        var shapes = Grid(cols: 10, rows: 10, spacing: 10);
        var index = new SceneIndex();
        index.Rebuild(shapes);

        var late = new VCircle(new VXYZ(500, 500), 5);
        index.Add(late);

        index.Query(490, 490, 510, 510);
        var slots = Ascending(index);

        Assert.Contains(shapes.Count, slots);
        Assert.Same(late, index.ShapeAt(shapes.Count));
    }

    [Fact]
    public void RemovedShapes_StopBeingVisible()
    {
        var shapes = Grid(cols: 8, rows: 8, spacing: 10);
        var index = new SceneIndex();
        index.Rebuild(shapes);

        var victim = (Shape)shapes[20];
        Assert.True(index.Remove(victim));

        index.Query(-100, -100, 1000, 1000);
        Assert.DoesNotContain(20, Ascending(index));
        Assert.Null(index.ShapeAt(20));

        Assert.False(index.Remove(victim));   // idempotent
    }

    [Fact]
    public void RepeatedQueries_DoNotLeakVisibilityBetweenFrames()
    {
        var shapes = Grid(cols: 30, rows: 30, spacing: 10);
        var index = new SceneIndex();
        index.Rebuild(shapes);

        index.Query(-5, -5, 300, 300);
        var wide = index.VisibleCount;
        Assert.True(wide > 100);

        index.Query(-5, -5, 5, 5);
        Assert.Equal(1, index.VisibleCount);
        Assert.Equal(new[] { 0 }, Ascending(index));

        // And back out again — the bitset must be fully restored, not merely widened.
        index.Query(-5, -5, 300, 300);
        Assert.Equal(wide, index.VisibleCount);
    }

    [Fact]
    public void QueryIsAllocationFree()
    {
        var shapes = Grid(cols: 100, rows: 100, spacing: 10);
        var index = new SceneIndex();
        index.Rebuild(shapes);

        index.Query(0, 0, 200, 200);              // warm up
        foreach (var _ in index.Visible) { }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
        {
            index.Query(i, i, 200 + i, 200 + i);
            foreach (var _ in index.Visible) { }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The per-frame path must not produce garbage. The old renderer allocated a HashSet per
        // frame purely to hold the cull result.
        Assert.True(allocated == 0,
            $"Query + enumeration allocated {allocated} bytes over 50 frames; it must allocate none.");
    }
}
