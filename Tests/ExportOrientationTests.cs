using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// What an exporter is allowed to forget about a shape: nothing the canvas draws.
///
/// <para>
/// Every exporter kept its own type switch, and three of them independently rebuilt a rectangle
/// from <c>Corner</c>, <c>Width</c> and <c>Height</c> — throwing away its rotation — and drew an
/// ellipse from its centre and radii, throwing away its sweep and its orientation. None of that
/// failed loudly: the file was valid, the shape was there, and it was simply the wrong shape. These
/// tests compare what comes out against the geometry itself rather than against a golden file, so
/// they keep holding as the formats change.
/// </para>
/// </summary>
public class ExportOrientationTests
{
    private static string Svg(params Shape[] shapes) =>
        SvgExporter.Export(shapes.Cast<IDrawable>().ToArray());

    private static string Dxf(params Shape[] shapes) =>
        new DoodleSharp.Export.DxfExporter().ExportToString(shapes.Cast<IDrawable>().ToArray());

    /// <summary>Every number in the string, in order, as doubles.</summary>
    private static List<double> Numbers(string text) =>
        Regex.Matches(text, @"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?")
             .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture))
             .ToList();

    private static bool Mentions(string text, double value, double tolerance = 0.01) =>
        Numbers(text).Any(n => Math.Abs(n - value) < tolerance);

    // ---- a rotated rectangle keeps its rotation ----

    private static VRectangle RotatedRect()
    {
        // Turned 45 degrees, so no corner keeps an axis-aligned coordinate: if the rotation is
        // dropped the exported numbers are a completely different set, not merely reordered.
        var rect = new VRectangle(new VXYZ(-5, -2), 10, 4) { RotationAngle = 45 };
        return rect;
    }

    [Fact]
    public void SvgKeepsARectanglesRotation()
    {
        var rect = RotatedRect();
        var svg = Svg(rect);

        // A <rect> element cannot express a rotation, so its very presence is the bug.
        Assert.DoesNotContain("<rect", svg);

        foreach (var corner in rect.Points)
        {
            Assert.True(Mentions(svg, corner.X) && Mentions(svg, corner.Y),
                $"corner {corner} is missing from the SVG");
        }
    }

    [Fact]
    public void DxfKeepsARectanglesRotation()
    {
        var rect = RotatedRect();
        var dxf = Dxf(rect);

        foreach (var corner in rect.Points)
        {
            Assert.True(Mentions(dxf, corner.X) && Mentions(dxf, corner.Y),
                $"corner {corner} is missing from the DXF");
        }
    }

    [Fact]
    public void AnUnrotatedRectangleStillExportsItsCorners()
    {
        var rect = new VRectangle(new VXYZ(1, 2), 10, 4);

        foreach (var corner in rect.Points)
        {
            Assert.True(Mentions(Svg(rect), corner.X), $"SVG lost {corner}");
            Assert.True(Mentions(Dxf(rect), corner.X), $"DXF lost {corner}");
        }
    }

    // ---- a partial ellipse exports its sweep, not the whole ellipse ----

    [Fact]
    public void SvgDoesNotTurnAHalfEllipseIntoAWholeOne()
    {
        var half = new VEllipse(new VXYZ(0, 0), 10, 5, 0, 180);
        var svg = Svg(half);

        // <ellipse> is the whole-ellipse element and has no way to say "half".
        Assert.DoesNotContain("<ellipse", svg);

        // Nothing below the X axis: the missing half must genuinely be missing.
        Assert.All(SampledYValues(svg), y => Assert.True(y > -0.01, $"y {y} is on the discarded half"));
    }

    [Fact]
    public void SvgKeepsAWholeEllipseAsAnEllipseElement()
    {
        // The common case must not get more expensive or less exact than it was.
        Assert.Contains("<ellipse", Svg(new VEllipse(new VXYZ(0, 0), 10, 5)));
    }

    [Fact]
    public void SvgKeepsAnEllipsesOrientation()
    {
        var svg = Svg(new VEllipse(new VXYZ(0, 0), 10, 5) { Rotation = 30 });
        Assert.Contains("rotate(", svg);
    }

    [Fact]
    public void DxfDoesNotTurnAHalfEllipseIntoAWholeOne()
    {
        var dxf = Dxf(new VEllipse(new VXYZ(0, 0), 10, 5, 0, 180));

        // Group 20 is a vertex Y. Every vertex of the upper half is at or above the axis.
        foreach (var y in GroupValues(dxf, 20))
            Assert.True(y > -0.01, $"vertex y {y} is on the discarded half");
    }

    [Fact]
    public void DxfKeepsAnEllipsesOrientation()
    {
        // A 10x5 ellipse turned a quarter turn reaches +-10 in Y and +-5 in X, not the reverse.
        var dxf = Dxf(new VEllipse(new VXYZ(0, 0), 10, 5) { Rotation = 90 });

        var xs = GroupValues(dxf, 10);
        var ys = GroupValues(dxf, 20);

        Assert.True(ys.Max() > 9.9, $"max y was {ys.Max()}, expected about 10");
        Assert.True(xs.Max() < 5.1, $"max x was {xs.Max()}, expected about 5");
    }

    // ---- SVG text lands where the canvas drew it ----

    /// <summary>
    /// A masked label's plate and its glyphs must end up in the same place. They are emitted through
    /// different code paths — the rect straight into the flipped group, the text inside an element
    /// with its own counter-flip — and SVG transforms <b>compose</b> rather than replace, which is
    /// what made the text land at the reflection of its position while the plate stayed put.
    /// </summary>
    [Fact]
    public void AMasksPlateAndItsGlyphsShareAPosition()
    {
        var label = new VText(new VXYZ(0, 40), "Hello", 10) { Mask = true };
        var svg = Svg(label);

        var rectY = double.Parse(Regex.Match(svg, @"<rect[^>]*\sy=""(-?[\d.]+)""").Groups[1].Value,
                                 CultureInfo.InvariantCulture);
        var textY = double.Parse(Regex.Match(svg, @"<text[^>]*\sy=""(-?[\d.]+)""").Groups[1].Value,
                                 CultureInfo.InvariantCulture);

        // Both are in document space, where Y grows downward, so both are negative for a label
        // above the origin, and the baseline sits within the plate.
        Assert.True(textY < 0, $"text y {textY} should be negative for a label at world y = 40");
        Assert.True(Math.Abs(rectY - textY) < label.Height * 2,
            $"plate at {rectY} and glyphs at {textY} are not on the same label");
    }

    [Fact]
    public void SvgGivesAMultiLineLabelOneElementPerLine()
    {
        var svg = Svg(new VText(new VXYZ(0, 0), "one\ntwo\nthree", 10) { Mask = false });

        Assert.Equal(3, Regex.Matches(svg, "<text").Count);
        foreach (var word in new[] { "one", "two", "three" })
            Assert.Contains($">{word}<", svg);
    }

    [Fact]
    public void SvgPlacesALabelAgainstItsAnchor()
    {
        // A right-anchored label hangs to the LEFT of its point, so its box starts at a negative x.
        var left = new VText(new VXYZ(0, 0), "Hello", 10) { Mask = false, Anchor = VTextAnchor.BottomLeft };
        var right = new VText(new VXYZ(0, 0), "Hello", 10) { Mask = false, Anchor = VTextAnchor.BottomRight };

        var leftX = double.Parse(Regex.Match(Svg(left), @"<text[^>]*\sx=""(-?[\d.]+)""").Groups[1].Value,
                                 CultureInfo.InvariantCulture);
        var rightX = double.Parse(Regex.Match(Svg(right), @"<text[^>]*\sx=""(-?[\d.]+)""").Groups[1].Value,
                                  CultureInfo.InvariantCulture);

        Assert.True(rightX < leftX, $"a right-anchored label ({rightX}) must sit left of a left-anchored one ({leftX})");
    }

    // ---- helpers ----

    /// <summary>Every value carried by the given DXF group code.</summary>
    private static List<double> GroupValues(string dxf, int groupCode)
    {
        var lines = dxf.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        var values = new List<double>();
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            if (int.TryParse(lines[i], out var code) && code == groupCode &&
                double.TryParse(lines[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                values.Add(v);
            }
        }
        Assert.NotEmpty(values);
        return values;
    }

    /// <summary>
    /// The Y coordinates of a sampled SVG path, back in world orientation. The document group flips
    /// Y, so the sign is undone here.
    /// </summary>
    private static List<double> SampledYValues(string svg)
    {
        var d = Regex.Match(svg, @"<path d=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(d), "expected a sampled path in the SVG");

        var numbers = Numbers(d);
        var ys = new List<double>();
        for (int i = 1; i < numbers.Count; i += 2) ys.Add(numbers[i]);
        return ys;
    }
}
