using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Bench;

/// <summary>
/// Deterministic scenes to measure against. Deterministic matters more than realism: a baseline is
/// only worth committing if the next run generates exactly the same geometry, so every scene is
/// built from a fixed seed and integer arithmetic, never from wall-clock or unseeded randomness.
///
/// <para>
/// The four scenes are chosen to stress different failure modes rather than to be pretty.
/// <c>CityGrid</c> is the sheer-count case; <c>HatchedParcels</c> is the per-frame regeneration case
/// (a single hatch can expand to tens of thousands of segments); <c>Scatter</c> is the authoring
/// pathology where one data point becomes one shape; <c>MixedCad</c> is the everything-at-once case
/// that catches per-type regressions the others would miss.
/// </para>
/// </summary>
public static class SceneGenerator
{
    public sealed record Scene(string Name, string Description, int TargetShapeCount);

    public static readonly Scene[] All =
    {
        new("city-grid",       "Orthogonal line grid with buildings — the sheer-count case", 1_000_000),
        new("hatched-parcels", "Dense hatched regions — the per-frame regeneration case",       200_000),
        new("scatter",         "Chart.Scatter — one VCircle per data point",                    100_000),
        new("mixed-cad",       "Lines, arcs, text, dimensions, polygons, groups",               200_000),
    };

    /// <summary>
    /// Builds a scene directly into the shape registry. Shapes auto-register on construction, so
    /// this deliberately does not collect them itself.
    /// </summary>
    public static void Build(string name, int shapeBudget)
    {
        CanvasRenderer.Instance.Clear();
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        Shape.AutoRegister = true;

        switch (name)
        {
            case "city-grid": BuildCityGrid(shapeBudget); break;
            case "hatched-parcels": BuildHatchedParcels(shapeBudget); break;
            case "scatter": BuildScatter(shapeBudget); break;
            case "mixed-cad": BuildMixedCad(shapeBudget); break;
            default: throw new ArgumentException($"Unknown scene '{name}'.", nameof(name));
        }
    }

    /// <summary>
    /// A regular street grid with a building outline per block. Shapes are spread over a large world
    /// so that a typical view holds a small fraction of them — which is the whole point: it is the
    /// scene that tells you whether culling works.
    /// </summary>
    private static void BuildCityGrid(int budget)
    {
        // Four shapes per block (two streets, one building, one lot line).
        var blocks = Math.Max(1, budget / 4);
        var side = (int)Math.Sqrt(blocks);
        const double Block = 100.0;

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                double ox = x * Block, oy = y * Block;

                new VLine(new VXYZ(ox, oy), new VXYZ(ox + Block, oy)) { Color = "DimGray", LineWeight = 1 };
                new VLine(new VXYZ(ox, oy), new VXYZ(ox, oy + Block)) { Color = "DimGray", LineWeight = 1 };
                new VRectangle(new VXYZ(ox + 20, oy + 20), 60, 55) { Color = "SteelBlue" };

                // A varied element so the scene isn't a single hot type — every 4th block gets a
                // circle instead of a lot line, which also exercises curve flattening.
                if (((x + y) & 3) == 0)
                    new VCircle(new VXYZ(ox + 50, oy + 50), 18) { Color = "Goldenrod" };
                else
                    new VLine(new VXYZ(ox + 10, oy + 90), new VXYZ(ox + 90, oy + 90)) { Color = "#444444" };
            }
        }
    }

    /// <summary>
    /// Parcels with hatch fills. Hatch is the dangerous content type: <c>VHatch.GenerateLines()</c>
    /// has no cache and its only guard is a per-pattern-family cap of 10,000 segments, so a few
    /// hundred hatches can submit more geometry than a million lines.
    /// </summary>
    private static void BuildHatchedParcels(int budget)
    {
        var parcels = Math.Max(1, budget / 8);
        var side = (int)Math.Sqrt(parcels);
        const double Lot = 40.0;
        var patterns = new[] { "ANSI31", "ANSI32", "ANSI37", "NET", "STARS" };

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                double ox = x * Lot, oy = y * Lot;

                var boundary = new List<VXYZ>
                {
                    new(ox + 2, oy + 2),
                    new(ox + Lot - 2, oy + 2),
                    new(ox + Lot - 2, oy + Lot - 2),
                    new(ox + 2, oy + Lot - 2),
                };

                new VPolygon(boundary.ToArray()) { Color = "White", LineWeight = 1 };

                // Only some parcels are hatched — an all-hatch scene is not realistic, and the mix
                // is what reveals whether hatch cost dominates.
                if (((x * 7 + y * 13) % 5) == 0)
                {
                    try
                    {
                        new VHatch(boundary, patterns[(x + y) % patterns.Length])
                        {
                            Color = "DarkCyan",
                            PatternScale = 1.5,
                        };
                    }
                    catch
                    {
                        // A pattern name that isn't built in must not abort scene generation.
                    }
                }
            }
        }
    }

    /// <summary>
    /// A large scatter plot. This measures the authoring pathology directly: <c>Chart.Scatter</c>
    /// emits one <c>VCircle</c> per data point, so the shape count is the data count.
    /// </summary>
    private static void BuildScatter(int budget)
    {
        var points = Math.Max(1, budget);

        // A fixed-seed LCG rather than Random, so the scene is byte-identical between runs and
        // between machines — a baseline generated on one box has to be comparable on another.
        ulong seed = 0x9E3779B97F4A7C15UL;
        double Next()
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            return ((seed >> 11) & ((1UL << 53) - 1)) / (double)(1UL << 53);
        }

        for (int i = 0; i < points; i++)
        {
            var x = Next() * 2000 - 1000;
            var y = Next() * 2000 - 1000;
            new VCircle(new VXYZ(x, y), 1.5) { Color = "Cyan", FillColor = "#2288CC" };
        }
    }

    /// <summary>
    /// A mixture in roughly the proportions a real drawing has: mostly lines, some curves, a little
    /// text and annotation. Text is the expensive minority — the legacy renderer builds a
    /// <c>FormattedText</c> per label per frame.
    /// </summary>
    private static void BuildMixedCad(int budget)
    {
        var groups = Math.Max(1, budget / 10);
        var side = (int)Math.Sqrt(groups);
        const double Cell = 120.0;

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                double ox = x * Cell, oy = y * Cell;

                new VLine(new VXYZ(ox, oy), new VXYZ(ox + 100, oy)) { Color = "White" };
                new VLine(new VXYZ(ox, oy), new VXYZ(ox, oy + 100)) { Color = "White" };
                new VLine(new VXYZ(ox + 100, oy), new VXYZ(ox + 100, oy + 100)) { Color = "White" };
                new VLine(new VXYZ(ox, oy + 100), new VXYZ(ox + 100, oy + 100)) { Color = "White" };

                new VArc(new VXYZ(ox + 50, oy + 50), 30, 0, 180) { Color = "Orange" };
                new VCircle(new VXYZ(ox + 50, oy + 50), 12) { Color = "LimeGreen" };

                new VPolyline(new VXYZ(ox + 10, oy + 10), new VXYZ(ox + 40, oy + 30),
                              new VXYZ(ox + 70, oy + 15), new VXYZ(ox + 95, oy + 40))
                { Color = "Violet" };

                new VEllipse(new VXYZ(ox + 60, oy + 75), 25, 12) { Color = "Salmon" };

                // One label and one dimension per cell — the minority that costs the most.
                new VText(new VXYZ(ox + 5, oy + 105), $"C{x},{y}") { Height = 8, Color = "Silver" };
                new VDimension(new VXYZ(ox, oy - 8), new VXYZ(ox + 100, oy - 8)) { Color = "Yellow" };
            }
        }
    }
}
