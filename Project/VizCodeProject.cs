using System.IO;
using DoodleSharp.Diagnostics;

namespace DoodleSharp.Project;

public class VizCodeProject
{
    public string ProjectFilePath { get; private set; } = string.Empty;
    public string ProjectDirectory => Path.GetDirectoryName(ProjectFilePath) ?? string.Empty;
    public VizProjectFile ProjectFile { get; private set; } = new VizProjectFile();

    public List<VizCodeFile> Files { get; } = new();

    public VizCodeFile? EntryPointFile => Files.FirstOrDefault(f => f.IsEntryPoint);
    public bool HasUnsavedChanges => Files.Any(f => f.HasUnsavedChanges);

    // Expose config for compatibility (or refactor consumers)
    // Consumers accessed project.Config.Packages... now project.ProjectFile.Packages
    // I can alias it if needed, but better to update consumers.

    private VizCodeProject() { }

    public static VizCodeProject Load(string vizProjPath)
    {
        // Every file the app opens is journaled here with its size, timestamp and content hash —
        // this is the record that lets a shared journal be matched against the exact source that
        // was loaded when a crash happened.
        using var scope = Journal.Scope("PROJ.LOAD", "Loading project", Journal.DescribeFile(vizProjPath));

        if (!File.Exists(vizProjPath))
        {
            Journal.Error("PROJ.LOAD.MISSING", "Project file does not exist", null, $"path={vizProjPath}");
            throw new FileNotFoundException("Project file not found", vizProjPath);
        }

        var project = new VizCodeProject
        {
            ProjectFilePath = vizProjPath,
            ProjectFile = VizProjectFile.Load(vizProjPath)
        };

        Journal.Info("PROJ.LOAD.MANIFEST", "Project manifest parsed",
            $"name={project.ProjectFile.Name} packages={project.ProjectFile.Packages.Count} references={project.ProjectFile.References?.Count ?? 0}");

        var directory = project.ProjectDirectory;
        var vizCodeFiles = DiscoverVizCodeFiles(directory);

        foreach (var filePath in vizCodeFiles)
        {
            var content = File.ReadAllText(filePath);
            var file = new VizCodeFile
            {
                FilePath = filePath,
                Content = content,
                HasUnsavedChanges = false
            };
            project.Files.Add(file);
            Journal.Info("PROJ.FILE.OPEN", "Source file read", Journal.DescribeFile(filePath, content));
        }

        SortFiles(project);

        // Only open the entry point file by default
        var entryPoint = project.EntryPointFile;
        if (entryPoint != null)
        {
            entryPoint.IsOpen = true;
        }

        project.ApplySettings();

        Journal.Info("PROJ.LOAD.DONE", "Project loaded",
            $"files={project.Files.Count} entry={entryPoint?.FileName ?? "<none>"} dir={directory}");
        return project;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public void ApplySettings()
    {
        // Project setting wins; the application-level default is the fallback when the project
        // doesn't specify one (per AppSettingsData: "used when project settings are empty").
        var app = ApplicationSettings.Instance;
        C2VGeometry.ShapeDefaults.GlobalColor = NonEmpty(ProjectFile.Settings.DefaultColor) ?? NonEmpty(app.AppDefaultColor);
        C2VGeometry.ShapeDefaults.GlobalFillColor = NonEmpty(ProjectFile.Settings.DefaultFillColor) ?? NonEmpty(app.AppDefaultFillColor);
        C2VGeometry.ShapeDefaults.GlobalLineWeight = ProjectFile.Settings.DefaultLineWeight ?? app.AppDefaultLineWeight;
        C2VGeometry.ShapeDefaults.GlobalLineTypeScale = ProjectFile.Settings.DefaultLineTypeScale ?? app.AppDefaultLineTypeScale;

        // Dimension style defaults
        C2VGeometry.ShapeDefaults.DimOffset = ProjectFile.Settings.DimOffset;
        C2VGeometry.ShapeDefaults.DimArrowSize = ProjectFile.Settings.DimArrowSize;
        C2VGeometry.ShapeDefaults.DimTextHeight = ProjectFile.Settings.DimTextHeight;
        C2VGeometry.ShapeDefaults.DimDecimalPlaces = ProjectFile.Settings.DimDecimalPlaces;
        C2VGeometry.ShapeDefaults.DimExtendBeyondDimLines = ProjectFile.Settings.DimExtendBeyondDimLines;
        C2VGeometry.ShapeDefaults.DimOffsetFromOrigin = ProjectFile.Settings.DimOffsetFromOrigin;
        C2VGeometry.ShapeDefaults.DimPrefix = ProjectFile.Settings.DimPrefix;
        C2VGeometry.ShapeDefaults.DimSuffix = ProjectFile.Settings.DimSuffix;
        C2VGeometry.ShapeDefaults.DimTextBgOpaque = ProjectFile.Settings.DimTextBgOpaque;
        C2VGeometry.ShapeDefaults.DimExtensionLineColor = ProjectFile.Settings.DimExtensionLineColor;
        C2VGeometry.ShapeDefaults.DimDimensionLineColor = ProjectFile.Settings.DimDimensionLineColor;
        C2VGeometry.ShapeDefaults.DimTextColor = ProjectFile.Settings.DimTextColor;
        C2VGeometry.ShapeDefaults.DimSuppressDimensionLine = ProjectFile.Settings.DimSuppressDimensionLine;
    }

    public static VizCodeProject CreateNew(string directory, string projectName)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var projFileName = $"{projectName}.vizproj";
        var projPath = Path.Combine(directory, projFileName);

        var project = new VizCodeProject
        {
            ProjectFilePath = projPath,
            ProjectFile = new VizProjectFile { Name = projectName }
        };

        project.SaveProjectFile();

        var entryPointPath = Path.Combine(directory, "StartViz.cs");
        var content = Templates.GetStartVizTemplate(projectName);

        var entryPointFile = new VizCodeFile
        {
            FilePath = entryPointPath,
            Content = content,
            HasUnsavedChanges = true,
            IsOpen = true
        };
        
        // Write it immediately so it exists on disk? 
        // Or keep purely in memory until save? 
        // CreateNew usually implies creating on disk.
        File.WriteAllText(entryPointPath, entryPointFile.Content);
        entryPointFile.HasUnsavedChanges = false;
        
        project.Files.Add(entryPointFile);

        Journal.Info("PROJ.CREATE", "New project created",
            $"path={projPath} entry={entryPointPath} chars={content.Length}");
        return project;
    }

    public void SaveFile(VizCodeFile file)
    {
        if (string.IsNullOrEmpty(file.FilePath))
        {
            Journal.Warn("PROJ.SAVE.NOPATH", "Save skipped: file has no path", $"name={file.FileName}");
            return;
        }

        try
        {
            File.WriteAllText(file.FilePath, file.Content);
            file.HasUnsavedChanges = false;
            Journal.Info("PROJ.SAVE.FILE", "File written", Journal.DescribeFile(file.FilePath, file.Content));
        }
        catch (Exception ex)
        {
            // Rethrow: callers decide how to surface it. The journal records it either way, which
            // matters because a failing write (locked file, full disk, denied path) can leave the
            // app in a state the next operation crashes on.
            Journal.Error("PROJ.SAVE.FAIL", "File write failed", ex, $"path={file.FilePath}");
            throw;
        }
    }

    public void SaveAllFiles()
    {
        var written = 0;
        foreach (var file in Files)
        {
            if (file.HasUnsavedChanges)
            {
                SaveFile(file);
                written++;
            }
        }
        Journal.Debug("PROJ.SAVE.ALL", "Save-all complete", $"written={written} total={Files.Count}");
    }

    public void AddFile(VizCodeFile file)
    {
        if (!Files.Contains(file)) Files.Add(file);
    }

    public void RemoveFile(VizCodeFile file)
    {
        Files.Remove(file);
        // Note: Only removes from open tabs, does NOT delete from disk
    }

    public void AddPackage(string id, string version)
    {
        if (!ProjectFile.Packages.Any(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            ProjectFile.Packages.Add(new PackageReference { Id = id, Version = version });
            SaveProjectFile();
        }
    }

    public void RemovePackage(string id)
    {
        var package = ProjectFile.Packages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (package != null)
        {
            ProjectFile.Packages.Remove(package);
            SaveProjectFile();
        }
    }

    public void MoveToDirectory(string newDirectory)
    {
        if (!Directory.Exists(newDirectory))
        {
            Directory.CreateDirectory(newDirectory);
        }

        var fileName = Path.GetFileName(ProjectFilePath);
        ProjectFilePath = Path.Combine(newDirectory, fileName);

        foreach (var file in Files)
        {
            // Assuming flat structure for now or preserving relative?
            // Old impl assumed flat (Path.Combine(newDirectory, file.FileName))
            var name = Path.GetFileName(file.FilePath);
            if (string.IsNullOrEmpty(name)) name = $"{Guid.NewGuid()}.cs"; // Should not happen for existing files
            file.FilePath = Path.Combine(newDirectory, name);
        }

        SaveAllFiles();
        SaveProjectFile();
    }

    public void SaveProjectFile()
    {
        ProjectFile.Save(ProjectFilePath);
    }

    private static IEnumerable<string> DiscoverVizCodeFiles(string directory)
    {
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Gets all source files from the project directory for compilation.
    /// Uses in-memory content for open files, reads from disk for others.
    /// In-memory-only files (newly created via the New File dialog, not yet saved) are
    /// included too — without this, an unsaved sketch file would not be compiled.
    /// </summary>
    public IEnumerable<VizCodeFile> GetAllSourceFiles()
    {
        var allFiles = new List<VizCodeFile>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Include all in-memory files first. This covers unsaved IsNew files whose
        //    FilePath points to a location not yet on disk.
        foreach (var file in Files)
        {
            if (string.IsNullOrEmpty(file.FilePath)) continue;
            if (addedPaths.Add(file.FilePath))
                allFiles.Add(file);
        }

        // 2. Then walk the project directory and pull in any disk files we haven't
        //    already added from memory.
        foreach (var filePath in DiscoverVizCodeFiles(ProjectDirectory))
        {
            if (!addedPaths.Add(filePath)) continue;
            try
            {
                allFiles.Add(new VizCodeFile
                {
                    FilePath = filePath,
                    Content = File.ReadAllText(filePath),
                    HasUnsavedChanges = false
                });
            }
            catch (Exception ex)
            {
                // Skip files that can't be read — but never silently: a file missing from the
                // compilation is a plausible explanation for a downstream failure.
                Journal.Warn("PROJ.SOURCES.READ_FAIL", "Source file skipped", $"path={filePath}", ex);
            }
        }

        return allFiles;
    }

    /// <summary>
    /// What a <see cref="RefreshFilesFromDisk"/> call actually did. The caller needs this to update
    /// the editor and to tell the user the truth — the old method reported "Project refreshed from
    /// disk" unconditionally, including when it had refreshed nothing at all.
    /// </summary>
    public sealed class DiskRefreshResult
    {
        public List<VizCodeFile> Added { get; } = new();
        public List<VizCodeFile> Removed { get; } = new();

        /// <summary>Open files whose content was replaced with what is now on disk.</summary>
        public List<VizCodeFile> Reloaded { get; } = new();

        /// <summary>
        /// Open files that changed on disk <i>and</i> have unsaved edits here. Deliberately left
        /// untouched — the in-memory version is the one the user typed, and silently discarding it
        /// would be the worst possible outcome of a background timer tick.
        /// </summary>
        public List<VizCodeFile> Conflicted { get; } = new();

        /// <summary>Files still on disk that could not be read (locked, permissions, deleted mid-scan).</summary>
        public List<string> Unreadable { get; } = new();

        public bool AnythingChanged =>
            Added.Count > 0 || Removed.Count > 0 || Reloaded.Count > 0 || Conflicted.Count > 0;
    }

    /// <summary>
    /// Refreshes the Files list to match what's on disk: adds new files, removes deleted ones, and
    /// re-reads files that changed externally.
    ///
    /// <para>
    /// The re-read is the part that was missing. Content of already-open files was never refreshed,
    /// so an edit made by another editor, a git checkout or a tool was silently ignored while the
    /// status bar claimed a refresh had happened. A file with unsaved changes here is never
    /// overwritten; it is reported in <see cref="DiskRefreshResult.Conflicted"/> instead.
    /// </para>
    /// </summary>
    public DiskRefreshResult RefreshFilesFromDisk()
    {
        var result = new DiskRefreshResult();
        var discoveredPaths = DiscoverVizCodeFiles(ProjectDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove files that no longer exist on disk (unless they have unsaved changes)
        var filesToRemove = Files
            .Where(f => !discoveredPaths.Contains(f.FilePath) && !f.HasUnsavedChanges && !f.IsNew)
            .ToList();
        foreach (var file in filesToRemove)
        {
            Files.Remove(file);
            result.Removed.Add(file);
            Journal.Info("PROJ.FILE.VANISHED", "File removed from project (gone from disk)", $"path={file.FilePath}");
        }

        // Add new files that aren't already loaded
        foreach (var filePath in discoveredPaths)
        {
            if (!Files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                if (TryReadFile(filePath, out var content))
                {
                    var added = new VizCodeFile
                    {
                        FilePath = filePath,
                        Content = content,
                        HasUnsavedChanges = false
                    };
                    Files.Add(added);
                    result.Added.Add(added);
                    Journal.Info("PROJ.FILE.DISCOVERED", "File appeared on disk and was opened",
                        Journal.DescribeFile(filePath, content));
                }
                else
                {
                    result.Unreadable.Add(filePath);
                }
            }
        }

        // Re-read files that are already open. A file we have never written (IsNew) has no disk
        // counterpart to compare against.
        foreach (var file in Files)
        {
            if (file.IsNew || string.IsNullOrEmpty(file.FilePath)) continue;
            if (!discoveredPaths.Contains(file.FilePath)) continue;
            if (result.Added.Contains(file)) continue;   // just read it

            if (!TryReadFile(file.FilePath, out var diskContent))
            {
                result.Unreadable.Add(file.FilePath);
                continue;
            }

            // Our own saves make the watcher fire, so "no difference" is the common case and must
            // stay a no-op — rewriting identical content would reset the editor and jump the caret.
            if (string.Equals(diskContent, file.Content, StringComparison.Ordinal)) continue;

            if (file.HasUnsavedChanges)
            {
                result.Conflicted.Add(file);
                Journal.Warn("PROJ.FILE.CONFLICT", "File changed on disk but has unsaved edits here",
                    $"path={file.FilePath}");
                continue;
            }

            file.Content = diskContent;
            result.Reloaded.Add(file);
            Journal.Info("PROJ.FILE.RELOADED", "File re-read after an external change",
                Journal.DescribeFile(file.FilePath, diskContent));
        }

        SortFiles(this);
        return result;
    }

    /// <summary>
    /// Reads a file, retrying briefly on a sharing violation. The watcher fires while the writer
    /// still holds the handle, so a single attempt loses the change — and nothing would fire again
    /// to recover it.
    /// </summary>
    private static bool TryReadFile(string path, out string content)
    {
        const int attempts = 3;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                content = File.ReadAllText(path);
                return true;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(60);
            }
            catch (Exception ex)
            {
                Journal.Warn("PROJ.REFRESH.READ_FAIL", "Disk file could not be read", $"path={path}", ex);
                content = string.Empty;
                return false;
            }
        }

        Journal.Warn("PROJ.REFRESH.READ_LOCKED", "Disk file stayed locked across retries", $"path={path}");
        content = string.Empty;
        return false;
    }

    private static void SortFiles(VizCodeProject project)
    {
         project.Files.Sort((a, b) =>
        {
            if (a.IsEntryPoint) return -1;
            if (b.IsEntryPoint) return 1;
            return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
        });
    }
}
