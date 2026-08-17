using System.IO;
using System.Windows;
using Microsoft.Win32;
using DoodleSharp.Diagnostics;
using DoodleSharp.Project;

namespace DoodleSharp
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
            LoadRecentProjects();
        }

        // ── Recent projects ───────────────────────────────────────────────────

        private void LoadRecentProjects()
        {
            var recentProjects = RecentProjectsManager.GetRecentProjects();
            RecentProjectsList.ItemsSource = recentProjects;

            if (recentProjects.Count == 0)
            {
                RecentProjectsList.Visibility = Visibility.Collapsed;
                NoRecentProjectsText.Visibility = Visibility.Visible;
            }
            else
            {
                RecentProjectsList.Visibility = Visibility.Visible;
                NoRecentProjectsText.Visibility = Visibility.Collapsed;
            }
        }

        private void RecentProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (RecentProjectsList.SelectedItem == null) return;
            OpenSelectedRecentProject();
        }

        private void OpenSelectedRecentProject()
        {
            if (RecentProjectsList.SelectedItem is not RecentProject project) return;

            Journal.Info("WELCOME.RECENT.OPEN", "Opening recent project", Journal.DescribeFile(project.Path));

            if (File.Exists(project.Path))
            {
                try
                {
                    var loadedProject = VizCodeProject.Load(project.Path);
                    RecentProjectsManager.AddProject(project.Path, loadedProject.ProjectFile.Name);
                    OpenMainWindow(loadedProject);
                }
                catch (Exception ex)
                {
                    Journal.Error("WELCOME.RECENT.FAIL", "Recent project failed to load", ex, $"path={project.Path}");
                    MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    RecentProjectsManager.RemoveProject(project.Path);
                    LoadRecentProjects();
                }
            }
            else
            {
                MessageBox.Show("Project file no longer exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                RecentProjectsManager.RemoveProject(project.Path);
                LoadRecentProjects();
            }
        }

        // ── New / Open buttons ────────────────────────────────────────────────

        private void NewProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewProjectDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                var fullPath = dialog.FullPath;
                Journal.Info("WELCOME.PROJECT.NEW", "Creating new project", $"dir={fullPath} name={dialog.ProjectName}");
                try
                {
                    var project = VizCodeProject.CreateNew(fullPath, dialog.ProjectName);
                    OpenMainWindow(project);
                }
                catch (Exception ex)
                {
                    Journal.Error("WELCOME.PROJECT.NEW_FAIL", "Project creation failed", ex, $"dir={fullPath}");
                    MessageBox.Show($"Failed to create project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "DoodleSharp Project (*.vizproj)|*.vizproj",
                Title = "Open Project"
            };

            if (dialog.ShowDialog() == true)
            {
                Journal.Info("WELCOME.PROJECT.OPEN", "Opening project from dialog", Journal.DescribeFile(dialog.FileName));
                try
                {
                    var project = VizCodeProject.Load(dialog.FileName);
                    OpenMainWindow(project);
                }
                catch (Exception ex)
                {
                    Journal.Error("WELCOME.PROJECT.OPEN_FAIL", "Project failed to load", ex, $"path={dialog.FileName}");
                    MessageBox.Show($"Failed to open project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenMainWindow(VizCodeProject project)
        {
            Journal.Info("WELCOME.MAIN.OPEN", "Handing project to the main window",
                $"project={project.ProjectFilePath} files={project.Files.Count}");
            RecentProjectsManager.AddProject(project.ProjectFilePath, project.ProjectFile.Name);

            var mainWindow = new MainWindow(project);
            mainWindow.Show();
            this.Close();
        }
    }
}
