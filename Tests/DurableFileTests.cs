using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    /// <summary>
    /// Removes a scratch directory without letting the removal fail a test.
    /// </summary>
    /// <remarks>
    /// Closing a handle on Windows does not always make the file deletable on the very next
    /// instruction — the directory entry can linger, and a scanner on a CI runner opens new files of
    /// its own. The tests that deliberately hold a file open therefore lost to their own cleanup:
    /// every assertion passed and the <c>finally</c> threw. What is under test is the write, not the
    /// tidying up.
    /// </remarks>
    private static void Cleanup(string dir)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(dir, true);
                return;
            }
            catch (IOException)
            {
                System.Threading.Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(50);
            }
        }
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
    /// The rename is the step that loses races it did not enter, and losing one must not fail the
    /// save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default projects folder on this developer's machine lives under OneDrive, and OneDrive
    /// opens a file for a moment after it changes. <c>File.Replace</c> hitting that window fails with
    /// ERROR_UNABLE_TO_REMOVE_REPLACED (0x80070497) even though the write itself was fine, and it
    /// took the whole app down: the exception escaped <c>MainWindow.AutoRunCheck_Changed</c>, which
    /// had no handler, and reached the WPF dispatcher — the user was only unticking Auto-Run.
    /// </para>
    ///
    /// <para>
    /// A file held open while the write is in flight is that situation exactly. The file is released
    /// from inside the retry callback rather than after a delay: the first attempt has then provably
    /// failed before the second can succeed, and the test asserts nothing about elapsed time. The
    /// delay-based version of this test passed locally for weeks and failed the 2026.8.15 release
    /// build, because a <c>Task.Delay(30)</c> on a loaded runner outlasts the whole retry budget.
    /// </para>
    /// </remarks>
    [Fact]
    public void RetriesTheRenameUntilTheDestinationIsLetGo()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "held.vizproj");
        var holder = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var retries = 0;

        try
        {
            holder.Write(Encoding.UTF8.GetBytes("the previous settings"));
            holder.Flush();

            // Release the file from INSIDE the retry callback. That is what makes this test
            // independent of how fast the machine is: the first attempt has provably failed by the
            // time the callback runs, and the next one then finds the file free however long the
            // scheduler took to get there. Reading a clock instead cost a release — see the seam's
            // own remarks in DurableFile.
            DurableFile.RenameRetrying = attempt =>
            {
                retries++;
                if (attempt == 1) holder.Dispose();
            };

            DurableFile.WriteAllText(path, "the new settings");

            Assert.True(retries >= 1, "the rename succeeded first time, so nothing was retried");
            Assert.Equal("the new settings", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));   // and no temporary file left over
        }
        finally
        {
            DurableFile.RenameRetrying = null;
            holder.Dispose();
            Cleanup(dir);
        }
    }

    /// <summary>
    /// And the retry is bounded: a destination that is never let go fails, having genuinely spent the
    /// budget rather than returning on the first refusal. This is the timing-free half of the pair —
    /// it asserts a lower bound on elapsed time, which no amount of machine load can break.
    /// </summary>
    [Fact]
    public void GivesUpOnADestinationThatIsNeverLetGo()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "wedged.vizproj");
            File.WriteAllText(path, "the previous settings");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Assert.ThrowsAny<IOException>(() => DurableFile.WriteAllText(path, "the new settings"));
                clock.Stop();

                // 20+40+80+160+320 ms of backoff. A single attempt would have thrown at once.
                Assert.True(clock.ElapsedMilliseconds >= 400, $"gave up after {clock.ElapsedMilliseconds} ms");
            }

            Assert.Equal("the previous settings", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));   // the temporary file was cleaned up
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The retry is for a file that is <em>busy</em>. A destination that cannot be written at all
    /// still fails at once — waiting does not make a read-only file writable, and a save dialog that
    /// hangs for half a second before saying no is worse than one that says no.
    /// </summary>
    [Fact]
    public void DoesNotRetryAFailureThatWaitingCannotFix()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "locked-down.cs");
            File.WriteAllText(path, "the user's work");
            File.SetAttributes(path, FileAttributes.ReadOnly);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Assert.ThrowsAny<Exception>(() => DurableFile.WriteAllText(path, "replacement"));
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            // The full retry budget is ~620 ms; this must not have spent any of it.
            Assert.True(clock.ElapsedMilliseconds < 300, $"took {clock.ElapsedMilliseconds} ms");
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
