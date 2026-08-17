using System;
using System.IO;
using System.Linq;
using System.Windows;
using Xunit;
using DoodleSharp.Docking;

namespace DoodleSharp.Tests;

/// <summary>
/// The docking layout's persistence and recovery logic.
///
/// <para>
/// These are the parts of the feature that run on every launch and whose failure mode is "the window
/// does not come up" or "a panel the user cannot reach" — so they are deliberately written as plain
/// functions over a string and a rectangle, testable without a window. The arrangement itself needs a
/// real DockingManager and is covered by the source scans below plus manual verification.
/// </para>
/// </summary>
public class DockingLayoutTests
{
    // ── The versioned layout file ────────────────────────────────────────────────────────────────

    [Fact]
    public void ALayoutRoundTripsThroughTheWrapper()
    {
        var inner = "<LayoutRoot><RootPanel /></LayoutRoot>";

        var unwrapped = LayoutFile.Unwrap(LayoutFile.Wrap(inner, "2026.8.2"));

        Assert.NotNull(unwrapped);
        Assert.Contains("RootPanel", unwrapped!);
    }

    [Fact]
    public void ALayoutFromAnotherSchemaIsRejected()
    {
        var wrapped = LayoutFile.Wrap("<LayoutRoot />", "2026.8.2")
            .Replace($"schema=\"{LayoutFile.CurrentSchema}\"", "schema=\"99\"");

        // Deliberately no migration path: a layout is a preference, and the cost of a bad migration —
        // an app that will not start, or a panel the user cannot find — far exceeds one reset.
        Assert.Null(LayoutFile.Unwrap(wrapped));
    }

    [Fact]
    public void ALayoutWithNoSchemaIsRejected()
        => Assert.Null(LayoutFile.Unwrap("<DoodleSharpLayout><LayoutRoot /></DoodleSharpLayout>"));

    [Fact]
    public void AForeignRootIsRejected()
        => Assert.Null(LayoutFile.Unwrap("<SomethingElse schema=\"1\"><LayoutRoot /></SomethingElse>"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<DoodleSharpLayout schema=\"1\"><LayoutRoot></DoodleSharpLayout>")]  // truncated
    public void UnreadableContentIsRejectedWithoutThrowing(string contents)
    {
        // Every failure here is the same failure: use the default instead. What must never happen is
        // an exception escaping into start-up.
        Assert.Null(LayoutFile.Unwrap(contents));
    }

    [Fact]
    public void UnwrapHandlesNull() => Assert.Null(LayoutFile.Unwrap(null));

    // ── Panels added since a layout was saved ────────────────────────────────────────────────────

    [Fact]
    public void APanelMissingFromTheLayoutIsReported()
    {
        var registered = new[] { "ds.tool.canvas", "ds.tool.console", "ds.tool.newpanel" };
        var inLayout = new[] { "ds.tool.canvas", "ds.tool.console" };

        // A layout saved before the panel existed simply omits it. Unnoticed, the panel would not be
        // in the tree at all and its Windows-menu entry would silently do nothing.
        Assert.Equal(new[] { "ds.tool.newpanel" }, LayoutFile.FindMissingIds(registered, inLayout));
    }

    [Fact]
    public void NothingIsReportedWhenTheLayoutCoversEveryPanel()
    {
        var ids = new[] { "ds.tool.canvas", "ds.tool.console" };
        Assert.Empty(LayoutFile.FindMissingIds(ids, ids));
    }

    [Fact]
    public void PanelsTheLayoutHasButTheAppDoesNotAreNotReported()
    {
        // The reverse case — a panel removed in a later version — is handled by dropping it during
        // restore, not by re-adding it here.
        Assert.Empty(LayoutFile.FindMissingIds(
            new[] { "ds.tool.canvas" },
            new[] { "ds.tool.canvas", "ds.tool.retired" }));
    }

    // ── Off-screen recovery ──────────────────────────────────────────────────────────────────────

    private static readonly Rect TwoMonitors = new(0, 0, 3840, 1080);

    [Fact]
    public void AWindowFullyOnScreenIsLeftAlone()
    {
        var window = new Rect(200, 100, 600, 400);

        Assert.False(ScreenBounds.IsStranded(window, TwoMonitors));
        Assert.Equal(window, ScreenBounds.ClampToVirtualScreen(window, TwoMonitors));
    }

    [Fact]
    public void AWindowOnAMonitorThatIsGoneIsBroughtBack()
    {
        // Saved on the second monitor; reopened with only the first attached.
        var window = new Rect(2200, 300, 600, 400);
        var onlyPrimary = new Rect(0, 0, 1920, 1080);

        Assert.True(ScreenBounds.IsStranded(window, onlyPrimary));

        var fixedUp = ScreenBounds.ClampToVirtualScreen(window, onlyPrimary);

        Assert.True(fixedUp.Right <= onlyPrimary.Right);
        Assert.True(fixedUp.Left >= onlyPrimary.Left);
        Assert.Equal(600, fixedUp.Width);   // size preserved; only the position moved
        Assert.Equal(400, fixedUp.Height);
    }

    [Fact]
    public void RecoveryClampsRatherThanCentring()
    {
        // A panel parked on the right should come back on the right. Centring would throw away the
        // arrangement the user chose.
        var window = new Rect(2400, 200, 400, 300);
        var onlyPrimary = new Rect(0, 0, 1920, 1080);

        var fixedUp = ScreenBounds.ClampToVirtualScreen(window, onlyPrimary);

        Assert.Equal(onlyPrimary.Right - 400, fixedUp.Left);
    }

    [Fact]
    public void ASecondMonitorToTheLeftOfPrimaryIsHandled()
    {
        // The case everyone gets wrong: a monitor placed left of primary gives the virtual desktop a
        // negative origin, so clamping to [0, width] would strand every window on it.
        var desktop = new Rect(-1920, 0, 3840, 1080);
        var window = new Rect(-1500, 200, 500, 400);

        Assert.False(ScreenBounds.IsStranded(window, desktop));
        Assert.Equal(window, ScreenBounds.ClampToVirtualScreen(window, desktop));
    }

    [Fact]
    public void AWindowLargerThanTheDesktopIsShrunkToFit()
    {
        var desktop = new Rect(0, 0, 1024, 768);
        var window = new Rect(2000, 2000, 4000, 3000);

        var fixedUp = ScreenBounds.ClampToVirtualScreen(window, desktop);

        Assert.True(fixedUp.Width <= desktop.Width);
        Assert.True(fixedUp.Height <= desktop.Height);
        Assert.True(desktop.Contains(fixedUp));
    }

    [Fact]
    public void ASliverOnScreenStillCountsAsStranded()
    {
        // Two pixels of a window is as unusable as none: there is no caption left to grab.
        var window = new Rect(1918, 400, 600, 400);
        var onlyPrimary = new Rect(0, 0, 1920, 1080);

        Assert.True(ScreenBounds.IsStranded(window, onlyPrimary));
    }

    [Fact]
    public void ADegenerateDesktopIsNotTreatedAsStranding()
    {
        // Reported while a monitor is being reconfigured. Moving windows on that basis would be worse
        // than leaving them alone.
        var window = new Rect(100, 100, 400, 300);

        Assert.False(ScreenBounds.IsStranded(window, new Rect(0, 0, 0, 0)));
        Assert.Equal(window, ScreenBounds.ClampToVirtualScreen(window, new Rect(0, 0, 0, 0)));
    }

    // ── Source scans: the wiring a unit test cannot reach ────────────────────────────────────────

    private static string MainWindowXaml()
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml"));

    private static string MainWindowCode()
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

    public static readonly string[] PanelIds =
    [
        "ds.document.code", "ds.document.settings",
        "ds.tool.canvas", "ds.tool.console", "ds.tool.findresults", "ds.tool.timeline",
        "ds.tool.projectbrowser", "ds.tool.outliner", "ds.tool.properties", "ds.tool.globalparameters",
    ];

    [Theory]
    [MemberData(nameof(AllPanelIds))]
    public void EveryPanelDeclaresItsContentIdExactlyOnce(string contentId)
    {
        var xaml = MainWindowXaml();
        var occurrences = xaml.Split($"ContentId=\"{contentId}\"").Length - 1;

        // A saved layout refers to panels by this id, so a duplicate makes the restore ambiguous and
        // a rename silently invalidates every user's saved arrangement.
        Assert.Equal(1, occurrences);
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void EveryToolPanelIsRegisteredInCodeBehind(string contentId)
    {
        // Catches "added a panel to the XAML, forgot to register it" — which produces a panel whose
        // menu entry does nothing and which no saved layout can restore.
        Assert.Contains($"\"{contentId}\"", MainWindowCode());
    }

    public static TheoryData<string> AllPanelIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in PanelIds) data.Add(id);
        return data;
    }

    public static TheoryData<string> AllToolIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in PanelIds.Where(i => i.StartsWith("ds.tool.", StringComparison.Ordinal)))
            data.Add(id);
        return data;
    }

    [Fact]
    public void TheCanvasPaneCannotAutoHide()
    {
        // A canvas that slid out of the edge on hover would be unusable, and it is one attribute away
        // from happening.
        var xaml = MainWindowXaml();
        var at = xaml.IndexOf("ContentId=\"ds.tool.canvas\"", StringComparison.Ordinal);
        Assert.True(at > 0);

        var declaration = xaml[Math.Max(0, at - 400)..at];
        Assert.Contains("CanAutoHide=\"False\"", declaration);
    }

    [Fact]
    public void PanelsHideRatherThanClose()
    {
        // Close removes an anchorable from the tree for good, so its Windows-menu entry would become a
        // one-way trip. Hide keeps it, remembering where it came from.
        var code = MainWindowCode();

        Assert.Contains("entry.Pane.Hide();", code);
        Assert.DoesNotContain("entry.Pane.Close();", code);
    }

    [Fact]
    public void ResetLayoutIsReachableFromBothEntryPoints()
    {
        var code = MainWindowCode();

        // The View menu item and the Ctrl+R key handler are separate call sites; before docking they
        // both went to a method that reset only two grid rows.
        Assert.Equal(2, code.Split("ResetLayoutToDefault();").Length - 1);
        Assert.DoesNotContain("ResetCanvasConsoleLayout", code);
    }

    [Fact]
    public void TheHandRolledLayoutMachineryIsGone()
    {
        var code = MainWindowCode();

        // Each of these was replaced by AvalonDock. They are listed individually so a merge that
        // resurrects one fails here rather than quietly reintroducing two layout systems.
        Assert.DoesNotContain("ConsoleSplitter_MouseMove", code);
        Assert.DoesNotContain("UpdateCanvasConsoleColumn", code);
        Assert.DoesNotContain("FloatPropertiesPanel", code);
        Assert.DoesNotContain("UpdateRightPanelVisibility", code);

        Assert.False(File.Exists(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "PropertiesWindow.xaml")),
            "PropertiesWindow was replaced by AvalonDock's own floating windows.");
    }

    [Fact]
    public void TheLayoutIsSavedOnCloseAndRestoredOnLoad()
    {
        var code = MainWindowCode();

        Assert.Contains("SaveLayout();", code);
        Assert.Contains("RestoreLayout();", code);
    }

    [Fact]
    public void TheCanvasOverlayStillTravelsWithTheCanvas()
    {
        // CanvasNavPanel and AnimationControlsPanel are siblings of RenderCanvas inside the pane's
        // Grid, which is what keeps them out of the exported bitmap and moves them with a floated
        // canvas. Guarded because the restructure moved this whole block.
        var xaml = MainWindowXaml();
        var pane = xaml.IndexOf("ContentId=\"ds.tool.canvas\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</avalonDock:LayoutAnchorable>", pane, StringComparison.Ordinal);

        var content = xaml[pane..end];
        Assert.Contains("x:Name=\"RenderCanvas\"", content);
        Assert.Contains("x:Name=\"CanvasNavPanel\"", content);
        Assert.Contains("x:Name=\"AnimationControlsPanel\"", content);
    }
}
