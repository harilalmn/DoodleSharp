using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using DoodleSharp.Animation;
using DoodleSharp.Canvas;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the host-side wiring of the mouse callbacks, which reflection cannot see and a unit test
/// cannot drive: the dispatch points live inside <c>RenderCanvas</c>'s input handlers, and those need a
/// real window and real input. A source scan is what is available, and it catches the realistic
/// regression — someone refactoring the handler chain and dropping a line.
///
/// <para>
/// This is the same approach <c>ExportFidelityTests</c> takes for the overlay-suppression call sites,
/// and for the same reason: the defect there was never a missing API, it was call sites that did not
/// use it.
/// </para>
/// </summary>
public class MouseWiringTests
{
    private static string ReadRepoFile(string relativePath)
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relativePath));

    private static string MethodBody(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{declaration}' not found — the handler was renamed or removed.");

        // Scan to the next member declaration at the same indentation.
        var next = source.IndexOf("\n    private ", start + declaration.Length, StringComparison.Ordinal);
        var alt = source.IndexOf("\n    public ", start + declaration.Length, StringComparison.Ordinal);
        if (alt >= 0 && (next < 0 || alt < next)) next = alt;

        return next > start ? source[start..next] : source[start..];
    }

    // ── Every mouse handler must offer the events to user code ────────────────────────────────────

    [Theory]
    [InlineData("private void OnMouseWheel(", "RaiseWheel")]
    [InlineData("private void OnMouseMove(", "RaiseMove")]
    [InlineData("private void OnMouseDown(", "RaiseDown")]
    [InlineData("private void OnMouseUp(", "RaiseUp")]
    [InlineData("private void OnMouseEnter(", "RaiseEnter")]
    [InlineData("private void OnMouseLeave(", "RaiseLeave")]
    public void EveryCanvasMouseHandlerDispatchesToUserCode(string declaration, string raiseCall)
    {
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")), declaration);

        Assert.Contains(raiseCall, body);
        Assert.Contains("AllowUserMouse", body);
    }

    [Fact]
    public void TheCanvasSubscribesToEnterAndLeave()
    {
        // Neither existed before this feature; a hover handler and the "a drag that leaves the canvas
        // must still get its up" guarantee both depend on them being wired.
        var source = ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs"));

        Assert.Contains("MouseEnter += OnMouseEnter;", source);
        Assert.Contains("MouseLeave += OnMouseLeave;", source);
    }

    [Fact]
    public void ThePolledStateIsTrackedOutsideTheHandlerGate()
    {
        // Mouse.X/Y/IsDown -- and so Sketch.MouseX/MouseY/MousePressed, which read them -- must be
        // current even when no handler is registered, so the tracking call cannot sit behind
        // AllowUserMouse. Assert it is reached from the move handler unconditionally.
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")),
            "private void OnMouseMove(");

        var trackAt = body.IndexOf("TrackPointer(", StringComparison.Ordinal);
        var gateAt = body.IndexOf("if (AllowUserMouse)", StringComparison.Ordinal);

        Assert.True(trackAt >= 0, "OnMouseMove no longer records the pointer position.");
        Assert.True(gateAt >= 0, "OnMouseMove no longer dispatches to user code.");
        Assert.True(trackAt < gateAt,
            "TrackPointer must run before the handler gate, or the polled state stops updating for "
            + "projects that register no handlers.");
    }

    [Fact]
    public void UserDispatchPrecedesSelectionInTheDownHandler()
    {
        // The crux of the feature. Selection mode is on by default and its branch consumes every left
        // click with e.Handled = true, so dispatching after it would mean a click handler never fired
        // in the default configuration.
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")),
            "private void OnMouseDown(");

        var dispatchAt = body.IndexOf("RaiseDown", StringComparison.Ordinal);
        var selectionAt = body.IndexOf("if (IsSelectionMode && _selectionTool != null)", StringComparison.Ordinal);

        Assert.True(dispatchAt >= 0, "OnMouseDown no longer dispatches to user code.");
        Assert.True(selectionAt >= 0, "The selection branch was renamed — re-check the ordering.");
        Assert.True(dispatchAt < selectionAt,
            "User dispatch must come before the selection branch, which returns with e.Handled set.");
    }

    [Fact]
    public void TheDrawingToolStillTakesPriorityOverUserHandlers()
    {
        // An armed drawing tool is a modal state the user chose with a shortcut; user code must not be
        // able to break it. Both tool branches return before the dispatch point.
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")),
            "private void OnMouseDown(");

        var drawingAt = body.IndexOf("_drawingTool.OnLeftClick(vPoint)", StringComparison.Ordinal);
        var measuringAt = body.IndexOf("_measuringTool.OnLeftClick(vPoint)", StringComparison.Ordinal);
        var dispatchAt = body.IndexOf("RaiseDown", StringComparison.Ordinal);

        Assert.True(drawingAt >= 0 && measuringAt >= 0 && dispatchAt >= 0);
        Assert.True(drawingAt < dispatchAt, "The drawing tool branch must precede user dispatch.");
        Assert.True(measuringAt < dispatchAt, "The measuring tool branch must precede user dispatch.");
    }

    [Fact]
    public void TheCanvasStillGrabsKeyboardFocusOnClick()
    {
        // Note 20: without this, focus stays in the code editor and the single-key drawing-tool
        // shortcuts type into the editor instead. It has to stay first and unconditional, and the
        // dispatch work above edits precisely this method.
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")),
            "private void OnMouseDown(");

        Assert.Contains("if (!IsKeyboardFocusWithin) Focus();", body);
    }

    [Fact]
    public void MiddleDragPanIsNotHandedToUserCode()
    {
        // Pan is the canvas's only pan gesture; a script taking it would leave a drawing larger than
        // the viewport unreachable. The gate excludes it by construction.
        Assert.False(MouseGate.Allow(interactive: true, panning: true,
            drawingToolActive: false, measuring: false));
    }

    // ── Lifecycle: every ALC boundary drops the handlers ──────────────────────────────────────────

    [Theory]
    [InlineData("Execution/ModuleCompiler.cs")]
    [InlineData("Sketch/SketchRuntime.cs")]
    [InlineData("MainWindow.xaml.cs")]
    public void EveryTeardownSiteDropsBothRegistriesTogether(string relativePath)
    {
        var source = ReadRepoFile(relativePath.Replace('/', Path.DirectorySeparatorChar));

        var frameClears = CountOccurrences(source, "Frame.Clear()");
        var mouseClears = CountOccurrences(source, "Mouse.Clear()");

        Assert.True(frameClears > 0, $"{relativePath} no longer clears Frame.");

        // The two registries have identical lifecycles: a handler and a queued callback are both
        // delegates into the collectible user assembly. Clearing one without the other is how a
        // pinned load context or a stale handler creeps back in.
        Assert.Equal(frameClears, mouseClears);
    }

    [Fact]
    public void TheRunPathDropsHandlersBeforeCompiling()
    {
        var source = ReadRepoFile(Path.Combine("Execution", "ModuleCompiler.cs"));

        var clearAt = source.IndexOf("Mouse.Clear();", StringComparison.Ordinal);
        var compileAt = source.IndexOf("CreateCompilationAsync(project, forExecution: true)",
            StringComparison.Ordinal);

        Assert.True(clearAt >= 0 && compileAt >= 0);
        Assert.True(clearAt < compileAt,
            "Handlers must be dropped before the new assembly is built, not after.");
    }

    [Fact]
    public void TheResidentReRunPathDropsHandlers()
    {
        // A Global Parameters slider drag lands here many times a second.
        var body = MethodBody(ReadRepoFile(Path.Combine("Execution", "ModuleCompiler.cs")),
            "public static async Task<CompilationResult> ReExecuteResidentAsync()");

        Assert.Contains("Frame.Clear();", body);
        Assert.Contains("Mouse.Clear();", body);
    }

    [Fact]
    public void StopDropsHandlersUnconditionally()
    {
        // SketchRuntime.Stop() early-returns when no sketch is active, so before this the Stop button
        // did not stop a Frame loop at all. The clear must not sit inside an IsRunning branch.
        var body = MethodBody(ReadRepoFile("MainWindow.xaml.cs"), "private void StopAllAnimations()");

        var clearAt = body.IndexOf("Frame.Clear();", StringComparison.Ordinal);
        var branchAt = body.IndexOf("SketchRuntime.Instance.IsRunning", StringComparison.Ordinal);

        Assert.True(clearAt >= 0, "StopAllAnimations no longer clears Frame.");
        Assert.True(branchAt < 0 || clearAt < branchAt,
            "The clear must precede any IsRunning test, so Stop works in Main() mode too.");
    }

    // ── The repaint hook ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFrameLoopConsumesTheDirtyFlag()
    {
        var source = ReadRepoFile("MainWindow.xaml.cs");
        Assert.Contains("Mouse.ConsumeSceneDirty()", source);
    }

    [Fact]
    public void RepaintAfterUserCodeRetakesTheSnapshotRatherThanOnlyRefreshing()
    {
        // CanvasRenderer.AddShape appends only to the registry's own list, so Refresh() alone repaints
        // the snapshot taken at the end of the run and a shape *created* by a callback never appears.
        var body = MethodBody(ReadRepoFile("MainWindow.xaml.cs"), "private void RepaintAfterUserCode()");

        Assert.Contains("SetFrameShapes", body);
        Assert.Contains("RegistryVersion", body);
    }

    // ── Interactive-mode chrome ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The zoom overlay still exists, still starts hidden, and still offers the three gestures the
    /// wheel gives up when user code owns the mouse.
    ///
    /// <para>
    /// It lives in <c>ViewportCell</c> now, one per cell, revealed while the pointer is over that
    /// cell — in either mode. Interactive mode's guarantee is unchanged, because hovering is a
    /// superset of it; what changed is that with several cells an always-visible panel per cell
    /// would be noise, and a revealed one also says which cell the pointer is on.
    /// </para>
    /// </summary>
    [Fact]
    public void TheNavigationOverlayExistsAndStartsHidden()
    {
        var cell = ReadRepoFile(System.IO.Path.Combine("Canvas", "ViewportCell.cs"));

        Assert.Contains("Visibility = Visibility.Collapsed", cell);
        Assert.Contains("ZoomStep(false)", cell);
        Assert.Contains("ZoomStep(true)", cell);
        Assert.Contains("ZoomExtentsRequested", cell);

        // Revealed on hover, and deliberately not hidden again on the way to a menu — the active
        // cell has to survive the pointer leaving, or every keyboard shortcut loses its target.
        Assert.Contains("MouseEnter += OnPointerEntered", cell);
        Assert.Contains("MouseLeave += OnPointerLeft", cell);
    }

    [Fact]
    public void InteractiveModeSuppressesTheSelectionOverlay()
    {
        // Otherwise handles from a selection made before the run stay painted over a canvas that no
        // longer responds to them.
        var body = MethodBody(ReadRepoFile(Path.Combine("Canvas", "RenderCanvas.cs")),
            "private void RedrawOverlay()");

        Assert.Contains("!IsInteractive", body);
    }

    [Fact]
    public void TheCanvasOffersAZoomStepForTheOverlayButtons()
    {
        var method = typeof(RenderCanvas).GetMethod("ZoomStep",
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method!.GetParameters().Single().ParameterType);
    }

    [Fact]
    public void F4IsInertWhileUserCodeOwnsTheMouse()
    {
        // The properties panel edits the selected shape, and selection is suppressed in interactive
        // mode, so F4 could only ever open a panel that can never be given anything.
        var source = ReadRepoFile("MainWindow.xaml.cs");

        var f4At = source.IndexOf("e.Key == Key.F4", StringComparison.Ordinal);
        Assert.True(f4At >= 0, "The F4 binding was moved or renamed.");

        var block = source[f4At..Math.Min(source.Length, f4At + 700)];
        Assert.Contains("IsCanvasInteractive", block);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
