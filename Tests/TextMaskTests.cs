using System.IO;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="VText.Mask"/> — the solid background that keeps a label legible where it crosses
/// other geometry (the reported case: dimension labels sitting on the lines they measure).
/// </summary>
public class TextMaskTests
{
    [Fact]
    public void IsOnByDefaultAndFollowsTheCanvasBackground()
    {
        var text = new VText(new VXYZ(0, 0), "hello");

        Assert.True(text.Mask);
        // Null is the sentinel for "whatever the canvas background is", resolved at draw time so a
        // label keeps blending in after the background changes. A captured colour would go stale.
        Assert.Null(text.MaskColor);
        Assert.Equal(0.15, text.MaskOffset, 9);
    }

    [Fact]
    public void ADefaultLabelExportsAPlateInTheCanvasColour()
    {
        var previous = VText.CanvasBackgroundColor;
        try
        {
            VText.CanvasBackgroundColor = "#123456";
            var text = new VText(new VXYZ(0, 0), "433.5", 20);   // nothing set: default mask
            var svg = DoodleSharp.Canvas.SvgExporter.Export(new[] { (IDrawable)text });

            Assert.Contains("<rect", svg);
            Assert.Contains("fill=\"#123456\"", svg);
        }
        finally
        {
            VText.CanvasBackgroundColor = previous;
        }
    }

    [Fact]
    public void AnExplicitMaskColourWinsOverTheCanvasBackground()
    {
        var previous = VText.CanvasBackgroundColor;
        try
        {
            VText.CanvasBackgroundColor = "#123456";
            var text = new VText(new VXYZ(0, 0), "433.5", 20) { MaskColor = "Red" };
            var svg = DoodleSharp.Canvas.SvgExporter.Export(new[] { (IDrawable)text });

            Assert.Contains("fill=\"Red\"", svg);
            Assert.DoesNotContain("#123456", svg);
        }
        finally
        {
            VText.CanvasBackgroundColor = previous;
        }
    }

    [Fact]
    public void DimensionLabelsAreNotMasked()
    {
        // The tessellator's label is how a dimension's number reaches the raster and GPU sinks. The
        // vector renderer draws that number itself and plates it only when the dimension asks
        // (TextBackgroundOpaque), so inheriting the new default here would make one drawing render
        // differently per backend.
        var source = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(),
            "C2VGeometry", "Rendering", "ShapeTessellator.cs"));
        var label = source.IndexOf("private static VText Label(", System.StringComparison.Ordinal);

        Assert.True(label > 0, "the dimension label helper must still exist");
        Assert.Contains("Mask = false", source[label..]);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(4.0, 1.0)]
    public void OffsetIsClampedToZeroThroughOne(double assigned, double expected)
    {
        // It is documented as a fraction of the text height, and the renderer multiplies it by the
        // font size with no clamp of its own — an unbounded value would paint a rectangle many
        // screen-heights tall over the drawing.
        var text = new VText(new VXYZ(0, 0), "hello") { MaskOffset = assigned };

        Assert.Equal(expected, text.MaskOffset, 9);
    }

    [Fact]
    public void CloneCarriesTheMask()
    {
        var text = new VText(new VXYZ(1, 2), "hello")
        {
            Mask = true,
            MaskColor = "#202020",
            MaskOffset = 0.4
        };

        var clone = text.Clone();

        Assert.True(clone.Mask);
        Assert.Equal("#202020", clone.MaskColor);
        Assert.Equal(0.4, clone.MaskOffset, 9);
    }

    [Fact]
    public void TheMaskDoesNotChangeTheBounds()
    {
        // The mask is a rendering flourish, not geometry: a masked label must not start reporting a
        // larger box, or zoom-extents and the cull index would both grow with it.
        var plain = new VText(new VXYZ(0, 0), "hello", 10);
        var masked = new VText(new VXYZ(0, 0), "hello", 10) { Mask = true, MaskOffset = 1.0 };

        Assert.Equal(plain.GetBounds().Min.X, masked.GetBounds().Min.X, 9);
        Assert.Equal(plain.GetBounds().Max.Y, masked.GetBounds().Max.Y, 9);
    }

    [Fact]
    public void TheSvgExportCarriesTheMaskRectAheadOfTheText()
    {
        var masked = new VText(new VXYZ(0, 0), "433.5", 20) { Mask = true, MaskColor = "Black" };
        var svg = DoodleSharp.Canvas.SvgExporter.Export(new[] { (IDrawable)masked });

        var rect = svg.IndexOf("<rect", System.StringComparison.Ordinal);
        var text = svg.IndexOf("<text", System.StringComparison.Ordinal);

        Assert.True(rect > 0, "a masked label must export a background rect");
        Assert.True(text > 0, "the text element must still be there");
        Assert.True(rect < text, "the rect must precede the text, or it covers it");
        Assert.Contains("fill=\"Black\"", svg);
    }

    [Fact]
    public void AnUnmaskedLabelExportsNoRect()
    {
        var plain = new VText(new VXYZ(0, 0), "433.5", 20) { Mask = false };
        var svg = DoodleSharp.Canvas.SvgExporter.Export(new[] { (IDrawable)plain });

        Assert.DoesNotContain("<rect", svg);
    }

    [Fact]
    public void TheMaskIsDrawnBeforeTheGlyphsOnEverySurfaceThatDrawsText()
    {
        // A mask painted after the text hides it — the one way this feature can be silently wrong.
        // The three surfaces that render a VText themselves each need the ordering; a behavioural
        // test would need a window (canvas) or a PDF/SVG differ, hence a scan.
        var root = ArrowheadConsistencyTests.RepoRoot();

        var canvas = File.ReadAllText(Path.Combine(root, "Canvas", "RenderCanvas.cs"));
        var maskDraw = canvas.IndexOf("if (text.Mask)", System.StringComparison.Ordinal);
        var glyphDraw = canvas.IndexOf("if (text.DrawFactor < 1.0)", System.StringComparison.Ordinal);
        Assert.True(maskDraw > 0 && glyphDraw > 0, "both sites must exist in DrawText");
        Assert.True(maskDraw < glyphDraw, "the canvas must paint the mask before the glyphs");

        var svg = File.ReadAllText(Path.Combine(root, "Canvas", "SvgExporter.cs"));
        Assert.Contains("if (t.Mask) inner = MaskToSvg(t) + inner;", svg);

        var pdf = File.ReadAllText(Path.Combine(root, "Export", "PdfExporter.cs"));
        var pdfMask = pdf.IndexOf("if (text.Mask)", System.StringComparison.Ordinal);
        var pdfText = pdf.IndexOf("gfx.DrawString(text.Content ?? \"\", font, brush, 0, 0);", System.StringComparison.Ordinal);
        Assert.True(pdfMask > 0 && pdfText > 0, "both sites must exist in the PDF exporter");
        Assert.True(pdfMask < pdfText, "the PDF exporter must fill the mask before drawing the string");
    }
}
