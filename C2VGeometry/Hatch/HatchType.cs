using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace C2VGeometry;

/// <summary>
/// Defines a hatch pattern composed of one or more line families.
/// Follows the AutoCAD .pat format. Can be created from built-in patterns,
/// parsed from a string definition, or constructed programmatically.
/// </summary>
public class HatchType
{
    /// <summary>Pattern name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Pattern description.</summary>
    public string Description { get; set; } = "";

    /// <summary>The line families that make up this pattern.</summary>
    public List<HatchPatternLine> Lines { get; set; } = new();

    public HatchType() { }

    public HatchType(string name, string description, List<HatchPatternLine> lines)
    {
        Name = name;
        Description = description;
        Lines = lines;
    }

    /// <summary>
    /// Parses a hatch pattern from the AutoCAD .pat format string.
    /// The first line should be "*NAME, Description".
    /// Subsequent lines define hatch line families.
    /// </summary>
    /// <example>
    /// var hatch = HatchType.Parse(@"
    ///   *ANSI31, ANSI Iron, Brick, Stone masonry
    ///   45, 0,0, 0,.125
    /// ");
    /// </example>
    public static HatchType Parse(string patDefinition)
    {
        var hatch = new HatchType();
        var lines = patDefinition.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Skip comments
            if (line.StartsWith(";;") || line.StartsWith(";") || string.IsNullOrEmpty(line))
                continue;

            // Pattern header
            if (line.StartsWith("*"))
            {
                var commaIdx = line.IndexOf(',');
                if (commaIdx > 0)
                {
                    hatch.Name = line.Substring(1, commaIdx - 1).Trim();
                    hatch.Description = line.Substring(commaIdx + 1).Trim();
                }
                else
                {
                    hatch.Name = line.Substring(1).Trim();
                }
                continue;
            }

            // Parse line definition: angle, x-origin, y-origin, delta-x, delta-y [, dash1, dash2, ...]
            var parts = line.Split(',');
            if (parts.Length < 5) continue;

            var patternLine = new HatchPatternLine();
            patternLine.Angle = ParseDouble(parts[0]);
            patternLine.OriginX = ParseDouble(parts[1]);
            patternLine.OriginY = ParseDouble(parts[2]);
            patternLine.DeltaX = ParseDouble(parts[3]);
            patternLine.DeltaY = ParseDouble(parts[4]);

            if (parts.Length > 5)
            {
                var dashes = new List<double>();
                for (int i = 5; i < parts.Length; i++)
                {
                    var val = parts[i].Trim();
                    if (!string.IsNullOrEmpty(val))
                        dashes.Add(ParseDouble(val));
                }
                patternLine.Dashes = dashes.ToArray();
            }

            hatch.Lines.Add(patternLine);
        }

        return hatch;
    }

    private static double ParseDouble(string s)
    {
        return double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets a built-in hatch pattern by name (case-insensitive).
    /// </summary>
    public static HatchType GetBuiltIn(string name)
    {
        return BuiltInHatches.Get(name);
    }

    /// <summary>
    /// Gets a built-in hatch pattern by enum value.
    /// </summary>
    public static HatchType GetBuiltIn(BuiltInHatch pattern)
    {
        return BuiltInHatches.Get(pattern);
    }

    public override string ToString() => $"HatchType({Name}: {Lines.Count} lines)";

    /// <summary>
    /// Deep copy of this pattern, including its line families.
    /// </summary>
    /// <remarks>
    /// <see cref="BuiltInHatches"/> caches one instance per pattern, so without a copy every caller
    /// asking for the same built-in name received the *same* mutable object: adjusting the angle or
    /// spacing of one hatch silently changed every later lookup, for the rest of the session.
    /// </remarks>
    public HatchType Clone() =>
        new HatchType(Name, Description, Lines.Select(l => l.Clone()).ToList());
}
