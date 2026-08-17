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

    /// <summary>
    /// Which renderer draws the scene.
    ///
    /// <para>
    /// <c>Legacy</c> is WPF's <c>DrawingVisual</c> throughout — complete, and the behaviour every
    /// existing drawing was authored against. <c>Managed</c> rasterises hairline geometry into a
    /// bitmap and leaves text, dimensions and chrome to the vector layer above it, which is far
    /// faster on large drawings but is a two-layer split: annotation always composites over
    /// geometry, regardless of the order shapes were created in.
    /// </para>
    ///
    /// <para>
    /// <c>Auto</c> is the default and picks per frame. Neither backend is right as a fixed choice:
    /// the rasterizer carries a fixed cost of roughly 2 ms a frame — clearing and copying an 8 MB
    /// buffer at 1080p — regardless of what is on screen, in exchange for a per-primitive cost far
    /// below WPF's. Measured on the benchmark, it turns the worst frame of a dense drawing from
    /// 107 ms into 45 ms, and makes a near-empty view four times slower. Choosing per frame gets
    /// both: exact vector semantics while a drawing is light, and a usable frame rate when it is not.
    /// </para>
    ///
    /// <para>
    /// <c>GPU</c> forces the Direct3D 11 path, which uploads geometry once in world coordinates so
    /// that panning and zooming cost almost nothing — the only backend with a flat frame time across
    /// navigation, and the only one that holds up at 4K. It falls back to the software rasterizer
    /// when no device can be created, recording the reason in the journal, so selecting it is safe
    /// on a machine without a usable GPU.
    /// </para>
    ///
    /// <para>
    /// Recognised values are <c>Auto</c>, <c>Legacy</c>, <c>Managed</c> and <c>GPU</c>, matched
    /// case-insensitively; anything else behaves as <c>Auto</c>. Settable from
    /// <c>Settings &gt; Application Settings &gt; Rendering</c>.
    /// </para>
    /// </summary>
    public string RenderBackend { get; set; } = "Auto";

    // Window Visibility Settings
    public bool ShowRibbon { get; set; } = true;
    public bool ShowProjectBrowser { get; set; } = false;
    public bool ShowOutliner { get; set; } = false;
    public bool ShowTimeline { get; set; } = false;
    // ShowToolbar removed: the drawing toolbar it governed was replaced by the Draw menu, so nothing
    // has read or written it since. An older appsettings.json still carrying the key deserializes
    // fine — unknown members are ignored — and the key drops out on the next save.
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
