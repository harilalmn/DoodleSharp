using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C2VGeometry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Rules about <c>RenderCanvas</c> that only a reader could previously enforce, and that a reader
/// repeatedly did not.
///
/// <para>
/// A renderer fails quietly. A shape drawn in the wrong place, or not drawn at all, produces no
/// exception and no log line — it produces a picture, and the picture looks like a drawing. So the
/// two invariants that have each been broken more than once get checked mechanically instead.
/// </para>
/// </summary>
public class RendererCoverageTests
{
    private static SyntaxNode Root() =>
        CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "Canvas", "RenderCanvas.cs")))
            .GetRoot();

    /// <summary>
    /// Note 7: every <c>Draw*</c> must apply the shape's <c>OffsetX</c>/<c>OffsetY</c>.
    /// </summary>
    /// <remarks>
    /// Those two fields are how <c>MoveAnimation</c> and <c>PathAnimation</c> move a shape — they are
    /// not written back into the geometry. A <c>Draw*</c> that ignores them therefore renders the
    /// shape at its original position for the whole animation, with no sign that anything is wrong:
    /// the animation runs, the timeline advances, the shape sits still. <c>DrawText</c>,
    /// <c>DrawEllipse</c>, <c>DrawDimension</c> and <c>DrawRadialDimension</c> were all in that state
    /// at once, so animating a label or an ellipse simply did nothing.
    /// </remarks>
    [Fact]
    public void EveryShapeDrawMethodAppliesTheAnimationOffsets()
    {
        var missing = ShapeDrawMethods()
            .Where(m => !m.ToFullString().Contains("OffsetX"))
            .Select(m => m.Identifier.ValueText)
            .ToArray();

        Assert.True(missing.Length == 0,
            "these Draw* methods ignore OffsetX/OffsetY, so MoveAnimation is a no-op for them: " +
            string.Join(", ", missing));
    }

    /// <summary>
    /// Shape-drawing methods: <c>private void DrawX(DrawingContext, TShape)</c> where TShape is a
    /// real geometry type. Excludes the chrome (snap markers, highlights) and the dispatcher.
    /// </summary>
    private static IEnumerable<MethodDeclarationSyntax> ShapeDrawMethods()
    {
        var shapeTypes = typeof(Shape).Assembly.GetTypes()
            .Where(t => typeof(Shape).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToHashSet();

        return Root().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText.StartsWith("Draw", StringComparison.Ordinal))
            .Where(m => m.ParameterList.Parameters.Count == 2)
            .Where(m => m.ParameterList.Parameters[0].Type?.ToString() == "DrawingContext")
            .Where(m => shapeTypes.Contains(m.ParameterList.Parameters[1].Type?.ToString() ?? ""))
            // The dispatcher routes by type and applies the rotation; the offsets are the callee's.
            .Where(m => m.Identifier.ValueText != "DrawShape");
    }

    /// <summary>
    /// There must be exactly one place that switches on shape type.
    /// </summary>
    /// <remarks>
    /// There used to be two — <c>DispatchShapeDraw</c> for top-level shapes and a near-copy inside
    /// the group path — and they had drifted. The copy was missing <c>VRay</c> and <c>VXLine</c> and
    /// had no <c>default</c> arm, so a construction line inside a <see cref="VGroup"/> was silently
    /// not drawn; and it skipped the animated-rotation transform, so a <c>RotateAnimation</c> on a
    /// grouped shape did nothing that the same animation ungrouped did fine. Neither failure
    /// announced itself. This is the same conclusion note 87 reached for the exporters.
    /// </remarks>
    [Fact]
    public void ThereIsOnlyOneShapeTypeSwitchInTheRenderer()
    {
        // A type switch here means: a switch statement whose arms name geometry types.
        var geometryTypes = typeof(Shape).Assembly.GetTypes()
            .Where(t => typeof(Shape).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => t.Name)
            .ToHashSet();

        var dispatchers = Root().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => CountsGeometryCases(m, geometryTypes) >= 5)
            // DrawPreviewShape is a different job, not a second copy of this one: it paints the
            // dashed grey outline a drawing tool drags out, in the preview pen rather than the
            // shape's own style, and it is bounded by what the tools can create rather than by the
            // shape universe. It is allowed to be its own switch; it is not allowed to route to the
            // per-shape Draw* methods, which is what makes something a duplicate of the dispatcher.
            .Where(m => RoutesToShapeDrawMethods(m))
            .Select(m => m.Identifier.ValueText)
            .ToArray();

        Assert.True(dispatchers.Length == 1,
            "expected exactly one shape-type switch in RenderCanvas, found: " + string.Join(", ", dispatchers));
        Assert.Equal("DispatchShapeDraw", dispatchers[0]);
    }

    /// <summary>
    /// True when the method's switch arms hand off to the per-shape <c>Draw*</c> renderers — the
    /// signature of a draw dispatcher, as opposed to a switch that draws something itself.
    /// </summary>
    private static bool RoutesToShapeDrawMethods(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(i => i.Expression.ToString())
            .Count(name => name.StartsWith("Draw", StringComparison.Ordinal) && name != "DrawShape") >= 5;

    private static int CountsGeometryCases(MethodDeclarationSyntax method, HashSet<string> geometryTypes) =>
        method.DescendantNodes().OfType<DeclarationPatternSyntax>()
            .Count(p => geometryTypes.Contains(p.Type.ToString()));

    /// <summary>
    /// The dispatcher must handle every concrete shape type. A type it does not name is a shape that
    /// silently does not appear.
    /// </summary>
    [Fact]
    public void TheDispatcherNamesEveryConcreteShapeType()
    {
        var dispatcher = Root().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "DispatchShapeDraw");

        var named = dispatcher.DescendantNodes().OfType<DeclarationPatternSyntax>()
            .Select(p => p.Type.ToString())
            .ToHashSet();

        // A subclass is covered by its base's arm, so a type counts as handled if it or any of its
        // ancestors is named. VCell/VRectangle ride on VPolygon this way.
        var unhandled = typeof(Shape).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(Shape).IsAssignableFrom(t))
            .Where(t => !Handled(t, named))
            .Select(t => t.Name)
            .ToArray();

        Assert.True(unhandled.Length == 0,
            "these shape types would not be drawn at all: " + string.Join(", ", unhandled));
    }

    private static bool Handled(Type type, HashSet<string> named)
    {
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            if (named.Contains(t.Name)) return true;
        return false;
    }
}
