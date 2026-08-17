using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using C2VGeometry;
using DoodleSharp.Animation;

namespace DoodleSharp.Tests;

/// <summary>
/// Animated rotation has to work on every shape, not just the few whose renderer branch happened to
/// implement it.
///
/// <para>
/// <c>RotateAnimation</c> writes <c>Shape.RotationAngle</c> and <c>Shape.RotationPivot</c> on any
/// <c>Shape</c>, but only <c>DrawLine</c>, <c>DrawCircle</c> and <c>DrawArrow</c> ever read them
/// back — so rotating an ellipse, arc, polygon, polyline, bezier, spline, text, group, hatch or
/// region silently did nothing at all. <c>VRectangle</c> was fixed on its own (CLAUDE.md note 55),
/// which is what made the general case easy to miss. Rotation is now applied once, for every shape
/// type, in <c>RenderCanvas.DispatchShapeDraw</c>.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ShapeRotationTests
{
    private static Shape Make(string kind)
    {
        var pts = new[] { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10) };
        return kind switch
        {
            "VLine" => new VLine(0, 0, 10, 10),
            "VCircle" => new VCircle(0, 0, 5),
            "VEllipse" => new VEllipse(new VXYZ(0, 0), 10, 5),
            "VArc" => new VArc(new VXYZ(0, 0), 10, 0, 90),
            "VPolygon" => new VPolygon(pts),
            "VPolyline" => new VPolyline(pts),
            "VBezier" => new VBezier(0, 0, 1, 1, 2, 2, 3, 3),
            "VSpline" => new VSpline(pts),
            "VText" => new VText(new VXYZ(0, 0), "hi", 12),
            "VArrow" => new VArrow(0, 0, 10, 10),
            "VRectangle" => new VRectangle(0, 0, 10, 5),
            _ => throw new ArgumentException(kind)
        };
    }

    [Theory]
    [InlineData("VLine")]
    [InlineData("VCircle")]
    [InlineData("VEllipse")]
    [InlineData("VArc")]
    [InlineData("VPolygon")]
    [InlineData("VPolyline")]
    [InlineData("VBezier")]
    [InlineData("VSpline")]
    [InlineData("VText")]
    [InlineData("VArrow")]
    [InlineData("VRectangle")]
    public void RotateAnimationRecordsTheRotationOnEveryShapeType(string kind)
    {
        var shape = Make(kind);
        var pivot = new VXYZ(100, 100);

        var anim = new RotateAnimation(shape, pivot, 90, 1.0);
        anim.Apply(1.0);

        // The renderer reads exactly these two. If either is missing the shape cannot rotate,
        // whatever the draw path does.
        Assert.Equal(90, shape.RotationAngle, 9);
        Assert.NotNull(shape.RotationPivot);
        Assert.Equal(pivot.X, shape.RotationPivot!.X, 9);
        Assert.Equal(pivot.Y, shape.RotationPivot!.Y, 9);
    }

    [Fact]
    public void RotateAnimationAccumulatesFromTheShapesExistingAngle()
    {
        var shape = Make("VEllipse");
        shape.RotationAngle = 30;

        var anim = new RotateAnimation(shape, new VXYZ(0, 0), 60, 1.0);
        anim.Apply(1.0);

        Assert.Equal(90, shape.RotationAngle, 9);
    }

    [Fact]
    public void RectangleStillBakesRotationIntoItsGeometry()
    {
        // VRectangle is the one shape excluded from the generic transform, because its
        // RotationAngle setter rebuilds the corners — applying a transform as well would rotate it
        // twice. If this ever stops being true, the exclusion in DispatchShapeDraw must go.
        var rect = (VRectangle)Make("VRectangle");
        var before = rect.Vertices.Select(v => (v.X, v.Y)).ToList();

        rect.RotationAngle = 45;

        var after = rect.Vertices.Select(v => (v.X, v.Y)).ToList();
        Assert.NotEqual(before, after);
    }

    // ── Renderer convention guard ───────────────────────────────────────────

    /// <summary>
    /// Rotation must be applied in exactly one place. Per-shape opt-in is what caused the bug:
    /// fifteen Draw* methods simply never implemented it, and nothing failed when they didn't.
    /// </summary>
    [Fact]
    public void RenderCanvasAppliesRotationInExactlyOnePlace()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Canvas", "RenderCanvas.cs"));

        // Any RotateTransform built from a shape's RotationAngle. VText.Angle is a different,
        // intrinsic property and is deliberately not matched.
        var sites = Regex.Matches(source, @"new\s+RotateTransform\s*\(\s*-?\w+\.RotationAngle")
            .Select(m => source.Take(m.Index).Count(c => c == '\n') + 1)
            .ToList();

        Assert.True(sites.Count == 1,
            $"Rotation should be applied once, in DispatchShapeDraw. Found {sites.Count} site(s) at " +
            $"line(s) {string.Join(", ", sites)}. A per-shape rotation transform either double-rotates " +
            "(if DispatchShapeDraw also handles that type) or hides the fact that other shapes have none.");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DoodleSharp.sln")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not locate the repository root (DoodleSharp.sln)");
        return dir!.FullName;
    }
}
