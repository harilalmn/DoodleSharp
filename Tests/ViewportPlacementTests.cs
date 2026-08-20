using System;
using System.IO;
using System.Linq;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Placing shapes on viewports: which cell a shape lands in, and how the registry partitions the
/// scene per cell without costing anything on the undivided default.
/// </summary>
[Collection("CanvasState")]
public class ViewportPlacementTests : IDisposable
{
    private readonly IShapeRegistry? _previousRegistry;
    private readonly bool _previousAutoRegister;

    public ViewportPlacementTests()
    {
        _previousRegistry = Shape.DefaultRegistry;
        _previousAutoRegister = Shape.AutoRegister;
        Shape.AutoRegister = true;
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        CanvasRenderer.Instance.ClearShapes();
        Viewport.Reset();
    }

    public void Dispose()
    {
        CanvasRenderer.Instance.ClearShapes();
        Viewport.Reset();
        Shape.DefaultRegistry = _previousRegistry;
        Shape.AutoRegister = _previousAutoRegister;
    }

    private static VLine Line(double y) => new(new VXYZ(0, y), new VXYZ(10, y));

    /// <summary>
    /// The blank-canvas regression: a run begins with <c>Clear()</c>, which calls
    /// <c>Viewport.Reset()</c> and swaps in a brand-new root object, but a <c>ViewportCell</c> goes
    /// on holding the previous one in <c>OwningViewport</c> until the host's <c>Sync()</c> re-keys
    /// it — and <c>Sync()</c> is queued at <c>DispatcherPriority.Render</c>, below the
    /// Normal-priority await continuation that runs the render. So the render path asks for a leaf
    /// that has just left the tree, and it must still be handed the scene: resolving it with
    /// <c>FirstLeaf()</c> returned the dead node and every cell got an empty list, which is how the
    /// canvas came up blank while the status bar reported "3 shapes drawn".
    /// </summary>
    [Fact]
    public void ADetachedLeafStillSeesTheScene()
    {
        var staleLeaf = Viewport.Root;              // what a cell captured before the run

        CanvasRenderer.Instance.Clear();            // the run's reset: installs a NEW root
        Assert.NotSame(staleLeaf, Viewport.Root);
        Assert.False(staleLeaf.IsAttached);

        Line(0);
        Line(1);
        Line(2);

        Assert.Equal(3, CanvasRenderer.Instance.GetShapes().Count);
        Assert.Equal(3, CanvasRenderer.Instance.GetShapes(staleLeaf).Count);
        Assert.Equal(3, CanvasRenderer.Instance.GetShapes(Viewport.Root).Count);
    }

    /// <summary>
    /// The same thing once shapes have been placed on named cells, which takes the other branch of
    /// <c>GetShapes(Viewport)</c> — the <c>_byViewport</c> dictionary, where a stale key misses just
    /// as surely as the reference check does.
    /// </summary>
    [Fact]
    public void ADetachedLeafStillSeesTheSceneWhenShapesArePlacedOnCells()
    {
        Viewport.Root.Columns = 2;
        var line = Line(0);
        line.Place(Viewport.Root[0][1]);

        var staleLeaf = Viewport.Root[0][1];
        Viewport.Reset();
        Assert.False(staleLeaf.IsAttached);

        Assert.Single(CanvasRenderer.Instance.GetShapes(staleLeaf));
    }

    [Fact]
    public void BareConstructionLandsOnTheRoot()
    {
        var line = Line(0);

        Assert.Same(Viewport.Root, CanvasRenderer.Instance.ViewportOf(line));
        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root));
    }

    /// <summary>
    /// The ordering that matters: a shape registers as it is constructed, so <c>Place(viewport)</c>
    /// is nearly always a <b>move</b>. It must leave the cell it came from.
    /// </summary>
    [Fact]
    public void PlaceMovesAnAlreadyRegisteredShape()
    {
        Viewport.Root.Columns = 2;
        var left = Viewport.Root[0][0];
        var right = Viewport.Root[0][1];

        var line = Line(0);                                   // registered on the root already
        Assert.Single(CanvasRenderer.Instance.GetShapes(left));

        line.Place(right);

        Assert.Empty(CanvasRenderer.Instance.GetShapes(left));
        Assert.Single(CanvasRenderer.Instance.GetShapes(right));
        Assert.Same(right, CanvasRenderer.Instance.ViewportOf(line));
    }

    [Fact]
    public void PlacingTwiceMovesRatherThanDuplicates()
    {
        Viewport.Root.Columns = 3;
        var line = Line(0);

        line.Place(Viewport.Root[0][1]);
        line.Place(Viewport.Root[0][2]);

        Assert.Empty(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][1]));
        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][2]));
        Assert.Single(CanvasRenderer.Instance.GetShapes());        // still one shape overall
    }

    [Fact]
    public void PlacingBackOnTheRootReturnsItThere()
    {
        Viewport.Root.Columns = 2;
        var line = Line(0);
        line.Place(Viewport.Root[0][1]);

        line.Place(Viewport.Root[0][0]);

        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));
        Assert.Empty(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][1]));
    }

    [Fact]
    public void PlaceRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Line(0).Place(null!));
    }

    /// <summary>
    /// The cost contract. While nothing has been placed on a viewport, asking for the root leaf's
    /// shapes hands back the <i>same list instance</i> the unfiltered call returns — no filter, no
    /// dictionary, no allocation. Reference equality is the assertion, because that is the only way
    /// to tell "returned everything" from "returned a copy of everything".
    /// </summary>
    [Fact]
    public void AnUndividedCanvasSharesTheOneList()
    {
        Line(0);
        Line(1);

        var all = CanvasRenderer.Instance.GetShapes();

        Assert.Same(all, CanvasRenderer.Instance.GetShapes(Viewport.Root));
    }

    /// <summary>
    /// The negative control for the test above. Without it, the fast-path assertion would pass just
    /// as happily against an implementation that had no fast path and simply ignored the argument.
    /// </summary>
    [Fact]
    public void OncePlacedTheListsAreNoLongerShared()
    {
        Viewport.Root.Columns = 2;
        Line(0);
        Line(1).Place(Viewport.Root[0][1]);

        var all = CanvasRenderer.Instance.GetShapes();

        Assert.NotSame(all, CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));
        Assert.Equal(2, all.Count);
        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));
        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][1]));
    }

    [Fact]
    public void EveryShapeLandsInExactlyOneCell()
    {
        Viewport.Root.Rows = 2;
        Viewport.Root.Columns = 2;

        for (var i = 0; i < 12; i++)
        {
            Line(i).Place(Viewport.Root[i % 2][(i / 2) % 2]);
        }

        var perCell = Viewport.Leaves().Sum(leaf => CanvasRenderer.Instance.GetShapes(leaf).Count);

        Assert.Equal(12, CanvasRenderer.Instance.GetShapes().Count);
        Assert.Equal(12, perCell);
    }

    /// <summary>
    /// The sort still happens once, in the unfiltered call; partitioning an already-ordered list
    /// preserves the order inside each part, so every cell gets the same ZIndex semantics for free.
    /// </summary>
    [Fact]
    public void CellSubsetsKeepTheirDrawOrder()
    {
        Viewport.Root.Columns = 2;
        var cell = Viewport.Root[0][1];

        var top = Line(0); top.ZIndex = 10; top.Place(cell);
        var bottom = Line(1); bottom.ZIndex = -5; bottom.Place(cell);
        var middle = Line(2); middle.Place(cell);

        var order = CanvasRenderer.Instance.GetShapes(cell).ToArray();

        Assert.Equal(new IDrawable[] { bottom, middle, top }, order);
    }

    /// <summary>
    /// A shape on a cell that is later split keeps drawing in that cell's first sub-cell, and stays
    /// correct however many times that sub-cell is split again. Resolved on read rather than fixed
    /// up on subdivision, so there is no pass to forget to run.
    /// </summary>
    [Fact]
    public void SubdividingACellResolvesItsShapesToTheFirstSubCell()
    {
        Viewport.Root.Columns = 2;
        var right = Viewport.Root[0][1];
        var line = Line(0);
        line.Place(right);

        right.Rows = 3;

        Assert.Single(CanvasRenderer.Instance.GetShapes(right[0][0]));
        Assert.Empty(CanvasRenderer.Instance.GetShapes(right[1][0]));

        right[0][0].Columns = 2;

        Assert.Single(CanvasRenderer.Instance.GetShapes(right[0][0][0][0]));
    }

    /// <summary>
    /// Shrinking a layout is a legitimate thing to do to your own drawing, so shapes on a cell that
    /// no longer exists are re-homed onto the nearest surviving ancestor rather than thrown over or
    /// silently dropped — a running animation must not die because a cell went away.
    /// </summary>
    [Fact]
    public void ShrinkingReHomesTheShapesItOrphansAndSaysSo()
    {
        Viewport.Root.Rows = 3;
        var doomed = Viewport.Root[2][0];
        Line(0).Place(doomed);
        Line(1).Place(Viewport.Root[0][0]);

        var reported = new System.Collections.Generic.List<string>();
        var previousSink = GeometryDiagnostics.Sink;
        GeometryDiagnostics.Sink = reported.Add;
        try
        {
            Viewport.Root.Rows = 2;

            // Nothing is lost, and the orphan lands in the first cell of the viewport that used to
            // contain it — here the root, so it joins the shape already in [0][0].
            Assert.Equal(2, CanvasRenderer.Instance.GetShapes().Count);
            Assert.Equal(2, CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]).Count);
            Assert.Empty(CanvasRenderer.Instance.GetShapes(Viewport.Root[1][0]));

            Assert.Single(reported);
            Assert.Contains("removed by a layout change", reported[0], StringComparison.Ordinal);

            // Reported once, not on every repaint: the map is rewritten, not merely resolved through.
            CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]);
            Assert.Single(reported);
        }
        finally { GeometryDiagnostics.Sink = previousSink; }
    }

    /// <summary>
    /// <c>RepaintAfterUserCode</c> decides between re-indexing and re-snapshotting by comparing this
    /// counter, so a move that did not bump it would take the cheap path and never reach the screen.
    /// </summary>
    [Fact]
    public void PlacingBumpsTheRegistryVersion()
    {
        Viewport.Root.Columns = 2;
        var line = Line(0);

        var before = RegistryVersion();
        line.Place(Viewport.Root[0][1]);
        var after = RegistryVersion();

        Assert.NotEqual(before, after);

        // ...and re-placing on the same cell is a no-op, so a mouse handler re-stating the placement
        // every frame does not force a re-snapshot 60 times a second.
        line.Place(Viewport.Root[0][1]);
        Assert.Equal(after, RegistryVersion());
    }

    private static int RegistryVersion() =>
        (int)typeof(CanvasRenderer)
            .GetProperty("RegistryVersion", System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.NonPublic |
                                            System.Reflection.BindingFlags.Public)!
            .GetValue(CanvasRenderer.Instance)!;

    /// <summary>
    /// The map holds strong references, so an entry left behind after a shape leaves the canvas
    /// would pin it. Observable through the fast path: with the map empty again, the root leaf gets
    /// the shared list back.
    /// </summary>
    [Fact]
    public void RemovingAShapeDropsItsViewportEntry()
    {
        Viewport.Root.Columns = 2;
        var line = Line(0);
        line.Place(Viewport.Root[0][1]);
        Assert.NotSame(CanvasRenderer.Instance.GetShapes(),
                       CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));

        line.Remove();

        Assert.Same(CanvasRenderer.Instance.GetShapes(),
                    CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));
    }

    [Fact]
    public void ClearingShapesEmptiesTheViewportMap()
    {
        Viewport.Root.Columns = 2;
        Line(0).Place(Viewport.Root[0][1]);

        CanvasRenderer.Instance.ClearShapes();

        Assert.Same(CanvasRenderer.Instance.GetShapes(),
                    CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]));
    }

    /// <summary>
    /// The layout is part of the run lifecycle, like shape ids — so deleting a
    /// <c>Viewports.Rows = 3</c> line takes effect on the next run.
    /// </summary>
    [Fact]
    public void TheBetweenRunsResetRestoresTheDefaultLayout()
    {
        Viewport.Root.Rows = 3;

        CanvasRenderer.Instance.Clear();

        Assert.True(Viewport.Root.IsLeaf);
    }

    /// <summary>
    /// ...but the geometry-only clear must not, and this is the trap worth a test of its own: sketch
    /// mode calls it on <b>every frame</b>, and <c>Canvas.Clear()</c> reaches it from user code. A
    /// layout torn down 60 times a second, or wiped from inside a mouse handler, is exactly the
    /// surprise that split these two methods apart in the first place.
    /// </summary>
    [Fact]
    public void TheGeometryOnlyClearLeavesTheLayoutAlone()
    {
        Viewport.Root.Rows = 3;

        CanvasRenderer.Instance.ClearShapes();
        C2VGeometry.Canvas.Clear();

        Assert.Equal(3, Viewport.Root.Rows);
        Assert.False(Viewport.Root.IsLeaf);
    }

    /// <summary>
    /// A source scan pinning the same rule at the point it could regress: the reset belongs in the
    /// lifecycle <c>Clear()</c> and nowhere near <c>ClearShapes()</c>.
    /// </summary>
    [Fact]
    public void TheResetIsNotWiredIntoClearShapes()
    {
        var source = File.ReadAllText(
            Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "Canvas", "CanvasRenderer.cs"));

        var i = source.IndexOf("public void ClearShapes()", StringComparison.Ordinal);
        Assert.True(i > 0, "ClearShapes must exist");
        var body = source[i..source.IndexOf("public void RenderTo", i, StringComparison.Ordinal)];

        Assert.DoesNotContain("Viewport.Reset()", body, StringComparison.Ordinal);
        Assert.Contains("Viewport.Reset()", source, StringComparison.Ordinal);   // but it is wired somewhere
    }
}
