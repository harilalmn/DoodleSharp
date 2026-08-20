using System;
using System.Linq;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Multi-line <see cref="VText"/> in a DXF export.
///
/// <para>
/// A DXF group value is a whole line of the file, so writing <c>Content</c> straight into group 1
/// put an embedded newline into the file as a bare line — and a reader then took that line as the
/// next group CODE, desynchronising the entity stream from that point on. The label did not merely
/// lose its line breaks; the file stopped being parseable. TEXT has no multi-line form, so each
/// line is written as its own entity.
/// </para>
/// </summary>
public class DxfMultiLineTextTests
{
    private static string Export(VText text) =>
        new DoodleSharp.Export.DxfExporter().ExportToString(new[] { (IDrawable)text });

    /// <summary>
    /// The structural invariant the corruption broke: in a well-formed DXF the file is strictly
    /// alternating group code / value pairs, so every even-indexed line of the entity stream parses
    /// as an integer. A stray newline inside a value shifts everything after it by one and this
    /// fails immediately.
    /// </summary>
    [Fact]
    public void EveryGroupCodeIsStillAnInteger()
    {
        var text = new VText(new VXYZ(0, 0), "first line\nsecond line\nthird line") { Height = 5 };

        var lines = Export(text)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .ToList();

        // Trailing blank from the final newline is not part of a pair.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        Assert.True(lines.Count % 2 == 0, "DXF must be an even number of code/value lines.");

        for (int i = 0; i < lines.Count; i += 2)
        {
            Assert.True(int.TryParse(lines[i], out _),
                $"Line {i} should be a group code but was \"{lines[i]}\" — the stream has desynchronised.");
        }
    }

    [Fact]
    public void EachLineBecomesItsOwnTextEntity()
    {
        var dxf = Export(new VText(new VXYZ(0, 0), "alpha\nbeta\ngamma") { Height = 5 });

        Assert.Equal(3, CountOccurrences(dxf, "\nTEXT"));
        Assert.Contains("alpha", dxf);
        Assert.Contains("beta", dxf);
        Assert.Contains("gamma", dxf);
    }

    /// <summary>
    /// Lines stack downwards from the location, so the label reads in the same order it was written
    /// rather than piling every line on one point.
    /// </summary>
    [Fact]
    public void LinesStackDownwards()
    {
        var dxf = Export(new VText(new VXYZ(0, 100), "one\ntwo") { Height = 10 });

        var ys = GroupValues(dxf, "20").Select(double.Parse).ToList();

        Assert.Equal(2, ys.Count);
        Assert.Equal(100, ys[0], 6);
        Assert.True(ys[1] < ys[0], $"second line should sit below the first (got {ys[1]} vs {ys[0]})");
    }

    [Fact]
    public void SingleLineTextIsUnchanged()
    {
        var dxf = Export(new VText(new VXYZ(0, 0), "just one") { Height = 5 });

        Assert.Equal(1, CountOccurrences(dxf, "\nTEXT"));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    /// <summary>Values following the given group code, in file order.</summary>
    private static System.Collections.Generic.List<string> GroupValues(string dxf, string code)
    {
        var lines = dxf.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        var found = new System.Collections.Generic.List<string>();
        for (int i = 0; i + 1 < lines.Count; i += 2)
        {
            if (lines[i] == code) found.Add(lines[i + 1]);
        }
        return found;
    }
}
