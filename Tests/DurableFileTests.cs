using System;
using System.IO;
using System.Linq;
using System.Text;
using DoodleSharp.Project;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="DurableFile"/> — the write that cannot leave a half-file behind.
///
/// <para>
/// Auto-save rewrites every one of the user's source files on a timer, so the app spends a fraction
/// of every minute inside <c>File.WriteAllText</c>'s truncate-then-stream window, on files it did
/// not author and cannot reconstruct. These tests pin the two properties that matter: the content
/// round-trips, and a failed write leaves the original exactly as it was.
/// </para>
/// </summary>
public class DurableFileTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DoodleSharpDurable_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void WritesANewFile()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "new.cs");
            DurableFile.WriteAllText(path, "hello");

            Assert.Equal("hello", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReplacesAnExistingFile()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "existing.cs");
            File.WriteAllText(path, "old content that is quite long");

            DurableFile.WriteAllText(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CreatesTheDirectoryIfItIsMissing()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "nested", "deeper", "file.json");
            DurableFile.WriteAllText(path, "{}");

            Assert.Equal("{}", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LeavesNoTemporaryFilesBehind()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "tidy.cs");
            DurableFile.WriteAllText(path, "one");
            DurableFile.WriteAllText(path, "two");
            DurableFile.WriteAllText(path, "three");

            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// The property the whole class exists for. A write that cannot complete must not have consumed
    /// the previous content on its way to failing.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesTheOriginalIntact()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "precious.cs");
            File.WriteAllText(path, "the user's work");

            // A directory where the temporary file wants to be: the write fails, the target must not.
            var blocker = Directory.GetFiles(dir);
            Assert.Single(blocker);

            // Make the target read-only, which is the realistic form of "this write cannot land".
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                Assert.ThrowsAny<Exception>(() => DurableFile.WriteAllText(path, "replacement"));
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            Assert.Equal("the user's work", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));   // and no temporary file left over
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void HonoursAnExplicitEncoding()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "ascii.dxf");
            DurableFile.WriteAllText(path, "AC1009", Encoding.ASCII);

            Assert.Equal("AC1009", File.ReadAllText(path));
            Assert.Equal(6, new FileInfo(path).Length);   // no byte-order mark
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>
    /// Every write that lands on a file the <b>user</b> owns has to go through here. A new call site
    /// added later would otherwise reintroduce exactly the window this class closes, and nothing
    /// about a plain <c>File.WriteAllText</c> looks wrong at a glance.
    /// </summary>
    [Theory]
    [InlineData("Project/VizCodeProject.cs")]
    [InlineData("Project/VizProjectFile.cs")]
    [InlineData("Project/RecentProjectsManager.cs")]
    [InlineData("ApplicationSettings.cs")]
    public void UserOwnedFilesAreWrittenDurably(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relativePath));

        Assert.DoesNotContain("File.WriteAllText(", source.Replace("DurableFile.WriteAllText(", ""));
        Assert.Contains("DurableFile.WriteAllText(", source);
    }
}
