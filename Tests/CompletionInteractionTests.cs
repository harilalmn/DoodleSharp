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
}
