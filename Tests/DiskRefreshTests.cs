using System;
using System.IO;
using System.Linq;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// External edits to files that are already open.
///
/// <para>
/// <c>RefreshFilesFromDisk</c> added files new to the project and dropped files deleted from disk,
/// but never re-read the content of a file it already had — so an edit made by another editor, a
/// git checkout or a tool was silently ignored while the status bar reported "Project refreshed
/// from disk". The conflict case is the one with teeth: a background watcher tick must never
/// discard what the user typed.
/// </para>
/// </summary>
public class DiskRefreshTests : IDisposable
{
    private readonly string _dir;

    public DiskRefreshTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "C2VDiskRefresh_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private VizCodeProject NewProject()
    {
        var project = VizCodeProject.CreateNew(_dir, "RefreshTest");
        project.SaveAllFiles();
        return project;
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static VizCodeFile FileNamed(VizCodeProject p, string name) =>
        p.Files.First(f => f.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AnExternalEditToAnOpenFileIsReadBackIn()
    {
        var project = NewProject();
        WriteFile("Helper.cs", "// original");
        project.RefreshFilesFromDisk();

        WriteFile("Helper.cs", "// edited outside the app");
        var result = project.RefreshFilesFromDisk();

        Assert.Contains(result.Reloaded, f => f.FileName == "Helper.cs");
        Assert.Equal("// edited outside the app", FileNamed(project, "Helper.cs").Content);
    }

    [Fact]
    public void UnsavedEditsAreNeverOverwritten()
    {
        var project = NewProject();
        WriteFile("Helper.cs", "// original");
        project.RefreshFilesFromDisk();

        var open = FileNamed(project, "Helper.cs");
        open.Content = "// what the user typed";
        open.HasUnsavedChanges = true;

        WriteFile("Helper.cs", "// what some other tool wrote");
        var result = project.RefreshFilesFromDisk();

        Assert.Contains(result.Conflicted, f => f.FileName == "Helper.cs");
        Assert.DoesNotContain(result.Reloaded, f => f.FileName == "Helper.cs");
        Assert.Equal("// what the user typed", open.Content);   // the user's work survives
    }

    [Fact]
    public void AnUnchangedFileIsNotReportedAsReloaded()
    {
        // The watcher fires on our own saves. Treating that as a change would reset the editor and
        // jump the caret on every Ctrl+S.
        var project = NewProject();
        WriteFile("Helper.cs", "// same");
        project.RefreshFilesFromDisk();

        var result = project.RefreshFilesFromDisk();

        Assert.Empty(result.Reloaded);
        Assert.Empty(result.Conflicted);
        Assert.False(result.AnythingChanged);
    }

    [Fact]
    public void ANewFileOnDiskIsStillAdded()
    {
        var project = NewProject();
        WriteFile("Added.cs", "// new");

        var result = project.RefreshFilesFromDisk();

        Assert.Contains(result.Added, f => f.FileName == "Added.cs");
        Assert.Equal("// new", FileNamed(project, "Added.cs").Content);
    }

    [Fact]
    public void ADeletedFileIsStillRemoved()
    {
        var project = NewProject();
        var path = WriteFile("Doomed.cs", "// bye");
        project.RefreshFilesFromDisk();

        File.Delete(path);
        var result = project.RefreshFilesFromDisk();

        Assert.Contains(result.Removed, f => f.FileName == "Doomed.cs");
        Assert.DoesNotContain(project.Files, f => f.FileName == "Doomed.cs");
    }

    [Fact]
    public void ADeletedFileWithUnsavedChangesIsKept()
    {
        var project = NewProject();
        var path = WriteFile("Precious.cs", "// on disk");
        project.RefreshFilesFromDisk();

        var open = FileNamed(project, "Precious.cs");
        open.Content = "// unsaved work";
        open.HasUnsavedChanges = true;

        File.Delete(path);
        var result = project.RefreshFilesFromDisk();

        Assert.DoesNotContain(result.Removed, f => f.FileName == "Precious.cs");
        Assert.Contains(project.Files, f => f.FileName == "Precious.cs");
        Assert.Equal("// unsaved work", open.Content);
    }

    [Fact]
    public void ReloadingClearsNothingElse()
    {
        // A reloaded file must come back clean — it now matches disk exactly, so leaving the dirty
        // flag set would make the next save write back what we just read.
        var project = NewProject();
        WriteFile("Helper.cs", "// original");
        project.RefreshFilesFromDisk();

        WriteFile("Helper.cs", "// changed");
        project.RefreshFilesFromDisk();

        Assert.False(FileNamed(project, "Helper.cs").HasUnsavedChanges);
    }
}
