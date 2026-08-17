using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;
using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Tests;

/// <summary>
/// Every exportable shape type must actually appear in every export format.
///
/// <para>
/// Each exporter carries its own <c>switch (shape)</c> mapping a type to that format's native
/// construct, which is right — flattening a circle to sixty-four chords in a DXF would make the
/// file useless in a CAD package. What was wrong is what those switches did with a type they had
/// never heard of: nothing. They fell off the end, the shape vanished, and nothing reported it.
/// Because the switches were written separately they had drifted to cover different subsets, so a
/// drawing could export correctly to SVG and silently lose the same shapes in DXF.
/// </para>
///
/// <para>
/// This walks the real public shape surface by reflection rather than a hand-written list, so a
/// shape type added later is covered the day it appears instead of the day someone remembers.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ExporterCoverageTests : IDisposable
{
    public ExporterCoverageTests() => Shape.DefaultRegistry = null;
    public void Dispose() => Shape.DefaultRegistry = null;

    /// <summary>
    /// One instance of every concrete public shape, built at a known size around the origin.
    /// Types needing constructor arguments we cannot guess are skipped and reported.
    /// </summary>
    private static IEnumerable<Shape> SampleShapes()
    {
        yield return new VPoint(5, 5);
        yield return new VLine(new VXYZ(0, 0), new VXYZ(10, 10));
        yield return new VCircle(new VXYZ(5, 5), 4);
        yield return new VArc(new VXYZ(5, 5), 4, 0, 120);
        yield return new VEllipse(new VXYZ(5, 5), 6, 3);
        yield return new VRectangle(new VXYZ(0, 0), 10, 6);
        yield return new VPolygon(new VXYZ(0, 0), new VXYZ(8, 0), new VXYZ(4, 7));
        yield return new VPolyline(new VXYZ(0, 0), new VXYZ(4, 3), new VXYZ(9, 1));
        yield return new VBezier(new VXYZ(0, 0), new VXYZ(3, 6), new VXYZ(7, -4), new VXYZ(10, 2));
        yield return new VSpline(new VXYZ(0, 0), new VXYZ(3, 5), new VXYZ(7, -2), new VXYZ(10, 3));
        yield return new VText(new VXYZ(1, 1), "Ab");
        yield return new VArrow(new VXYZ(0, 0), new VXYZ(9, 4));
        yield return new VDimension(new VXYZ(0, 0), new VXYZ(10, 0));
        yield return new VRadialDimension(new VXYZ(5, 5), 4);
        yield return new VGroup(new VLine(new VXYZ(0, 0), new VXYZ(5, 5)));
        yield return new VHatch(
            new List<VXYZ> { new(0, 0), new(9, 0), new(9, 9), new(0, 9) }, "ANSI31");
        yield return new Region(new VPolygon(new VXYZ(0, 0), new VXYZ(9, 0), new VXYZ(9, 9)));
        yield return new VRay(new VXYZ(0, 0), new VXYZ(1, 1));
        yield return new VXLine(new VXYZ(0, 0), new VXYZ(1, 0));
    }

    /// <summary>
    /// Reflection guard: if a new concrete public shape appears and is not in the sample set above,
    /// this fails rather than quietly leaving it untested.
    /// </summary>
    [Fact]
    public void SampleSetCoversEveryPublicShapeType()
    {
        var covered = SampleShapes().Select(s => s.GetType()).ToHashSet();

        var all = typeof(Shape).Assembly.GetExportedTypes()
            .Where(t => typeof(Shape).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic)
            // VCell is only ever created by VSpatialGrid; VGrid and VSpatialGrid are containers that
            // materialise their own children, and are covered through those.
            .Where(t => t.Name is not ("VCell" or "VGrid" or "VSpatialGrid"))
            .ToList();

        var missing = all.Where(t => !covered.Contains(t)).Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0,
            "These shape types are not exercised by the exporter tests: " + string.Join(", ", missing) +
            ". Add an instance to SampleShapes() so the exporters are proven to handle it.");
    }

    [Fact]
    public void SvgExportContainsEveryShapeType()
    {
        foreach (var shape in SampleShapes())
        {
            var svg = SvgExporter.Export(new[] { (IDrawable)shape }, 400, 400, 10);
            Assert.False(string.IsNullOrWhiteSpace(svg), $"{shape.GetType().Name}: empty SVG");

            // A bare document with no drawing element means the shape was dropped.
            var hasGeometry =
                svg.Contains("<path") || svg.Contains("<circle") || svg.Contains("<line") ||
                svg.Contains("<polyline") || svg.Contains("<polygon") || svg.Contains("<ellipse") ||
                svg.Contains("<text") || svg.Contains("<rect");

            Assert.True(hasGeometry, $"{shape.GetType().Name} produced an SVG with no drawing element.");
        }
    }

    [Fact]
    public void DxfExportContainsEveryShapeType()
    {
        foreach (var shape in SampleShapes())
        {
            var dxf = new DoodleSharp.Export.DxfExporter().ExportToString(new[] { (IDrawable)shape });
            Assert.False(string.IsNullOrWhiteSpace(dxf), $"{shape.GetType().Name}: empty DXF");

            // ENTITIES must contain something between its header and ENDSEC.
            var start = dxf.IndexOf("ENTITIES", StringComparison.Ordinal);
            var end = dxf.IndexOf("ENDSEC", start > 0 ? start : 0, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, $"{shape.GetType().Name}: no ENTITIES section");

            var body = dxf.Substring(start, end - start);
            var hasEntity =
                body.Contains("LINE") || body.Contains("CIRCLE") || body.Contains("ARC") ||
                body.Contains("POLYLINE") || body.Contains("POINT") || body.Contains("TEXT") ||
                body.Contains("ELLIPSE");

            Assert.True(hasEntity,
                $"{shape.GetType().Name} produced a DXF with an empty ENTITIES section — it was dropped.");
        }
    }

    [Fact]
    public void PdfExportSucceedsForEveryShapeType()
    {
        foreach (var shape in SampleShapes())
        {
            var path = Path.Combine(Path.GetTempPath(), $"ds_pdf_{Guid.NewGuid():N}.pdf");
            try
            {
                new DoodleSharp.Export.PdfExporter().Export(new[] { (IDrawable)shape }, path);
                Assert.True(File.Exists(path), $"{shape.GetType().Name}: no PDF written");
                Assert.True(new FileInfo(path).Length > 400, $"{shape.GetType().Name}: PDF suspiciously small");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    /// <summary>
    /// The tessellator is the floor every exporter falls back to, so anything it declines is a type
    /// that can still go missing. Text and the annotation shapes are declined by design — their
    /// drawing rules live in the host — but they must be declined, not silently produce nothing.
    /// </summary>
    [Fact]
    public void TessellatorEitherHandlesAShapeOrSaysItCannot()
    {
        var tess = new C2VGeometry.Rendering.ShapeTessellator();

        foreach (var shape in SampleShapes())
        {
            var emitted = 0;
            var sink = new C2VGeometry.Rendering.PolylineFallbackSink
            {
                OnPolyline = (_, _, _) => emitted++,
                OnPoint = (_, _) => emitted++,
                OnText = _ => emitted++,
                OnFilled = (_, _) => emitted++,
            };

            var handled = tess.Tessellate(shape, sink);

            Assert.True(!handled || emitted > 0,
                $"{shape.GetType().Name}: Tessellate reported success but emitted no primitives, " +
                "so a caller relying on the return value would drop it.");
        }
    }
}
