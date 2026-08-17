using System.IO;
using System.Text.Json;

namespace DoodleSharp;

public class AppSettingsData
{
    public bool IncludeGridInExport { get; set; } = true;
    public string DefaultExportBackground { get; set; } = "Transparent";
    public bool ShowGrid { get; set; } = true;
    public bool ZoomToFitOnRun { get; set; } = false;
    public bool AutoUpdateCanvas { get; set; } = true;
    public int AutoUpdateDelayMs { get; set; } = 500;
    public bool AutoDraw { get; set; } = true;
    public bool DrawPointAsPatch { get; set; } = false;

    // Window Visibility Settings
    public bool ShowRibbon { get; set; } = true;
    public bool ShowProjectBrowser { get; set; } = false;
    public bool ShowOutliner { get; set; } = false;
    public bool ShowTimeline { get; set; } = false;
    public bool ShowToolbar { get; set; } = false;
    public bool ShowConsole { get; set; } = true;
    public bool ShowCanvas { get; set; } = true;
    public bool ShowProperties { get; set; } = false;
    public bool PropertiesDocked { get; set; } = false;
    public bool ShowGlobalParameters { get; set; } = false;
    public bool ShowMinimap { get; set; } = false;

    // Snap Settings
    public bool SnapEndpointEnabled { get; set; } = true;
    public bool SnapMidpointEnabled { get; set; } = true;
    public bool SnapCenterEnabled { get; set; } = true;
    public bool SnapIntersectionEnabled { get; set; } = true;
    public bool SnapNearestEnabled { get; set; } = true;
    public bool SnapPerpendicularEnabled { get; set; } = true;
    public bool SnapExtensionEnabled { get; set; } = true;
    public bool SnapTangentEnabled { get; set; } = true;
    public bool SnapToGridEnabled { get; set; } = false;

    // Highlight Settings (for Outliner hover)
    public string HighlightColor { get; set; } = "Yellow";
    public int HighlightOpacity { get; set; } = 40; // 0-100 percentage

    // Auto Save Settings
    /// <summary>When true, unsaved project files are written to disk every <see cref="AutoSaveIntervalSeconds"/> seconds.</summary>
    public bool AutoSaveEnabled { get; set; } = false;
    /// <summary>Auto-save interval in seconds. Clamped to [5, 3600] when applied.</summary>
    public int AutoSaveIntervalSeconds { get; set; } = 60;

    // Line Style Rendering Settings
    /// <summary>
    /// True (default) = line weight is measured in world units, so strokes get thicker as you zoom in.
    /// False = line weight is measured in screen pixels and stays constant at any zoom.
    /// </summary>
    public bool LineWeightRelativeToZoom { get; set; } = true;
    /// <summary>
    /// True (default) = dash/gap lengths are measured in world units, so the pattern stretches as you zoom in.
    /// False = the pattern keeps a constant on-screen size at any zoom.
    /// </summary>
    public bool LineTypeScaleRelativeToZoom { get; set; } = true;

    // Default Shape Settings (Application-level defaults, used when project settings are empty)
    public string? AppDefaultColor { get; set; }
    public string? AppDefaultFillColor { get; set; }
    public string? AppDefaultCanvasBackground { get; set; }
    public double? AppDefaultLineWeight { get; set; }
    public double? AppDefaultLineTypeScale { get; set; }
}

public static class ApplicationSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "DoodleSharp",
        "appsettings.json");

    public static AppSettingsData Instance { get; private set; } = new();

    static ApplicationSettings()
    {
        Load();
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Instance = JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
            }
        }
        catch { /* Ignore errors, use defaults */ }
    }

    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(dir) && dir != null) Directory.CreateDirectory(dir);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(Instance, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* Ignore errors */ }
    }
}
