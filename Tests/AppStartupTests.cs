using System;
using System.IO;

namespace DoodleSharp.Tests;

/// <summary>
/// Tests for how the app interprets its command line. Double-clicking a <c>.vizproj</c> in Explorer
/// launches <c>DoodleSharp.exe "&lt;path&gt;"</c> — the installer registers that association — so the
/// argument has to be recognised, or the user lands on the welcome screen instead of their project.
/// </summary>
public class AppStartupTests : IDisposable
{
    private readonly string _dir;

    public AppStartupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ds_args_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void FindsAnExistingProjectArgument()
    {
        var path = MakeFile("Demo.vizproj");
        Assert.Equal(path, App.FindProjectArgument(new[] { path }));
    }

    [Fact]
    public void MatchesTheExtensionCaseInsensitively()
    {
        var path = MakeFile("Demo.VIZPROJ");
        Assert.Equal(path, App.FindProjectArgument(new[] { path }));
    }

    [Fact]
    public void ResolvesRelativePathsToFullPaths()
    {
        var path = MakeFile("Relative.vizproj");
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _dir;
            var found = App.FindProjectArgument(new[] { "Relative.vizproj" });
            Assert.Equal(path, found);
            Assert.True(Path.IsPathRooted(found));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void SkipsArgumentsThatAreNotProjects()
    {
        var cs = MakeFile("Module.cs");
        var project = MakeFile("Demo.vizproj");

        // Switches and unrelated files must not be mistaken for the project to open.
        Assert.Equal(project, App.FindProjectArgument(new[] { "--flag", cs, project }));
    }

    [Fact]
    public void ReturnsNullWhenTheProjectDoesNotExist()
    {
        Assert.Null(App.FindProjectArgument(new[] { Path.Combine(_dir, "Gone.vizproj") }));
    }

    [Fact]
    public void ReturnsNullForNoArguments()
    {
        Assert.Null(App.FindProjectArgument(null));
        Assert.Null(App.FindProjectArgument(Array.Empty<string>()));
        Assert.Null(App.FindProjectArgument(new[] { "", "   " }));
    }

    [Fact]
    public void SurvivesAMalformedPathArgument()
    {
        // Explorer will not produce this, but a hand-typed command line can.
        var exception = Record.Exception(() => App.FindProjectArgument(new[] { "bad|path?.vizproj" }));
        Assert.Null(exception);
    }
}
