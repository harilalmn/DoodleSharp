using System.IO;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The completion list is alphabetical, with snippets ahead of it.
///
/// <para>
/// It used to be ranked — expected type, fuzzy-score band, type-vs-member, scope, then <b>name
/// length</b> — which is an order with no rule a reader can see: a member list on a VLine opened
/// End, Flip, Move, Clone, Scale, Start, Divide, Offset. Finding a member you already knew the name
/// of meant reading every row. Both editor implementations have to follow the rule (note 43), and
/// the ordering is only observable with a real editor, so these are source scans.
/// </para>
/// </summary>
public class CompletionOrderingTests
{
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relativePath));

    [Theory]
    [InlineData("MainWindow.xaml.cs")]
    [InlineData("Editor/SharedEditorController.cs")]
    public void SortCompletionsOrdersByNameAlone(string file)
    {
        var source = Read(file);
        var start = source.IndexOf("List<ICompletionData> SortCompletions(", System.StringComparison.Ordinal);
        Assert.True(start > 0, $"{file} must still have a SortCompletions");

        var body = MethodBody(source, start);

        Assert.Contains("OrderBy", body);
        Assert.Contains("StringComparer.OrdinalIgnoreCase", body);

        // The two terms that made the old order unreadable, plus the ranking keys that would
        // quietly reintroduce it.
        Assert.DoesNotContain("Text.Length", body);
        Assert.DoesNotContain("MatchScore ?? 0", body);
        Assert.DoesNotContain("expectedType", body);
    }

    [Theory]
    [InlineData("MainWindow.xaml.cs", "foreach (var snippet in snippets)", "foreach (var item in sortedCompletions)")]
    [InlineData("Editor/SharedEditorController.cs", "new SnippetCompletionData(trigger, description", "foreach (var item in sorted)")]
    public void SnippetsAreStillFilledInFirst(string file, string snippetSite, string symbolSite)
    {
        // AvalonEdit renders items in insertion order and never consults Priority for it, and the
        // initial selection is item 0 — so an alphabetical symbol list must not be poured in ahead
        // of the snippets, or `for` lands below every symbol starting with a letter before f.
        var source = Read(file);

        var snippets = source.IndexOf(snippetSite, System.StringComparison.Ordinal);
        var symbols = source.IndexOf(symbolSite, System.StringComparison.Ordinal);

        Assert.True(snippets > 0 && symbols > 0, $"both fill sites must exist in {file}");
        Assert.True(snippets < symbols, $"{file} must add snippets before symbols");
    }

    /// <summary>
    /// The source of one method, from its signature to the brace that closes its body. Indentation
    /// differs between the two files, so the extent is found by balancing braces rather than by
    /// matching a leading-whitespace pattern.
    /// </summary>
    private static string MethodBody(string source, int signatureStart)
    {
        var open = source.IndexOf('{', signatureStart);
        Assert.True(open > 0, "the method must have a body");

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source.Substring(signatureStart, i - signatureStart + 1);
            }
        }

        Assert.Fail("unbalanced braces after the SortCompletions signature");
        return "";
    }
}
