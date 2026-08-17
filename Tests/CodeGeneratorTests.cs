using System.Linq;
using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Tests;

/// <summary>
/// What the drawing tools write into the user's file.
///
/// <para>
/// This is the app's own largest producer of DoodleSharp source, and whatever it emits is what a new
/// user reads first — so it has to spell things the way the documentation does. It emitted
/// <c>.Draw()</c>, the historical name, long after <c>Place()</c> became the recommended one.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class CodeGeneratorTests
{
    private static string Generate(Shape shape) => CodeGenerator.GenerateCode(shape);

    [Fact]
    public void GeneratedCodeUsesPlaceNotDraw()
    {
        var code = Generate(new VCircle(10, 20, 5) { Name = "circle1" });

        Assert.Contains(".Place();", code);
        Assert.DoesNotContain(".Draw();", code);
    }

    [Theory]
    [InlineData(typeof(VPoint))]
    [InlineData(typeof(VLine))]
    [InlineData(typeof(VCircle))]
    [InlineData(typeof(VRectangle))]
    [InlineData(typeof(VEllipse))]
    [InlineData(typeof(VArc))]
    [InlineData(typeof(VPolygon))]
    [InlineData(typeof(VPolyline))]
    [InlineData(typeof(VBezier))]
    [InlineData(typeof(VSpline))]
    [InlineData(typeof(VArrow))]
    [InlineData(typeof(VText))]
    public void EveryGeneratedShapeTypePlacesItself(System.Type shapeType)
    {
        // Guards against one branch being missed — the switch has a separate literal per type.
        var shape = MakeShape(shapeType);
        var code = Generate(shape);

        Assert.Contains(".Place();", code);
        Assert.DoesNotContain(".Draw();", code);
    }

    [Fact]
    public void GeneratedCodeDeclaresAVariableAndThenPlacesIt()
    {
        // The declaration-plus-call shape is what CodeSyncManager's delete matcher expects: it
        // finds the declaration, then sweeps the following `varName.*;` lines. The variable name
        // comes from a running counter, so match the shape rather than a fixed number.
        var code = Generate(new VLine(0, 0, 100, 50));

        var declaration = System.Text.RegularExpressions.Regex.Match(code, @"var (line\d+) = new VLine\(");
        Assert.True(declaration.Success, $"expected a var declaration, got: {code}");
        Assert.Contains($"{declaration.Groups[1].Value}.Place();", code);
    }

    [Fact]
    public void GeneratedCodeCanStillBeDeletedByTheCanvasDeletePath()
    {
        // End-to-end: generate, then delete. If the generator's spelling ever drifted away from
        // what the matcher sweeps, canvas delete would leave the trailing call behind.
        var circle = new VCircle(10, 20, 5) { Name = "circle1" };
        var file = $@"
using C2VGeometry;

namespace TestBed
{{
    public class Viz
    {{
        public static void Main()
        {{
            {Generate(circle)}
            VXYZ keep = new VXYZ(1, 1);
        }}
    }}
}}";

        var (result, found) = CodeSyncManager.RemoveShapeCode(file, circle);

        Assert.True(found);
        Assert.DoesNotContain("new VCircle", result);
        Assert.DoesNotContain(".Place();", result);   // the trailing call goes too
        Assert.Contains("VXYZ keep", result);
    }

    private static Shape MakeShape(System.Type type)
    {
        var pts = new[] { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10) };

        return type.Name switch
        {
            nameof(VPoint) => new VPoint(1, 2),
            nameof(VLine) => new VLine(0, 0, 10, 10),
            nameof(VCircle) => new VCircle(0, 0, 5),
            nameof(VRectangle) => new VRectangle(0, 0, 10, 5),
            nameof(VEllipse) => new VEllipse(new VXYZ(0, 0), 10, 5),
            nameof(VArc) => new VArc(new VXYZ(0, 0), 10, 0, 90),
            nameof(VPolygon) => new VPolygon(pts),
            nameof(VPolyline) => new VPolyline(pts),
            nameof(VBezier) => new VBezier(0, 0, 1, 1, 2, 2, 3, 3),
            nameof(VSpline) => new VSpline(pts),
            nameof(VArrow) => new VArrow(0, 0, 10, 10),
            nameof(VText) => new VText(new VXYZ(0, 0), "hi", 12),
            _ => throw new System.ArgumentException($"no factory for {type.Name}")
        };
    }
}
