using System;
using System.IO;
using System.Linq;
using System.Reflection;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the arrowhead geometry against the divergence that shipped: five separate implementations
/// of "where do the wings go", which disagreed with each other and mostly ignored
/// <see cref="VArrow.HeadAngle"/> altogether.
///
/// <para>
/// Before the fix: <c>RenderCanvas.DrawArrow</c> and <c>VArrow.GetArrowheadPoints</c> hard-coded a
/// <c>HeadLength / 6</c> half-width (a ≈9.46° half-angle) and never read <c>HeadAngle</c>;
/// <c>ShapeTessellator</c> honoured it and drew an open V; the PDF exporter honoured it but also
/// clamped the head to 20% of the shaft; the DXF exporter hard-coded <i>both</i> 30° and
/// <c>min(length * 0.2, 10)</c>, ignoring <c>HeadLength</c> too. So setting <c>HeadAngle</c> did
/// nothing on screen, changed the raster/GPU/PDF output, and an arrow's head was a different shape
/// and size depending on which backend or exporter drew it. Same failure as the rotation bug in
/// note 68: per-renderer geometry lets a property be honoured in one path and dropped in another.
/// </para>
/// </summary>
public class ArrowheadConsistencyTests
{
    /// <summary>
    /// The defining property: the angle between the shaft and each wing IS <c>HeadAngle</c>. This is
    /// what the old fixed <c>HeadLength / 6</c> ratio could not express — it pinned the angle at
    /// ≈9.46° whatever <c>HeadAngle</c> said.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    public void WingsSitAtHeadAngleOffTheShaft(double headAngle)
    {
        var arrow = new VArrow(new VXYZ(0, 0), new VXYZ(100, 0))
        {
            HeadLength = 10,
            HeadAngle = headAngle,
        };

        var (wing1, wing2) = arrow.GetEndArrowhead();

        // Shaft points +X, so the angle of (wing - tip) away from -X is the head half-angle.
        Assert.Equal(headAngle, AngleFromShaft(arrow.End, arrow.Start, wing1), 6);
        Assert.Equal(headAngle, AngleFromShaft(arrow.End, arrow.Start, wing2), 6);

        // Each wing is HeadLength long, measured from the tip.
        Assert.Equal(10, arrow.End.DistanceTo(wing1), 6);
        Assert.Equal(10, arrow.End.DistanceTo(wing2), 6);

        // ...and they sit on opposite sides of the shaft.
        Assert.True(wing1.Y > 0 && wing2.Y < 0, "wings should straddle the shaft");
    }

    /// <summary>
    /// Pins the specific number the old implementation was stuck at, so a regression to a fixed
    /// perpendicular half-width is caught rather than merely looking plausible.
    /// </summary>
    [Fact]
    public void HeadAngleIsNotTheOldFixedRatio()
    {
        var arrow = new VArrow(new VXYZ(0, 0), new VXYZ(100, 0)) { HeadLength = 12, HeadAngle = 30 };
        var (wing1, _) = arrow.GetEndArrowhead();

        var legacyAngle = Math.Atan2(12.0 / 6.0, 12.0) * 180.0 / Math.PI; // ≈9.46°
        var actual = AngleFromShaft(arrow.End, arrow.Start, wing1);

        Assert.Equal(30, actual, 6);
        Assert.True(Math.Abs(actual - legacyAngle) > 1,
            $"arrowhead is back to the fixed HeadLength/6 ratio ({legacyAngle:F2}°)");
    }

    [Fact]
    public void DoubleEndedHeadPointsTheOtherWay()
    {
        var arrow = new VArrow(new VXYZ(0, 0), new VXYZ(100, 0)) { HeadLength = 10, HeadAngle = 30 };

        var (s1, s2) = arrow.GetStartArrowhead();

        // The start head opens back towards End, so its wings are on the +X side of Start.
        Assert.True(s1.X > arrow.Start.X && s2.X > arrow.Start.X);
        Assert.Equal(30, AngleFromShaft(arrow.Start, arrow.End, s1), 6);
    }

    [Fact]
    public void DegenerateArrowReturnsTheTipRatherThanNaN()
    {
        var arrow = new VArrow(new VXYZ(5, 5), new VXYZ(5, 5));

        var (wing1, wing2) = arrow.GetEndArrowhead();

        Assert.True(wing1.IsAlmostEqualTo(arrow.End));
        Assert.True(wing2.IsAlmostEqualTo(arrow.End));
    }

    /// <summary>
    /// Every renderer and exporter must route through <see cref="VArrow.ArrowheadWings"/>. A source
    /// scan is the only thing that catches someone reintroducing local wing maths — the geometry
    /// would still look reasonable in isolation, which is exactly how five copies accumulated.
    /// </summary>
    [Theory]
    [InlineData("Canvas/RenderCanvas.cs")]
    [InlineData("Canvas/SvgExporter.cs")]
    [InlineData("Export/PdfExporter.cs")]
    [InlineData("Export/DxfExporter.cs")]
    [InlineData("C2VGeometry/Rendering/ShapeTessellator.cs")]
    public void NoRendererComputesItsOwnArrowheadWings(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found at {path}");

        var source = File.ReadAllText(path);

        Assert.True(source.Contains("ArrowheadWings") || source.Contains("GetArrowheadPoints")
                    || source.Contains("GetEndArrowhead"),
            $"{relativePath} draws arrowheads but no longer uses VArrow's shared geometry.");

        // The signature of every old copy: a perpendicular half-width of size / 6.
        Assert.DoesNotContain("/ 6.0;", source);
    }

    /// <summary>
    /// Dimension arrowheads had the same problem independently — the tessellator drew them at a
    /// hard-coded 20° while the canvas, SVG and PDF used a fixed <c>ArrowSize / 6</c>.
    /// </summary>
    [Fact]
    public void DimensionArrowAngleIsSharedNotHardCoded()
    {
        Assert.Equal(20, VDimension.DimensionArrowAngleDegrees);

        foreach (var file in new[]
                 {
                     "Canvas/RenderCanvas.cs", "Canvas/SvgExporter.cs",
                     "Export/PdfExporter.cs", "C2VGeometry/Rendering/ShapeTessellator.cs",
                 })
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), file));
            if (!source.Contains("DimensionArrowhead") && !source.Contains("EmitArrowHead")) continue;

            Assert.Contains("DimensionArrowAngleDegrees", source);
        }
    }

    /// <summary>
    /// <c>ExtensionLength</c> controls nothing — an extension line's length is fully determined by
    /// OffsetFromOrigin, Offset and ExtendBeyondDimLines. It is deprecated rather than deleted so
    /// existing code still compiles, and the attribute is what tells the reader it is inert.
    /// </summary>
    [Fact]
    public void DeadDimensionPropertyIsMarkedObsolete()
    {
        var property = typeof(VDimension).GetProperty(nameof(VDimension.ExtensionLength));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ObsoleteAttribute>());
    }

    /// <summary>
    /// The extension lines really are governed by the other three, which is why there is no room for
    /// ExtensionLength: this pins the geometry so a future "fix" that wires it up has to think about
    /// what it would even mean.
    /// </summary>
    [Fact]
    public void ExtensionLineSpansOffsetFromOriginToBeyondTheDimensionLine()
    {
        var dim = new VDimension(new VXYZ(0, 0), new VXYZ(100, 0))
        {
            Offset = 20,
            OffsetFromOrigin = 2,
            ExtendBeyondDimLines = 3,
        };

        var (_, _, _, ext1Start, ext1End, _, _) = dim.GetDimensionGeometry();

        // Perpendicular to a horizontal measurement is -Y for this winding; compare magnitudes.
        Assert.Equal(2, Math.Abs(ext1Start.Y), 6);
        Assert.Equal(23, Math.Abs(ext1End.Y), 6);
    }

    private static double AngleFromShaft(VXYZ tip, VXYZ from, VXYZ wing)
    {
        var shaft = new VXYZ(from.X - tip.X, from.Y - tip.Y).Normalize();
        var toWing = new VXYZ(wing.X - tip.X, wing.Y - tip.Y).Normalize();
        var dot = Math.Clamp(shaft.DotProduct(toWing), -1.0, 1.0);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "DoodleSharp.sln")))
            dir = Path.GetDirectoryName(dir);

        Assert.NotNull(dir);
        return dir!;
    }
}
