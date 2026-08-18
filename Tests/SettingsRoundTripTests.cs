using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Every value on the Settings tab must survive a round trip: shown from the saved state, written
/// back when changed, and still there on the next launch.
///
/// <para>
/// The failures here are silent — a setting reverts and nothing errors — and they need a real window
/// to observe, so these are source scans. They exist because Auto Draw Shapes could not be turned
/// off: <c>AutoUpdateCheck</c> declared <c>IsChecked="True"</c> in the markup, and
/// <c>InitializeComponent</c> raising Checked ran the settings handler, which wrote the markup's
/// value over the saved one and persisted it — before the file was ever read.
/// </para>
/// </summary>
public class SettingsRoundTripTests
{
    private static string Xaml() => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml"));
    private static string Code() => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

    /// <summary>The Settings document's markup, which is where the tab's controls are declared.</summary>
    private static string SettingsSection()
    {
        var xaml = Xaml();
        var a = xaml.IndexOf("ContentId=\"ds.document.settings\"", StringComparison.Ordinal);
        var b = xaml.IndexOf("</avalonDock:LayoutDocument>", a, StringComparison.Ordinal);
        Assert.True(a > 0 && b > a, "the Settings document must be findable in the markup");
        return xaml[a..b];
    }

    /// <summary>Named controls that carry a value — colour swatch buttons and labels do not.</summary>
    private static string[] ValueControls() =>
        Regex.Matches(SettingsSection(), @"x:Name=""(\w+)""")
             .Select(m => m.Groups[1].Value)
             .Where(n => !n.EndsWith("Btn", StringComparison.Ordinal) && !n.EndsWith("Text", StringComparison.Ordinal))
             .Distinct()
             .OrderBy(n => n, StringComparer.Ordinal)
             .ToArray();

    private static string MethodBody(string code, string signature)
    {
        var i = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i > 0, $"{signature} must exist");

        var k = code.IndexOf('{', i);
        var depth = 0;
        for (; k < code.Length; k++)
        {
            if (code[k] == '{') depth++;
            else if (code[k] == '}' && --depth == 0) break;
        }
        return code[i..Math.Min(k + 1, code.Length)];
    }

    [Fact]
    public void EverySettingsControlIsShownAndWrittenBack()
    {
        var code = Code();
        var load = MethodBody(code, "private void LoadSettingsToUI()");

        foreach (var name in ValueControls())
        {
            Assert.True(load.Contains(name, StringComparison.Ordinal),
                $"{name} is never populated from the saved settings, so the tab shows a stale value");

            // A write-back reads the control. Save-button and instant-save handlers both count;
            // the cast in `(int)HighlightOpacitySlider.Value` is why this is not anchored to '='.
            var written = Regex.IsMatch(code, $@"{name}\.(IsChecked|Text|SelectedIndex|SelectedItem|Value)\b");
            Assert.True(written, $"{name} is loaded but never read back, so a change to it is lost");
        }
    }

    [Fact]
    public void NoSettingsControlDeclaresItsValueInMarkup()
    {
        // The value belongs to the settings file. A starting value in markup both fights the loaded
        // value and — because InitializeComponent raises the change event — makes the handler
        // persist the markup's value over the user's. This is the Auto Draw Shapes bug exactly.
        foreach (Match m in Regex.Matches(Xaml(), @"<(?:CheckBox|ComboBox|RadioButton)\b[^>]*?>", RegexOptions.Singleline))
        {
            var element = m.Value;
            var declaresValue = Regex.IsMatch(element, @"\b(IsChecked|SelectedIndex)=""[^""]+""");
            var wiresHandler = Regex.IsMatch(element, @"\b(Checked|Unchecked|SelectionChanged)=""[^""]+""");

            if (declaresValue && wiresHandler)
            {
                var name = Regex.Match(element, @"x:Name=""(\w+)""");
                Assert.Fail($"{(name.Success ? name.Groups[1].Value : "<unnamed>")} declares a value in markup " +
                            "and wires a change handler; InitializeComponent will persist the markup value");
            }
        }
    }

    [Fact]
    public void EverySettingsHandlerIsGuardedAgainstStartupAndLoad()
    {
        // A handler that persists must not run while the markup or the loader is driving the control.
        // SaveSettingsButton_Click is exempt: it only ever runs because the user pressed it.
        var code = Code();
        var exempt = new[]
        {
            "SaveSettingsButton_Click", "ResetLayoutToDefault", "GridMenuItem_Click",
            "OnPaneVisibilityChanged", "ShowRibbonMenuItem_Click", "ShowMinimapMenuItem_Click",
            "MainWindow_PreviewKeyDown", "PersistDefaultColor",
        };

        foreach (Match m in Regex.Matches(code, @"private void (\w+)\([^)]*\)\s*\r?\n\s*\{"))
        {
            var name = m.Groups[1].Value;
            if (exempt.Contains(name)) continue;

            var body = MethodBody(code, m.Value.TrimEnd('{', '\r', '\n', ' '));
            if (!body.Contains("ApplicationSettings.Save()", StringComparison.Ordinal)) continue;

            Assert.True(body.Contains("SettingsUiBusy", StringComparison.Ordinal),
                $"{name} persists a setting without checking SettingsUiBusy");
        }
    }

    [Fact]
    public void LineTypeScaleHasNoZoomMode()
    {
        // Line weight and line type scale used to be two Absolute/Relative dropdowns, which exposed
        // four combinations when two were wanted — and the pair interacted, because WPF dash lengths
        // are multiples of pen thickness, so scaling the thickness stretched the dashes as a side
        // effect that had to be divided back out. Line type scale is now always absolute, with no
        // setting at all; only the thickness compensation survives in GetShapePen.
        Assert.DoesNotContain("LineTypeScaleRelativeToZoom", Code());
        Assert.DoesNotContain("LineTypeScaleRelativeToZoom", Xaml());
        Assert.DoesNotContain("LineTypeScaleRelativeToZoom",
            File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "ApplicationSettings.cs")));

        // The one switch that remains, off by default.
        var settings = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "ApplicationSettings.cs"));
        Assert.Contains("public bool DisplayLineWeight { get; set; } = false;", settings);
    }

    [Fact]
    public void ApplicationSettingsLoadIsNotGatedOnAProject()
    {
        // Application settings are global. They used to sit behind the same early return as the
        // per-project ones, so with no project open the tab showed markup defaults for all of them —
        // and Save writes every one back from the UI, so pressing it wiped the saved values.
        var code = Code();
        var load = MethodBody(code, "private void LoadSettingsToUI()");

        var appBlock = load.IndexOf("Load Application Settings", StringComparison.Ordinal);
        Assert.True(appBlock > 0, "the application-settings block must exist");

        Assert.DoesNotContain("return;", load[..appBlock]);

        // ...and the constructor must call it whether or not a project was passed.
        var ctor = MethodBody(code, "public MainWindow(VizCodeProject? project = null)");
        var call = ctor.IndexOf("LoadSettingsToUI();", StringComparison.Ordinal);
        var branch = ctor.IndexOf("if (project != null)", StringComparison.Ordinal);
        Assert.True(call > 0 && branch > 0);
        Assert.True(call > ctor.IndexOf("_settingsUiReady = true;", StringComparison.Ordinal) - 400,
            "LoadSettingsToUI must run before the handlers are armed");
        Assert.Contains("_settingsUiReady = true;", ctor);
    }
}
