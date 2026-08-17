using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using DoodleSharp.Diagnostics;
using DoodleSharp.Project;

namespace DoodleSharp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // FIRST statement: the journal has to be open before anything else can fail.
            // AppDiagnostics.Install also attaches the global crash handlers and the UI watchdog.
            AppDiagnostics.Install(this, "DoodleSharp", e.Args);

            base.OnStartup(e);

            Journal.Info("APP.STARTUP", "Application starting", $"args={e.Args.Length}");

            // Double-clicking a .vizproj (or any other shell "open with") passes the path here. The
            // installer already registers the association and passes "%1"; before this the argument
            // was ignored and the user was dropped on the welcome screen instead of their project.
            var projectPath = FindProjectArgument(e.Args);
            if (projectPath != null && TryOpenProject(projectPath))
                return;

            var welcome = new WelcomeWindow();
            welcome.Show();

            Journal.Info("APP.WELCOME.SHOWN", "Welcome window shown");
        }

        /// <summary>
        /// First argument that names an existing <c>.vizproj</c> file, or null.
        /// </summary>
        internal static string? FindProjectArgument(string[]? args)
        {
            if (args == null) return null;

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (!arg.EndsWith(".vizproj", System.StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var full = Path.GetFullPath(arg);
                    if (File.Exists(full)) return full;

                    Journal.Warn("APP.ARG.MISSING", "Project argument does not exist", $"path={full}");
                }
                catch (System.Exception ex)
                {
                    Journal.Warn("APP.ARG.BAD_PATH", "Project argument could not be resolved", $"arg={arg}", ex);
                }
            }

            return null;
        }

        /// <summary>
        /// Opens a project straight into the main window. Returns false if it could not be loaded, in
        /// which case start-up falls through to the welcome screen rather than leaving no window at
        /// all — a corrupt or moved project should not make the app appear to do nothing.
        /// </summary>
        private bool TryOpenProject(string projectPath)
        {
            try
            {
                Journal.Info("APP.ARG.OPEN", "Opening project from the command line", Journal.DescribeFile(projectPath));

                var project = VizCodeProject.Load(projectPath);
                RecentProjectsManager.AddProject(project.ProjectFilePath, project.ProjectFile.Name);

                var main = new MainWindow(project);
                main.Show();
                return true;
            }
            catch (System.Exception ex)
            {
                Journal.Error("APP.ARG.OPEN_FAIL", "Project from the command line failed to load", ex,
                    $"path={projectPath}");

                MessageBox.Show(
                    $"Could not open '{Path.GetFileName(projectPath)}':\n\n{ex.Message}",
                    "DoodleSharp", MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }
        }
    }
}
