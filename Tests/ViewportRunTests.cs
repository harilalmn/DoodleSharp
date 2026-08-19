using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C2VGeometry;
using DoodleSharp.Canvas;
using DoodleSharp.Execution;
using DoodleSharp.Project;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// A real project that uses <c>Viewports</c>, compiled and executed the way pressing Run does.
///
/// <para>
/// The pieces are covered separately — the tree, placement, the injected global using — but only
/// running one proves they compose: the synthetic using has to survive emit (it carries an encoding
/// for exactly that reason), the layout has to be reset before <c>Main()</c> rather than after, and
/// the shapes have to land in the cells the source names.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ViewportRunTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "DoodleSharpViewportRun", Guid.NewGuid().ToString("N"));

    private readonly IShapeRegistry? _previousRegistry;
    private readonly bool _previousAutoRegister;

    public ViewportRunTests()
    {
        // Other tests in this collection swap the registry for a counting double, and executing a
        // project registers through whatever DefaultRegistry happens to be — so it has to be pinned
        // here rather than assumed. These pass alone without it and fail in a full run, which is the
        // most confusing way for a test to be wrong.
        _previousRegistry = Shape.DefaultRegistry;
        _previousAutoRegister = Shape.AutoRegister;
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        Shape.AutoRegister = true;
        CanvasRenderer.Instance.Clear();
    }

    public void Dispose()
    {
        CanvasRenderer.Instance.Clear();
        Shape.DefaultRegistry = _previousRegistry;
        Shape.AutoRegister = _previousAutoRegister;
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private static string Program(string body) => $$"""
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using C2VGeometry;
        using DoodleSharp.Animation;
        using DoodleSharp.Console;

        namespace ViewportRunProbe
        {
            public class Viz
            {
                public static void Main()
                {
        {{body}}
                }
            }
        }
        """;

    private async Task<CompilationResult> RunAsync(string body)
    {
        var project = VizCodeProject.CreateNew(_dir, "ViewportRunProbe");
        project.EntryPointFile!.Content = Program(body);
        return await new ModuleCompiler().CompileAndExecuteAsync(project);
    }

    [Fact]
    public async Task AProjectCanDivideTheCanvasAndPlaceIntoEachCell()
    {
        var result = await RunAsync("""
                    Viewports.Rows = 2;
                    Viewports.Columns = 3;

                    new VLine(new VXYZ(0, 0), new VXYZ(10, 0)).Place(Viewports[0][0]);
                    new VLine(new VXYZ(0, 0), new VXYZ(10, 0)).Place(Viewports[1][2]);
                    new VLine(new VXYZ(0, 0), new VXYZ(10, 0)).Place(Viewports[1][2]);
                    new VCircle(new VXYZ(0, 0), 5);                    // bare, so the first cell
        """);

        Assert.True(result.Success, result.Error);

        Assert.Equal(2, Viewport.Root.Rows);
        Assert.Equal(3, Viewport.Root.Columns);
        Assert.Equal(4, CanvasRenderer.Instance.GetShapes().Count);

        Assert.Equal(2, CanvasRenderer.Instance.GetShapes(Viewport.Root[0][0]).Count);
        Assert.Equal(2, CanvasRenderer.Instance.GetShapes(Viewport.Root[1][2]).Count);
        Assert.Empty(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][1]));
    }

    [Fact]
    public async Task AProjectCanSubdivideACellAndSizeTheRowsAndColumns()
    {
        var result = await RunAsync("""
                    Viewports.Columns = 2;
                    Viewports[0][0].Width = "3*";

                    Viewport right = Viewports[0][1];
                    right.Rows = 3;
                    right[0].Height = "240";

                    new VLine(new VXYZ(0, 0), new VXYZ(1, 0)).Place(right[2][0]);
        """);

        Assert.True(result.Success, result.Error);

        Assert.Equal("3*", Viewport.Root[0][0].Width);
        Assert.Equal("240", Viewport.Root[0][1][0].Height);
        Assert.Equal(4, Viewport.Leaves().Count);
        Assert.Single(CanvasRenderer.Instance.GetShapes(Viewport.Root[0][1][2][0]));
    }

    /// <summary>
    /// The layout is reset before the program runs, not after — so a run always establishes the
    /// layout its own source asks for, and deleting the line that divided the canvas puts it back.
    /// </summary>
    [Fact]
    public async Task DeletingTheLayoutLineRestoresASingleViewportOnTheNextRun()
    {
        Assert.True((await RunAsync("            Viewports.Rows = 3;")).Success);
        Assert.Equal(3, Viewport.Root.Rows);

        var result = await RunAsync("            new VCircle(new VXYZ(0, 0), 5);");

        Assert.True(result.Success, result.Error);
        Assert.True(Viewport.Root.IsLeaf);
        Assert.Single(Viewport.Leaves());
    }

    /// <summary>
    /// Indexing past the layout is a runtime error in the user's own code, so it has to arrive the
    /// way every other runtime error does — as a failed run whose message says what went wrong.
    /// </summary>
    [Fact]
    public async Task IndexingPastTheLayoutFailsTheRunWithAUsefulMessage()
    {
        var result = await RunAsync("""
                    Viewports.Rows = 2;
                    new VLine(new VXYZ(0, 0), new VXYZ(1, 0)).Place(Viewports[5][0]);
        """);

        Assert.False(result.Success);
        Assert.Contains("out of range", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 rows x 1 column", result.Error ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// The negative control for the whole feature: a project that never mentions viewports must be
    /// completely unaffected, right down to the registry handing back one shared list.
    /// </summary>
    [Fact]
    public async Task AProjectThatNeverMentionsViewportsIsUnchanged()
    {
        var result = await RunAsync("""
                    new VCircle(new VXYZ(0, 0), 5);
                    new VLine(new VXYZ(0, 0), new VXYZ(10, 0));
        """);

        Assert.True(result.Success, result.Error);
        Assert.True(Viewport.Root.IsLeaf);

        var all = CanvasRenderer.Instance.GetShapes();
        Assert.Equal(2, all.Count);
        Assert.Same(all, CanvasRenderer.Instance.GetShapes(Viewport.Root));
    }
}
