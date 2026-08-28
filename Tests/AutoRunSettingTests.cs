using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// Auto-Run re-executes the project's code on a 500 ms timer instead of waiting for F5. The flag
/// lives on the project file, so the property that matters here is that it survives a round trip —
/// "persistent across sessions" is the whole request, and a setting that is written but not read
/// back is the defect the app already carries for <c>AppDefaultCanvasBackground</c>.
///
/// <para>
/// The timer itself needs a window and cannot be driven from a test worker; the wiring is pinned by
/// source scan in <see cref="AutoUpdateRemovalTests"/>.
/// </para>
/// </summary>
public class AutoRunSettingTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "DoodleSharpAutoRunTests", Guid.NewGuid().ToString("N"));

    public AutoRunSettingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    [Fact]
    public void AutoRunSurvivesASaveAndLoad()
    {
        var path = Path_("armed.vizproj");

        var project = new VizProjectFile { Name = "Armed" };
        project.Settings.AutoRun = true;
        project.Save(path);

        Assert.True(VizProjectFile.Load(path).Settings.AutoRun);
    }

    [Fact]
    public void AProjectThatNeverMentionsAutoRunLoadsAsOff()
    {
        // The default has to be "off" for a project that predates the setting as well as for one that
        // simply never turned it on — nothing may start recompiling twice a second on its own.
        var path = Path_("silent.vizproj");
        File.WriteAllText(path, """{"Name":"Silent","Packages":[],"References":[],"Settings":{}}""");

        var loaded = VizProjectFile.Load(path);

        Assert.Null(loaded.Settings.AutoRun);
        Assert.NotEqual(true, loaded.Settings.AutoRun);
    }

    [Fact]
    public void TurningAutoRunOffLeavesNothingArmedBehind()
    {
        var path = Path_("disarmed.vizproj");

        var project = new VizProjectFile { Name = "Disarmed" };
        project.Settings.AutoRun = true;
        project.Save(path);

        // The toggle writes null rather than false when switched off, so the key does not linger in
        // every project file that ever had it enabled once.
        var reopened = VizProjectFile.Load(path);
        reopened.Settings.AutoRun = null;
        reopened.Save(path);

        Assert.NotEqual(true, VizProjectFile.Load(path).Settings.AutoRun);
    }

    [Fact]
    public void AutoRunIsIndependentOfTheOtherProjectSettings()
    {
        // It sits in the same object as the canvas colour and the dimension style, and those are
        // written back wholesale by Save Settings — so arming Auto-Run must not disturb them, and
        // saving them must not disarm it.
        var path = Path_("mixed.vizproj");

        var project = new VizProjectFile { Name = "Mixed" };
        project.Settings.AutoRun = true;
        project.Settings.DefaultCanvasBackgroundColor = "#101010";
        project.Settings.DefaultLineWeight = 3;
        project.Save(path);

        var loaded = VizProjectFile.Load(path);

        Assert.True(loaded.Settings.AutoRun);
        Assert.Equal("#101010", loaded.Settings.DefaultCanvasBackgroundColor);
        Assert.Equal(3, loaded.Settings.DefaultLineWeight);
    }

    // -- Standing down: a tick that would destroy more than it refreshes ----------------------
    //
    // Source scans, for the same reason the timer wiring is one: driving a tick needs a window and a
    // dispatcher. What they pin is the *shape* of the guard, which is the part that regressed -- the
    // tick used to ask only "did the source change", and answered a failing interactive program by
    // full-compiling it twice a second forever.

    private static string MainWindowSource() =>
        File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

    /// <summary>
    /// An unchanged-source tick consults <c>ShouldStandDown()</c> and returns without running when it
    /// says so. A proximity match, so the guard cannot drift below the run it guards.
    /// </summary>
    [Fact]
    public void AnUnchangedSourceTickAsksWhetherToStandDown()
    {
        Assert.Matches(
            @"private async void AutoRunTimer_Tick[\s\S]{0,1000}ShouldStandDown\(\)[\s\S]{0,200}return;",
            MainWindowSource());
    }

    /// <summary>
    /// The two states that stand a tick down. Interactive mode is asked via
    /// <c>IsCanvasInteractive</c> rather than a second <c>Mouse.HasHandlers</c> read, so note 95 keeps
    /// one definition of "the program is interactive".
    /// </summary>
    [Fact]
    public void StandingDownCoversInteractiveModeAndAFailedSource()
    {
        Assert.Contains(
            "private bool ShouldStandDown() => IsCanvasInteractive || _autoRunSourceFailed;",
            MainWindowSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The latch is cleared on every success and wherever the signature is dropped, or a project that
    /// failed once would never auto-run again. <c>ApplyAutoRunSetting</c> runs on every settings load,
    /// which is every project open (note 121).
    /// </summary>
    [Fact]
    public void TheFailureLatchIsClearedOnSuccessAndOnEveryProjectOpen()
    {
        var code = MainWindowSource();

        Assert.Matches(@"_lastAutoRunSignature = null;\s*\r?\n\s*_autoRunSourceFailed = false;", code);

        // Four: the reset above, both silent run paths' success arms (full compile and resident
        // re-invoke), and the manual Run button's. The last one matters on its own -- a failure that
        // was environmental rather than textual is fixed by pressing Run, not by editing, and
        // without it Auto-Run stayed stood down through a run that had plainly just succeeded.
        Assert.Equal(4, Regex.Matches(code, @"_autoRunSourceFailed = false;").Count);
        Assert.Matches(@"private async void RunButton_Click[\s\S]{0,3000}_autoRunSourceFailed = false;", code);
    }

    /// <summary>
    /// A run that compiled and then threw carries no diagnostics, so counting error diagnostics
    /// reported nothing at all -- the status bar read "Ready" while <c>Main()</c> died on every tick.
    /// The silent paths now write <c>result.Error</c> to the console, as the Run button always has.
    /// </summary>
    [Fact]
    public void ASilentRunThatThrewIsReportedRatherThanCounted()
    {
        var report = Regex.Match(MainWindowSource(),
            @"private void ReportSilentRunFailure\(string label, Execution\.CompilationResult result\)[\s\S]{0,1400}?\n    \}");

        Assert.True(report.Success, "ReportSilentRunFailure is the one reporting point for a failed silent run.");
        Assert.Contains("WriteError(label, 0, result.Error!)", report.Value, StringComparison.Ordinal);
        Assert.Contains("LatchSilentRunFailure(label)", report.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// The interactive pause is announced, and announced once. A tick that stands down produces no
    /// other visible effect at all, so without this the feature is indistinguishable from the app
    /// having quietly broken -- and announcing per tick would refill the console at 2 Hz, which is
    /// the flicker note 143 exists to remove.
    /// </summary>
    [Fact]
    public void TheInteractivePauseIsAnnouncedOncePerEpisode()
    {
        var code = MainWindowSource();

        var announce = Regex.Match(code,
            @"private void AnnounceInteractivePause\(\)[\s\S]{0,900}?\n    \}");
        Assert.True(announce.Success, "AnnounceInteractivePause is where the pause is reported.");

        // Latched, so the same sentence cannot arrive twice a second.
        Assert.Contains("if (_interactivePauseAnnounced || !IsCanvasInteractive) return;",
            announce.Value, StringComparison.Ordinal);
        Assert.Contains("your code is handling the mouse", announce.Value, StringComparison.Ordinal);

        // Cleared in exactly two places -- every project open, and any tick that actually runs.
        // Never per tick, or the latch would be pointless.
        Assert.Equal(2, Regex.Matches(code, @"_interactivePauseAnnounced = false;").Count);
    }

    /// <summary>
    /// Standing down must not touch the user's setting. Unticking the checkbox would write
    /// <c>AutoRun: null</c> into the .vizproj behind their back, and stopping the timer would lose
    /// the edit-then-re-run loop -- the timer is also the change detector (note 143).
    /// </summary>
    [Fact]
    public void StandingDownNeitherStopsTheTimerNorUnticksTheBox()
    {
        var tick = Regex.Match(MainWindowSource(),
            @"private async void AutoRunTimer_Tick\(object\? sender, EventArgs e\)[\s\S]{0,2600}?\n    \}\r?\n");
        Assert.True(tick.Success, "AutoRunTimer_Tick body not found.");

        var standDown = tick.Value[..tick.Value.IndexOf("_interactivePauseAnnounced = false;", StringComparison.Ordinal)];

        Assert.DoesNotContain("AutoRunCheck", standDown, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.AutoRun =", standDown, StringComparison.Ordinal);
        // The one Stop() in the tick is the "the project turned it off" path, above the guard.
        Assert.DoesNotContain("_autoRunTimer?.Stop();", standDown[standDown.IndexOf("ShouldStandDown", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }
}
