using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Auto-update is gone: code runs on F5 / Run only. Both settings — "Auto-update Canvas" and
/// "Auto-Draw Shapes" — were removed along with the debounce timer that drove the first and the
/// <c>Shape.AutoRegister</c> assignments that drove the second.
///
/// <para>
/// This is note 98's audit shape: a retired setting leaves debris in four places at once (the
/// settings model, the markup, the handlers, and whatever read it), and a half-removal is silent —
/// a control with no reader, or a reader with no control, both compile. Two implementations had to
/// be cleaned, because <c>SharedEditorController</c> is a parallel copy of the main window's editor
/// wiring (note 43) and its auto-update path would otherwise have regrown the feature.
/// </para>
/// </summary>
public class AutoUpdateRemovalTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relative));

    private static readonly string[] Surfaces =
    {
        "MainWindow.xaml",
        "MainWindow.xaml.cs",
        "ApplicationSettings.cs",
        Path.Combine("Editor", "SharedEditorController.cs"),
    };

    /// <summary>
    /// The settings keys themselves. <c>ApplicationSettings.Load</c> uses default
    /// <c>JsonSerializerOptions</c>, which ignore unknown members, so an <c>appsettings.json</c>
    /// still carrying them deserializes fine and drops them on the next save (note 98) — no
    /// migration, and nothing may read them again.
    /// </summary>
    [Theory]
    [InlineData("AutoUpdateCanvas")]
    [InlineData("AutoUpdateDelayMs")]
    [InlineData("AutoDraw")]
    public void TheRetiredSettingsKeysAreGone(string key)
    {
        foreach (var surface in Surfaces)
        {
            Assert.DoesNotContain(key, Read(surface), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The controls, their handlers, the timer and its suppression flag. Named individually rather
    /// than by a pattern, because each is a distinct way the feature could come back.
    /// </summary>
    [Theory]
    [InlineData("SettingsAutoUpdateCanvasCheck")]
    [InlineData("AutoUpdateCheck")]
    [InlineData("_autoUpdateTimer")]
    [InlineData("_suppressAutoUpdate")]
    [InlineData("IsAutoUpdateEnabled")]
    [InlineData("GetAutoUpdateDelayMs")]
    public void TheAutoUpdateMachineryIsGone(string symbol)
    {
        foreach (var surface in Surfaces)
        {
            Assert.DoesNotContain(symbol, Read(surface), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// <c>Shape.AutoRegister</c> keeps its <c>true</c> default and the host no longer writes it.
    ///
    /// <para>
    /// Deliberately not replaced with <c>Shape.AutoRegister = true</c>: that would add a second
    /// writer to a flag owned by <c>AutoRegisterScope</c> and the Chart helper, and an assignment
    /// landing inside a nested scope would defeat it. The absence is the fix.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHostNoLongerWritesShapeAutoRegister()
    {
        Assert.DoesNotContain("Shape.AutoRegister =", Read("MainWindow.xaml.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The silent-run path survives the removal, because Global Parameters needs it: a slider drag
    /// re-executes the program without anyone pressing Run (note 37). It is renamed, because a
    /// method still called <c>AutoRunCodeAsync</c> would send the next reader hunting for a setting
    /// that no longer exists.
    /// </summary>
    [Fact]
    public void TheSilentRunPathSurvivesUnderItsNewName()
    {
        var code = Read("MainWindow.xaml.cs");

        Assert.DoesNotContain("AutoRunCodeAsync", code, StringComparison.Ordinal);
        Assert.Contains("private async Task RunSilentlyAsync()", code, StringComparison.Ordinal);

        // Exactly the two Global Parameters callers: no resident assembly, and after a write-back.
        Assert.Equal(2, Regex.Matches(code, @"await RunSilentlyAsync\(\);").Count);
    }

    /// <summary>
    /// The negative control. Without it the scans above would pass just as happily against a file
    /// that had been emptied or renamed out from under them.
    /// </summary>
    [Fact]
    public void TheScannedSurfacesAreStillTheRealOnes()
    {
        Assert.Contains("SettingsZoomToFitCheck", Read("MainWindow.xaml"), StringComparison.Ordinal);
        Assert.Contains("ZoomToFitOnRun", Read("ApplicationSettings.cs"), StringComparison.Ordinal);
        Assert.Contains("SettingsUiBusy", Read("MainWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("PerformWorkspaceSyntaxCheckAsync", Read(Path.Combine("Editor", "SharedEditorController.cs")), StringComparison.Ordinal);
    }
}
