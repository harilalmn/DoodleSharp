using System.IO;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="VText.Justify"/> — how the lines of a multi-line label line up with each other,
/// which is a separate question from where <see cref="VText.Anchor"/> puts the block.
/// </summary>
public class TextJustifyTests
{
    private const string ThreeLines = "\u03b8 = 13\nRadius r = 1\nx = 0.9074";

    [Fact]
    public void DefaultsToLeft()
    {
        Assert.Equal(VTextJustify.Left, new VText(new VXYZ(0, 0), ThreeLines).Justify);
    }

    [Fact]
    public void SurvivesClone()
    {
        var text = new VText(new VXYZ(1, 2), ThreeLines)
        {
            Justify = VTextJustify.Center,
            Anchor = VTextAnchor.MiddleCenter
        };

        var clone = text.Clone();

        Assert.Equal(VTextJustify.Center, clone.Justify);
        Assert.Equal(VTextAnchor.MiddleCenter, clone.Anchor);
    }

    /// <summary>
    /// Justification lays out the inside of the block; it must not move or resize the block itself,
    /// or a label would jump on the drawing when its ragged edge changed. The anchor owns placement
    /// and stays the only thing that decides it.
    /// </summary>
    [Theory]
    [InlineData(VTextJustify.Left)]
    [InlineData(VTextJustify.Center)]
    [InlineData(VTextJustify.Right)]
    public void DoesNotChangeTheBounds(VTextJustify justify)
    {
        var reference = new VText(new VXYZ(3, 4), ThreeLines) { Height = 10 }.GetBounds();

        var text = new VText(new VXYZ(3, 4), ThreeLines) { Height = 10, Justify = justify };
        var bounds = text.GetBounds();

        Assert.Equal(reference.Min.X, bounds.Min.X, 9);
        Assert.Equal(reference.Min.Y, bounds.Min.Y, 9);
        Assert.Equal(reference.Max.X, bounds.Max.X, 9);
        Assert.Equal(reference.Max.Y, bounds.Max.Y, 9);
    }

    /// <summary>
    /// The renderer is the one surface that lays multi-line text out as lines, so it is the one
    /// that has to consult Justify. Pinned by reading the source, in the manner of the other
    /// wiring guards here: a property nothing reads is the failure that looks exactly like success.
    /// </summary>
    [Fact]
    public void TheRendererAppliesIt()
    {
        var source = File.ReadAllText(RepoFile("Canvas/RenderCanvas.cs"));

        Assert.Contains("ApplyJustification(formattedText, text.Justify", source);
        // WPF aligns lines inside MaxTextWidth; without it TextAlignment is inert and all three
        // justifications render identically.
        Assert.Contains("MaxTextWidth", source);
        Assert.Contains("TextAlignment", source);
    }

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DoodleSharp.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }
}
