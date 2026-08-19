using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Exporting a divided drawing: every cell tiled onto one page as it appears on screen.
///
/// <para>
/// The invariant that matters most is the one about <i>not</i> tiling: an undivided drawing must
/// take the path it always took, byte for byte. The single-cell case is what every existing export
/// has produced, and the tiled one is a different picture — it reproduces the <i>view</i>, where the
/// historical export fits the <i>shapes</i>.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class ViewportExportTests : IDisposable
{
    private readonly IShapeRegistry? _previousRegistry;

    public ViewportExportTests()
    {
        _previousRegistry = Shape.DefaultRegistry;
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        Shape.AutoRegister = true;
        CanvasRenderer.Instance.ClearShapes();
        Viewport.Reset();
    }

    public void Dispose()
    {
        CanvasRenderer.Instance.ClearShapes();
        Viewport.Reset();
        Shape.DefaultRegistry = _previousRegistry;
    }

    private static SvgExporter.SvgTile Tile(Rect page, double scale, double panX, double panY, params IDrawable[] shapes)
        => new(page, scale, panX, panY, shapes);

    /// <summary>Pulls the six numbers out of a tile group's transform, in order.</summary>
    private static double[] Matrix(string svg, int index)
    {
        var groups = Regex.Matches(svg, @"transform=""matrix\(([^)]*)\)""");
        Assert.True(groups.Count > index, $"expected at least {index + 1} tile groups");
        return groups[index].Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
            .ToArray();
    }

    /// <summary>
    /// The transform is what makes "as it appears on screen" literal. A cell showing the world origin
    /// centred must put it at the centre of that cell's rectangle on the page — and the Y scale must
    /// be negative, because world Y is up and page Y is down.
    /// </summary>
    [Fact]
    public void ATilePutsTheWorldOriginAtTheCentreOfItsRectangle()
    {
        var line = new VLine(new VXYZ(0, 0), new VXYZ(10, 0));

        var svg = SvgExporter.ExportTiled(new[]
        {
            Tile(new Rect(0, 0, 320, 270), scale: 1, panX: 0, panY: 0, line),
        }, 960, 540);

        var m = Matrix(svg, 0);

        Assert.Equal(1, m[0]);          // scale x
        Assert.Equal(0, m[1]);
        Assert.Equal(0, m[2]);
        Assert.Equal(-1, m[3]);         // scale y, flipped
        Assert.Equal(160, m[4]);        // tile left + half its width
        Assert.Equal(135, m[5]);        // tile top + half its height
    }

    [Fact]
    public void EachTileCarriesItsOwnZoomAndPan()
    {
        var a = new VLine(new VXYZ(0, 0), new VXYZ(1, 0));
        var b = new VLine(new VXYZ(0, 0), new VXYZ(1, 0));

        var svg = SvgExporter.ExportTiled(new[]
        {
            Tile(new Rect(0, 0, 400, 300), scale: 2, panX: 0, panY: 0, a),
            Tile(new Rect(400, 0, 400, 300), scale: 0.5, panX: 30, panY: -20, b),
        }, 800, 300);

        var first = Matrix(svg, 0);
        var second = Matrix(svg, 1);

        Assert.Equal(2, first[0]);
        Assert.Equal(-2, first[3]);
        Assert.Equal(200, first[4]);

        Assert.Equal(0.5, second[0]);
        Assert.Equal(-0.5, second[3]);
        Assert.Equal(630, second[4]);    // 400 + 200 + 30
        Assert.Equal(130, second[5]);    // 0 + 150 - 20
    }

    /// <summary>
    /// Cells clip on screen, so they must clip on the page — otherwise a zoomed-in cell spills its
    /// geometry across its neighbours.
    /// </summary>
    [Fact]
    public void EveryTileIsClippedToItsOwnRectangle()
    {
        var svg = SvgExporter.ExportTiled(new[]
        {
            Tile(new Rect(0, 0, 400, 300), 1, 0, 0, new VLine(new VXYZ(0, 0), new VXYZ(1, 0))),
            Tile(new Rect(400, 0, 400, 300), 1, 0, 0, new VLine(new VXYZ(0, 0), new VXYZ(1, 0))),
        }, 800, 300);

        Assert.Contains("<clipPath id=\"viewport0\">", svg, StringComparison.Ordinal);
        Assert.Contains("<clipPath id=\"viewport1\">", svg, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(svg, @"clip-path=""url\(#viewport\d+\)""").Count);
    }

    [Fact]
    public void ADividedExportShowsWhereTheCellsAre()
    {
        var svg = SvgExporter.ExportTiled(new[]
        {
            Tile(new Rect(0, 0, 400, 300), 1, 0, 0),
            Tile(new Rect(400, 0, 400, 300), 1, 0, 0),
        }, 800, 300);

        Assert.Equal(2, Regex.Matches(svg, @"stroke=""#333333""").Count);
    }

    /// <summary>
    /// Line weights are device pixels, so a tile's scale must not thicken them — the same rule that
    /// makes strokes survive the untiled export.
    /// </summary>
    [Fact]
    public void ScalingATileDoesNotThickenItsStrokes()
    {
        var svg = SvgExporter.ExportTiled(new[]
        {
            Tile(new Rect(0, 0, 400, 300), scale: 8, panX: 0, panY: 0,
                 new VLine(new VXYZ(0, 0), new VXYZ(10, 10))),
        }, 400, 300);

        foreach (Match m in Regex.Matches(svg, @"<(line|path|polyline|polygon|circle|ellipse|rect)\b[^>]*>"))
        {
            if (!m.Value.Contains("stroke-width", StringComparison.Ordinal)) continue;
            Assert.Contains("vector-effect=\"non-scaling-stroke\"", m.Value, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The invariant. An undivided drawing must not go anywhere near the tiled path, because the two
    /// produce different pictures and only one of them is what every existing export looks like.
    /// </summary>
    [Fact]
    public void AnUndividedDrawingTakesTheHistoricalPath()
    {
        var source = File.ReadAllText(
            Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

        var start = source.IndexOf("private void ExportSvgButton_Click(", StringComparison.Ordinal);
        Assert.True(start > 0, "the SVG export handler must exist");
        var next = source.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        var body = source[start..next];

        Assert.Contains("ViewportHost.IsDivided", body, StringComparison.Ordinal);
        Assert.Contains("SvgExporter.SaveToFile(dialog.FileName, shapes)", body, StringComparison.Ordinal);
        Assert.Contains("SaveTiledToFile", body, StringComparison.Ordinal);
    }
}
