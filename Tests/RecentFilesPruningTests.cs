using System.IO;
using System.Linq;
using DoodleSharp.Project;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Verifies the welcome-screen recent-projects list never surfaces files that have been
/// deleted/moved after the app loaded — the getters prune on every read, not
/// only in the static-constructor Load().
/// </summary>
public class RecentFilesPruningTests
{
    [Fact]
    public void GetRecentProjects_OmitsFilesDeletedAfterLoad()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"ds_test_{Path.GetRandomFileName()}.vizproj");
        File.WriteAllText(temp, "{}");
        try
        {
            RecentProjectsManager.AddProject(temp, "TempProj");
            Assert.Contains(RecentProjectsManager.GetRecentProjects(), p =>
                p.Path.Equals(temp, System.StringComparison.OrdinalIgnoreCase));

            // Simulate the file being deleted while the app is already running.
            File.Delete(temp);

            // The getter must now omit it (and not require an app restart).
            Assert.DoesNotContain(RecentProjectsManager.GetRecentProjects(), p =>
                p.Path.Equals(temp, System.StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            RecentProjectsManager.RemoveProject(temp);
        }
    }
}
