using System.IO;
using System.Text.Json;

namespace DoodleSharp.Project;

public class PackageReference
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public override string ToString() => $"{Id} ({Version})";
}

public class AssemblyReference
{
    /// <summary>
    /// Path to the DLL (for local references) or assembly name (for framework references).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True for .NET framework assemblies that should be resolved from trusted assemblies.
    /// False for local DLL files.
    /// </summary>
    public bool IsFramework { get; set; }

    public override string ToString() => IsFramework ? Path : System.IO.Path.GetFileName(Path);
}


public class VizProjectFile
{
    public string Name { get; set; } = "New Project";
    public List<PackageReference> Packages { get; set; } = new();
    public List<AssemblyReference> References { get; set; } = new();
    public ProjectSettings Settings { get; set; } = new();

    public static VizProjectFile Load(string path)
    {
        if (!File.Exists(path)) return new VizProjectFile();
        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<VizProjectFile>(json) ?? new VizProjectFile();
        return project;
    }

    public void Save(string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(path, json);
    }
}

public class ProjectSettings
{
    public string? DefaultColor { get; set; }
    public string? DefaultFillColor { get; set; }
    public string? DefaultCanvasBackgroundColor { get; set; }
    public string? DefaultExportBackground { get; set; } // "Transparent", "Canvas", "White", "Black", "Light"
    public double? DefaultLineWeight { get; set; }
    public double? DefaultLineTypeScale { get; set; }

    /// <summary>
    /// Re-run the project's code on a timer instead of waiting for F5. Off unless the project says
    /// otherwise, so a project opened without the key behaves exactly as before.
    /// <para>
    /// Per-project rather than per-application: whether a sketch is worth re-running twice a second
    /// is a property of that sketch, and leaving it armed globally would recompile whatever project
    /// happened to be open next.
    /// </para>
    /// </summary>
    public bool? AutoRun { get; set; }

    // Dimension style defaults
    public double? DimOffset { get; set; }
    public double? DimArrowSize { get; set; }
    public double? DimTextHeight { get; set; }
    public int? DimDecimalPlaces { get; set; }
    public double? DimExtendBeyondDimLines { get; set; }
    public double? DimOffsetFromOrigin { get; set; }
    public string? DimPrefix { get; set; }
    public string? DimSuffix { get; set; }
    public bool? DimTextBgOpaque { get; set; }
    public string? DimExtensionLineColor { get; set; }
    public string? DimDimensionLineColor { get; set; }
    public string? DimTextColor { get; set; }
    public bool? DimSuppressDimensionLine { get; set; }
}
