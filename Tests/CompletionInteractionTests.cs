using System.IO;
using System.Linq;
using DoodleSharp.Editor;

namespace DoodleSharp.Tests;

/// <summary>
/// Rules for what an open completion list does when the next character is typed. These decide
/// whether the editor silently rewrites what the user typed, so they are worth pinning precisely.
/// </summary>
public class CompletionInteractionTests
{
    [Theory]
    [InlineData('(')]
    [InlineData('[')]
    [InlineData('{')]
    [InlineData(';')]
    [InlineData(',')]
    [InlineData(')')]
    public void CommitCharactersAcceptTheSelection(char c)
    {
        Assert.True(CompletionInteraction.Commits(c));
    }

    [Fact]
    public void SpaceDismissesAndNeverCommits()
    {
        // Regression: committing on space rewrote the keyword the user was typing. The list opens
        // while typing `new`, and `new ` then became `VXYZ `; `new VXYZ(10, ` became
        // `new VXYZ(10,Viz )`. Space must dismiss, never accept.
        Assert.False(CompletionInteraction.Commits(' '));
        Assert.True(CompletionInteraction.Dismisses(' '));
    }

    [Theory]
    [InlineData('a')]
    [InlineData('Z')]
    [InlineData('7')]
    [InlineData('_')]
    public void IdentifierCharactersNeitherCommitNorDismiss(char c)
    {
        Assert.False(CompletionInteraction.Commits(c));
        Assert.False(CompletionInteraction.Dismisses(c));
    }

    [Theory]
    [InlineData('+')]
    [InlineData('=')]
    [InlineData('"')]
    [InlineData('/')]
    public void OtherCharactersDismiss(char c)
    {
        Assert.True(CompletionInteraction.Dismisses(c));
        Assert.False(CompletionInteraction.Commits(c));
    }

    [Theory]
    [InlineData("new", true)]
    [InlineData("is", true)]
    [InlineData("as", true)]
    [InlineData("for", false)]
    [InlineData("point", false)]
    [InlineData(null, false)]
    public void PrimingKeywordsAreRecognised(string? word, bool expected)
    {
        Assert.Equal(expected, CompletionInteraction.IsPrimingKeyword(word));
    }

    [Theory]
    [InlineData("VXYZ p = new ", "new")]
    [InlineData("VXYZ p = new", "new")]
    [InlineData("VXYZ p = new   ", "new")]      // several spaces
    [InlineData("var x = 1;", null)]            // punctuation, not a word
    [InlineData("point", "point")]
    [InlineData("", null)]
    public void WordBeforeSkipsSpacesAndStopsAtNonIdentifiers(string text, string? expected)
    {
        Assert.Equal(expected, CompletionInteraction.WordBefore(text, text.Length));
    }

    [Fact]
    public void WordBeforeToleratesOutOfRangeOffsets()
    {
        Assert.Null(CompletionInteraction.WordBefore("abc", -1));
        Assert.Null(CompletionInteraction.WordBefore("abc", 99));
    }

    // ── Snippets are accepted by Tab and Enter only ──────────────────────────────────────────────

    [Theory]
    [InlineData('(')]
    [InlineData('[')]
    [InlineData('{')]
    [InlineData(';')]
    [InlineData(',')]
    [InlineData(')')]
    public void ACommitCharacterNeverAcceptsASnippet(char c)
    {
        // Snippets sort first and win the selection, so the highlighted item on `for` is the loop
        // snippet. Accepting it on a bracket would expand a whole multi-line construct around the
        // parenthesis someone was typing by hand — a far worse outcome than for a symbol, where the
        // commit merely swaps one identifier. Tab and Enter stay available; the caller closes the
        // list on these instead, leaving what was typed intact.
        Assert.True(CompletionInteraction.Commits(c), "precondition: this commits a symbol");
        Assert.False(CompletionInteraction.Commits(c, selectedItemIsSnippet: true));
    }

    [Theory]
    [InlineData('(')]
    [InlineData(';')]
    [InlineData(',')]
    public void ASymbolIsStillCommittedByACommitCharacter(char c)
    {
        // The snippet rule must not cost symbols their commit characters.
        Assert.True(CompletionInteraction.Commits(c, selectedItemIsSnippet: false));
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('.')]
    [InlineData('+')]
    public void ANonCommitCharacterIsUnaffectedByTheSnippetFlag(char c)
    {
        Assert.False(CompletionInteraction.Commits(c, selectedItemIsSnippet: true));
        Assert.False(CompletionInteraction.Commits(c, selectedItemIsSnippet: false));
    }

    [Fact]
    public void SnippetsAreAddedToTheListBeforeSymbols()
    {
        // AvalonEdit renders completion items in insertion order and never consults Priority for it,
        // so display position is decided here and nowhere else. Appending snippets put `for` below
        // every FormatException-shaped type — several scrolls down, where a snippet cannot be
        // discovered, let alone accepted with one key. Needs a real editor to observe, hence a scan.
        var code = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

        var snippetLoop = code.IndexOf("foreach (var snippet in snippets)", System.StringComparison.Ordinal);
        var symbolLoop = code.IndexOf("foreach (var item in sortedCompletions)", System.StringComparison.Ordinal);

        Assert.True(snippetLoop > 0 && symbolLoop > 0, "both fill loops must exist");
        Assert.True(snippetLoop < symbolLoop,
            "snippets must be added before symbols, or they render at the bottom of the list");
    }

    [Fact]
    public void EnterNeverExpandsASnippet()
    {
        // The destructive pair: snippets win the selection on an exact match, and Enter is how a
        // line is ended. Triggers that are also ordinary things to type — null, else, throw, using,
        // do — would rewrite the line into a multi-line block. `null` expands to a four-line
        // ArgumentNullException guard, so `x = null` + Enter is the worst case.
        var code = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(),
            "Editor", "CompletionData.cs"));

        var complete = code.IndexOf("public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)",
            code.IndexOf("class SnippetCompletionData", System.StringComparison.Ordinal),
            System.StringComparison.Ordinal);

        Assert.True(complete > 0, "SnippetCompletionData.Complete must exist");

        var body = code[complete..];
        Assert.Contains("Key.Enter", body[..1600]);
        Assert.Contains("enterKey.Handled = false;", body[..1600]);
    }

    [Fact]
    public void KeywordsDuplicatingASnippetTriggerAreDropped()
    {
        // Only keywords a snippet already spells. The rest are why keywords are injected at all:
        // without them `for (int` fuzzy-matched type names and ranked IntersectionResult first.
        var code = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "MainWindow.xaml.cs"));

        Assert.Contains("var triggers = new HashSet<string>(snippets.Select(s => s.Text), StringComparer.Ordinal);", code);
        Assert.Contains("Editor.CompletionData { Kind: Editor.CompletionKind.Keyword } kw", code);
    }

    [Fact]
    public void EverySnippetTriggerIsStillOfferedAfterTheDedup()
    {
        // The dedup drops keywords, never snippets — the row the user is meant to land on has to
        // survive it. Cheap cross-check that the two sets do overlap, so the rule is not inert.
        var triggers = CodeSnippets.GetAll().Select(t => t.Trigger).ToHashSet(System.StringComparer.Ordinal);

        Assert.Contains("for", triggers);
        Assert.Contains("foreach", triggers);
        Assert.Contains("null", triggers);
    }

    [Fact]
    public void SnippetsOutrankEverySymbolKindOnATie()
    {
        // AvalonEdit breaks a match-quality tie with the HIGHER priority, so the old 0.5 — the
        // lowest value in the table — meant `for` selected the keyword over the loop snippet.
        var kinds = new[]
        {
            CompletionKind.Keyword, CompletionKind.Type, CompletionKind.Property,
            CompletionKind.Method, CompletionKind.Delegate,
        };

        foreach (var kind in kinds)
        {
            var symbol = new CompletionData("for", "for keyword", kind);
            Assert.True(CompletionData.SnippetPriority > symbol.Priority,
                $"a snippet must outrank {kind} when match quality ties");
        }
    }
}
