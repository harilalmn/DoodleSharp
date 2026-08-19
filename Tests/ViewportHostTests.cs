using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The WPF side of the viewport grid: that the visual tree really is rebuilt to match the viewport
/// tree, that a surviving cell keeps its own canvas, and that row and column sizes reach the grid.
///
/// <para>
/// These construct real WPF elements, so they run on a dedicated STA thread rather than the xunit
/// worker. That is worth the machinery: re-parenting a reused canvas throws
/// <c>InvalidOperationException</c> at runtime and nothing short of building the tree catches it —
/// a source scan cannot see it, and it fires the first time any layout changes.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ViewportHostTests
{
    /// <summary>Runs a body on a fresh STA thread and rethrows whatever it threw.</summary>
    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the STA thread did not finish");

        if (failure != null)
            throw new Xunit.Sdk.XunitException($"{failure.GetType().Name}: {failure.Message}\n{failure.StackTrace}");
    }

    /// <summary>Builds a host, applies a layout, and hands both to the assertions.</summary>
    private static void WithHost(Action<ViewportHost> body) => OnStaThread(() =>
    {
        var previousRegistry = Shape.DefaultRegistry;
        Viewport.Reset();
        CanvasRenderer.Instance.ClearShapes();
        try
        {
            body(new ViewportHost());
        }
        finally
        {
            Viewport.Reset();
            CanvasRenderer.Instance.ClearShapes();
            Shape.DefaultRegistry = previousRegistry;
        }
    });

    [Fact]
    public void TheDefaultLayoutIsExactlyOneCanvas()
    {
        WithHost(host =>
        {
            Assert.Single(host.Canvases);
            Assert.False(host.IsDivided);
            Assert.NotNull(host.ActiveCanvas);
            Assert.Same(Viewport.Root, host.ActiveViewport);
        });
    }

    [Fact]
    public void DividingTheLayoutBuildsOneCanvasPerLeaf()
    {
        WithHost(host =>
        {
            Viewport.Root.Rows = 2;
            Viewport.Root.Columns = 3;
            host.Sync();

            Assert.Equal(6, host.Canvases.Count);
            Assert.True(host.IsDivided);
            Assert.Equal(6, Viewport.Leaves().Count);

            foreach (var leaf in Viewport.Leaves())
            {
                Assert.Same(leaf, host.CanvasFor(leaf).OwningViewport);
            }
        });
    }

    [Fact]
    public void NestedSubdivisionProducesNestedGrids()
    {
        WithHost(host =>
        {
            Viewport.Root.Columns = 2;
            Viewport.Root[0][1].Rows = 3;
            host.Sync();

            Assert.Equal(4, host.Canvases.Count);
            Assert.Equal(4, Viewport.Leaves().Count);
        });
    }

    /// <summary>
    /// The bug this class exists for. A canvas that survives a rebuild is re-parented into a fresh
    /// cell, and a WPF element may have only one parent — without detaching it first, the second
    /// layout change throws "Specified element is already the logical child of another element".
    /// </summary>
    [Fact]
    public void ReshapingRepeatedlyDoesNotThrowOnReparenting()
    {
        WithHost(host =>
        {
            for (var i = 0; i < 4; i++)
            {
                Viewport.Root.Columns = 2;
                host.Sync();
                Viewport.Root.Columns = 3;
                host.Sync();
                Viewport.Root.Columns = 1;
                host.Sync();
            }

            Assert.Single(host.Canvases);
        });
    }

    /// <summary>
    /// A cell that survives a resize keeps its own canvas object — and with it that cell's pan, zoom
    /// and tool state. Re-running a sketch that declares the same layout must not slam every view
    /// back to the origin.
    /// </summary>
    [Fact]
    public void ACellThatSurvivesAResizeKeepsItsCanvas()
    {
        WithHost(host =>
        {
            Viewport.Root.Columns = 2;
            host.Sync();

            var first = host.CanvasFor(Viewport.Root[0][0]);
            var second = host.CanvasFor(Viewport.Root[0][1]);

            Viewport.Root.Columns = 3;
            host.Sync();

            Assert.Same(first, host.CanvasFor(Viewport.Root[0][0]));
            Assert.Same(second, host.CanvasFor(Viewport.Root[0][1]));
            Assert.Equal(3, host.Canvases.Count);
        });
    }

    /// <summary>
    /// Re-declaring the layout a run already established changes nothing, so nothing is rebuilt —
    /// which is the mechanism that lets pan and zoom survive pressing F5.
    /// </summary>
    [Fact]
    public void ReDeclaringTheSameLayoutKeepsEveryCanvas()
    {
        WithHost(host =>
        {
            Viewport.Root.Rows = 2;
            host.Sync();
            var before = host.Canvases.ToList();

            Viewport.Root.Rows = 2;      // what the next run does
            host.Sync();

            Assert.Equal(before, host.Canvases);
        });
    }

    /// <summary>
    /// Star and pixel sizes have to reach the WPF grid, or the layout on screen keeps equal shares
    /// however the code is written.
    /// </summary>
    [Fact]
    public void RowAndColumnSizesReachTheGrid()
    {
        WithHost(host =>
        {
            Viewport.Root.Rows = 2;
            Viewport.Root.Columns = 3;
            Viewport.Root[0].Height = "3*";
            Viewport.Root[0][2].Width = "4*";
            Viewport.Root[0][0].Width = "240";
            host.Sync();

            var grid = Assert.IsType<Grid>(host.Child);

            Assert.Equal(3, grid.RowDefinitions[0].Height.Value);
            Assert.True(grid.RowDefinitions[0].Height.IsStar);
            Assert.Equal(1, grid.RowDefinitions[1].Height.Value);

            Assert.Equal(240, grid.ColumnDefinitions[0].Width.Value);
            Assert.True(grid.ColumnDefinitions[0].Width.IsAbsolute);
            Assert.Equal(4, grid.ColumnDefinitions[2].Width.Value);
            Assert.True(grid.ColumnDefinitions[2].Width.IsStar);
        });
    }

    /// <summary>
    /// Every canvas has to come up with the drawing's settings, including one created by a resize
    /// after the fact — a cell with the wrong grid or no snapping looks alive and is not.
    /// </summary>
    [Fact]
    public void ACanvasCreatedByAResizeInheritsTheDrawingsSettings()
    {
        WithHost(host =>
        {
            host.ShowGrid = false;
            host.SnapToGrid = true;
            host.IsSelectionMode = false;

            Viewport.Root.Columns = 3;
            host.Sync();

            Assert.Equal(3, host.Canvases.Count);
            foreach (var canvas in host.Canvases)
            {
                Assert.False(canvas.ShowGrid);
                Assert.True(canvas.SnapToGrid);
                Assert.False(canvas.IsSelectionMode);
            }
        });
    }

    /// <summary>
    /// The host raises a creation event for every canvas, so the window can wire the per-canvas
    /// events onto cells that did not exist when it started.
    /// </summary>
    [Fact]
    public void EveryNewCanvasIsAnnouncedExactlyOnce()
    {
        WithHost(host =>
        {
            var announced = new List<RenderCanvas>();
            host.CanvasCreated += (_, canvas) => announced.Add(canvas);

            Viewport.Root.Columns = 3;
            host.Sync();

            // Two new ones; the first cell's canvas was reused and was announced at construction.
            Assert.Equal(2, announced.Count);
            Assert.Equal(announced.Count, announced.Distinct().Count());

            Viewport.Root.Columns = 4;
            host.Sync();

            Assert.Equal(3, announced.Count);
            Assert.Equal(announced.Count, announced.Distinct().Count());
        });
    }

    /// <summary>
    /// Each cell draws only what was placed on it. The rendering half of the partition, checked
    /// through the canvases rather than the registry.
    /// </summary>
    [Fact]
    public void EachCanvasRendersOnlyItsOwnShapes()
    {
        WithHost(host =>
        {
            Shape.DefaultRegistry = CanvasRenderer.Instance;

            Viewport.Root.Columns = 2;
            host.Sync();

            new VLine(new VXYZ(0, 0), new VXYZ(1, 0)).Place(Viewport.Root[0][0]);
            new VLine(new VXYZ(0, 0), new VXYZ(1, 0)).Place(Viewport.Root[0][1]);
            new VLine(new VXYZ(0, 0), new VXYZ(1, 0)).Place(Viewport.Root[0][1]);

            CanvasRenderer.Instance.RenderTo(host);

            Assert.Single(host.CanvasFor(Viewport.Root[0][0]).GetCurrentShapes());
            Assert.Equal(2, host.CanvasFor(Viewport.Root[0][1]).GetCurrentShapes().Count);
        });
    }

    /// <summary>
    /// A handler is registered once for the whole drawing, so the event has to say which cell it
    /// came from — otherwise every cell's events look alike and a divided drawing cannot respond to
    /// the pointer at all.
    /// </summary>
    [Fact]
    public void MouseEventsCarryTheViewportTheyCameFrom()
    {
        WithHost(host =>
        {
            Viewport.Root.Columns = 2;
            host.Sync();

            foreach (var canvas in host.Canvases)
            {
                var info = (DoodleSharp.Animation.MouseInfo)typeof(RenderCanvas)
                    .GetMethod("BuildMouseInfo", System.Reflection.BindingFlags.Instance |
                                                 System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(canvas, new object?[]
                    {
                        DoodleSharp.Animation.MouseEventKind.Move,
                        new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0),
                        new System.Windows.Point(1, 1),
                        new VXYZ(0, 0),
                        0,
                    })!;

                Assert.Same(canvas.OwningViewport, info.Viewport);
            }

            // ...and the two cells really are told apart.
            Assert.NotSame(host.Canvases[0].OwningViewport, host.Canvases[1].OwningViewport);
        });
    }

    /// <summary>
    /// A canvas that leaves the layout gives its Direct3D device back. Nothing used to destroy a
    /// canvas, so without this a drawing that is divided and re-divided leaks one device per round.
    /// </summary>
    [Fact]
    public void RetiringACanvasDoesNotLeakItsGpuDevice()
    {
        WithHost(host =>
        {
            for (var i = 0; i < 3; i++)
            {
                Viewport.Root.Columns = 4;
                host.Sync();
                Viewport.Root.Columns = 1;
                host.Sync();
            }

            var inUse = (int)typeof(RenderCanvas)
                .GetField("_gpuBackendsInUse", System.Reflection.BindingFlags.Static |
                                               System.Reflection.BindingFlags.NonPublic)!
                .GetValue(null)!;

            var budget = (int)typeof(RenderCanvas)
                .GetField("MaxGpuBackends", System.Reflection.BindingFlags.Static |
                                            System.Reflection.BindingFlags.NonPublic)!
                .GetRawConstantValue()!;

            Assert.InRange(inUse, 0, budget);
        });
    }

    /// <summary>
    /// The frame-timing readout is drawn once, by the cell being looked at. Its numbers are
    /// process-wide — one frame, one cost — so repeating them on every cell would not be honest.
    /// </summary>
    [Fact]
    public void OnlyOneCanvasPaintsThePerformanceReadout()
    {
        WithHost(host =>
        {
            Viewport.Root.Columns = 3;
            host.Sync();

            Assert.Single(host.Canvases.Where(c => c.DrawsPerformanceHud));
            Assert.True(host.ActiveCanvas.DrawsPerformanceHud);
        });
    }
}
