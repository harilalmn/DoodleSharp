using System.Collections.Generic;
using System.Linq;
using C2VGeometry;
using DoodleSharp.Animation;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Fake provider so the C2VGeometry-side glyph logic can be tested without WPF fonts.
/// 'O' yields two contours (outer + hole); any other non-space letter yields one square.
/// </summary>
file sealed class FakeGlyphProvider : IGlyphOutlineProvider
{
    public List<List<VXYZ>>? GetCharContours(VText text, int charIndex)
    {
        char c = text.Content[charIndex];
        if (char.IsWhiteSpace(c)) return null;
        var outer = new List<VXYZ> { new(0, 0), new(20, 0), new(20, 20), new(0, 20) };
        if (c == 'O')
        {
            var hole = new List<VXYZ> { new(6, 6), new(14, 6), new(14, 14), new(6, 14) };
            return new List<List<VXYZ>> { outer, hole };
        }
        return new List<List<VXYZ>> { outer };
    }
}

[Collection("CanvasState")]
public class TextGlyphTests
{
    public TextGlyphTests()
    {
        Shape.DefaultRegistry = CanvasRenderer.Instance;
        CanvasRenderer.Instance.Clear();
        Shape.AutoRegister = true;
        VText.GlyphOutlineProvider = new FakeGlyphProvider();
    }

    [Fact]
    public void ToCharShape_SingleContour_ReturnsClosedPolyline()
    {
        var txt = new VText(new VXYZ(0, 0), "AB", 20);
        var shape = txt.ToCharShape(0);

        var poly = Assert.IsType<VPolyline>(shape);
        // Closed: first ≈ last
        Assert.True(poly.Points[0].Subtract(poly.Points[^1]).GetLength() < 1e-6);
        // ToCharShape must NOT mutate the text.
        Assert.Equal("AB", txt.Content);
    }

    [Fact]
    public void ToCharShape_MultiContour_ReturnsGroupOfContours()
    {
        var txt = new VText(new VXYZ(0, 0), "O", 20);
        var shape = txt.ToCharShape(0);

        var group = Assert.IsType<VGroup>(shape);
        Assert.Equal(2, group.Shapes.Count);
        Assert.All(group.Shapes, s => Assert.IsType<VPolyline>(s));
    }

    [Fact]
    public void LiftChar_BlanksTheCharacter()
    {
        var txt = new VText(new VXYZ(0, 0), "AB", 20);
        var shape = txt.LiftChar(0);

        Assert.NotNull(shape);
        Assert.Equal(" B", txt.Content);
    }

    [Fact]
    public void Indexer_LiftsAndBlanks()
    {
        var txt = new VText(new VXYZ(0, 0), "Hi", 20);
        var shape = txt[1];

        Assert.NotNull(shape);
        Assert.Equal("H ", txt.Content);
    }

    [Fact]
    public void Whitespace_OutOfRange_AndNoProvider_ReturnNull()
    {
        var txt = new VText(new VXYZ(0, 0), "A B", 20);
        Assert.Null(txt.ToCharShape(1));   // space
        Assert.Null(txt.ToCharShape(99));  // out of range
        Assert.Null(txt.ToCharShape(-1));  // out of range

        VText.GlyphOutlineProvider = null;
        Assert.Null(txt.ToCharShape(0));   // no provider
        VText.GlyphOutlineProvider = new FakeGlyphProvider();
    }

    [Fact]
    public void LiftChars_GroupsAndBlanksRange()
    {
        var txt = new VText(new VXYZ(0, 0), "ABCD", 20);
        var group = txt.LiftChars(1, 2); // 'B','C'

        Assert.NotNull(group);
        Assert.Equal(2, group!.Shapes.Count);
        Assert.Equal("A  D", txt.Content);
    }

    [Fact]
    public void CharTransform_KeepsWordVisible_AndBlanksOnlyWhenStarted()
    {
        var word = new VText(new VXYZ(0, 0), "AB", 20);
        word.Draw();
        var target = new VCircle(100, 0, 10);

        // The (VText, index, to, duration) overload must NOT blank up front.
        var anim = new TransformAnimation(word, 0, target, 1.0);
        Assert.Equal("AB", word.Content);   // word still intact after construction

        anim.Apply(-0.5);                    // before its turn
        Assert.Equal("AB", word.Content);    // still intact

        anim.Apply(0.0);                     // morph begins
        Assert.Equal(" B", word.Content);    // now the character is blanked

        anim.Apply(0.5);                     // mid-morph, idempotent
        Assert.Equal(" B", word.Content);
    }

    [Fact]
    public void CharTransform_ThrowsForWhitespace()
    {
        var word = new VText(new VXYZ(0, 0), "A B", 20);
        word.Draw();
        var target = new VCircle(0, 0, 10);
        Assert.Throws<System.ArgumentException>(() => new TransformAnimation(word, 1, target, 1.0));
    }

    [Fact]
    public void TransformAnimation_MorphsGlyphGroupByDominantContour_NotBoundingBox()
    {
        var txt = new VText(new VXYZ(0, 0), "O", 20);
        var glyph = txt[0];                 // VGroup: outer 20x20 + inner 8x8 hole
        var target = new VCircle(100, 0, 10);

        var anim = new TransformAnimation(glyph!, target, 1.0);
        var proxy = CanvasRenderer.Instance.GetShapes()
            .OfType<VPolyline>()
            .First(p => p.Name.StartsWith("__transform_morph_"));

        anim.Apply(0.0001); // ~ source state: should trace the outer contour (the dominant one)
        // The outer contour is the 20x20 square (5 closed pts); a bbox fallback would also be
        // ~20x20, so assert the proxy has many sample points (not 4 bbox corners) AND closes.
        Assert.True(proxy.Points.Count > 8);
        double w = proxy.Points.Max(p => p.X) - proxy.Points.Min(p => p.X);
        double h = proxy.Points.Max(p => p.Y) - proxy.Points.Min(p => p.Y);
        Assert.InRange(w, 18, 22);
        Assert.InRange(h, 18, 22);
    }
}
