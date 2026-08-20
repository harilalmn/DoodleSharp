using System;
using System.IO;
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
}
