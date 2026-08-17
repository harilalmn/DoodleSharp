using System;
using System.Collections.Generic;
using System.Linq;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Three smaller API defects found during the documentation pass: diagnostics written to a stream
/// nobody reads, an option no chart honoured, and a cache that handed every caller the same mutable
/// object.
/// </summary>
[Collection("CanvasState")]
public class GeometryApiPolishTests : IDisposable
{
    public void Dispose() => GeometryDiagnostics.Sink = null;

    // ── Diagnostics reach the host ───────────────────────────────────────────

    [Fact]
    public void FailedUnionExplainsItselfThroughTheDiagnosticsSink()
    {
        var messages = new List<string>();
        GeometryDiagnostics.Sink = messages.Add;

        // Two squares far apart cannot union into one polygon.
        var a = new VPolygon(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10), new VXYZ(0, 10));
        var b = new VPolygon(new VXYZ(100, 0), new VXYZ(110, 0), new VXYZ(110, 10), new VXYZ(100, 10));

        var result = BooleanOps.Union(a, b);

        Assert.Null(result);
        Assert.Contains(messages, m => m.Contains("disjoint"));
    }

    [Fact]
    public void NoSinkMeansNoOutputAndNoFailure()
    {
        GeometryDiagnostics.Sink = null;
        Assert.Null(BooleanOps.Union());   // must not throw with nobody listening
    }

    [Fact]
    public void ABrokenSinkDoesNotBreakTheOperation()
    {
        GeometryDiagnostics.Sink = _ => throw new InvalidOperationException("bad sink");
        Assert.Null(BooleanOps.Union());
    }

    // ── ShowLegend is honoured ───────────────────────────────────────────────

    [Fact]
    public void BarChartDrawsALegendWhenAsked()
    {
        var labels = new[] { "Alpha", "Beta", "Gamma" };
        var values = new[] { 3.0, 5.0, 2.0 };

        var without = Chart.Bar(labels, values, new ChartOptions { ShowLegend = false });
        var with = Chart.Bar(labels, values, new ChartOptions { ShowLegend = true });

        // A legend adds a swatch and a label per category.
        Assert.True(with.Shapes.Count > without.Shapes.Count,
            "ShowLegend = true should add shapes; it used to be read by nothing at all");

        var legendLabels = with.Shapes.OfType<VText>().Count(t => labels.Contains(t.Content));
        Assert.True(legendLabels >= labels.Length,
            "each category should appear again as a legend entry");
    }

    [Fact]
    public void PieChartDrawsALegendWhenAsked()
    {
        var labels = new[] { "One", "Two" };
        var values = new[] { 1.0, 3.0 };

        var without = Chart.Pie(values, labels, new ChartOptions { ShowLegend = false });
        var with = Chart.Pie(values, labels, new ChartOptions { ShowLegend = true });

        Assert.True(with.Shapes.Count > without.Shapes.Count);
    }

    // ── Built-in hatches are not shared ──────────────────────────────────────

    [Fact]
    public void BuiltInHatchLookupsAreIndependent()
    {
        var first = BuiltInHatches.Get("ANSI31");
        var second = BuiltInHatches.Get("ANSI31");

        Assert.NotSame(first, second);
        Assert.NotSame(first.Lines[0], second.Lines[0]);
    }

    [Fact]
    public void MutatingAHatchDoesNotPoisonLaterLookups()
    {
        var original = BuiltInHatches.Get("ANSI31");
        double angleBefore = original.Lines[0].Angle;

        original.Lines[0].Angle = angleBefore + 90;
        original.Lines[0].Dashes = new[] { 1.0, -1.0 };

        var fresh = BuiltInHatches.Get("ANSI31");

        Assert.Equal(angleBefore, fresh.Lines[0].Angle);
        Assert.NotEqual(2, fresh.Lines[0].Dashes.Length);
    }

    [Fact]
    public void TheEnumOverloadIsAlsoIndependent()
    {
        var a = BuiltInHatches.Get(BuiltInHatch.ANSI31);
        var b = BuiltInHatches.Get(BuiltInHatch.ANSI31);

        Assert.NotSame(a, b);
    }
}
