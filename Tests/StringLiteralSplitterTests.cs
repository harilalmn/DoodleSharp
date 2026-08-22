using DoodleSharp.Editor;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Enter inside a string literal (note 139). A raw newline between the quotes of <c>"…"</c> or
/// <c>$"…"</c> is not legal C#, so pressing Enter there used to leave the file not compiling. The
/// literal has to be closed and continued instead. These fix the decision — which literals split,
/// which are left alone, and exactly what gets inserted — because the key handling around it needs
/// a real window and cannot be exercised here.
/// </summary>
public class StringLiteralSplitterTests
{
    /// <summary>Splits at the <c>~</c> marker and returns the inserted text, or null.</summary>
    /// <remarks>The marker is <c>~</c> and not <c>$</c>: these snippets are full of interpolated
    /// strings, where a <c>$</c> marker matches the interpolation sigil instead of the caret.</remarks>
    private static string? Split(string markedCode, string newLine = "\n")
    {
        var position = markedCode.IndexOf('~');
        Assert.True(position >= 0, "Test source must contain a '~' caret marker");
        var code = markedCode.Remove(position, 1);

        return StringLiteralSplitter.Compute(code, position, newLine)?.InsertedText;
    }

    [Fact]
    public void InterpolatedString_ClosesTheLiteralAndReopensItOnTheNextLine()
    {
        // $"hello |world"  ->  $"hello " +
        //                          $"world"
        var inserted = Split("""
            class C { void M() { var s = $"hello ~world"; } }
            """);

        Assert.Equal("\" +\n    $\"", inserted);
    }

    [Fact]
    public void PlainString_ReopensWithoutTheDollarSign()
    {
        // A non-interpolated literal must not gain interpolation it never had.
        var inserted = Split("""
            class C { void M() { var s = "hello ~world"; } }
            """);

        Assert.Equal("\" +\n    \"", inserted);
    }

    [Fact]
    public void ContinuationLineKeepsItsIndent()
    {
        // The second and later fragments already sit at the continuation indent. Adding another
        // level to each one walks the text off the right of the screen.
        var code = "class C { void M() { var s = \"a\" +\n        \"b~c\"; } }";
        var position = code.IndexOf('~');

        var inserted = StringLiteralSplitter.Compute(code.Remove(position, 1), position, "\n")?.InsertedText;

        Assert.Equal("\" +\n        \"", inserted);
    }

    [Fact]
    public void VerbatimString_IsLeftAlone()
    {
        // @"…" already accepts a newline, so the plain Enter is the correct behaviour.
        Assert.Null(Split("""
            class C { void M() { var s = @"C:\temp~\out"; } }
            """));
    }

    [Fact]
    public void RawString_IsLeftAlone()
    {
        var code = "class C { void M() { var s = \"\"\"he~llo\"\"\"; } }";
        var position = code.IndexOf('~');

        Assert.Null(StringLiteralSplitter.Compute(code.Remove(position, 1), position, "\n"));
    }

    [Fact]
    public void InsideAnInterpolationHole_IsLeftAlone()
    {
        // `$"{ count $}"` is code, not string content, and a newline is legal there.
        Assert.Null(Split("""
            class C { void M() { int count = 0; var s = $"n = {count ~}"; } }
            """));
    }

    [Fact]
    public void PlainCode_IsLeftAlone()
    {
        Assert.Null(Split("""
            class C { void M() { var s = "done"; ~ } }
            """));
    }

    [Fact]
    public void AfterAClosedLiteralOnTheSameLine_IsLeftAlone()
    {
        // The naive "is there a quote to my left" test gets this one wrong.
        Assert.Null(Split("""
            class C { void M() { var a = "x"; var b = 1 ~; } }
            """));
    }

    [Fact]
    public void AQuoteInsideALineComment_DoesNotOpenALiteral()
    {
        Assert.Null(Split("""
            class C { void M() { // it's a "quote
                var n = 1 ~;
            } }
            """));
    }

    [Fact]
    public void AQuoteInsideABlockComment_DoesNotOpenALiteral()
    {
        Assert.Null(Split("""
            class C { void M() { /* a " here */ var n = 1 ~; } }
            """));
    }

    [Fact]
    public void AQuoteCharLiteral_DoesNotOpenALiteral()
    {
        Assert.Null(Split("""
            class C { void M() { char q = '"'; var n = 1 ~; } }
            """));
    }

    [Fact]
    public void InsideAnEscapeSequence_IsLeftAlone()
    {
        // Splitting between the backslash and what it escapes produces `\"` — an escaped quote,
        // not a closed literal — which would corrupt the string rather than continue it.
        Assert.Null(Split("""
            class C { void M() { var s = "line\~n"; } }
            """));
    }

    [Fact]
    public void AnEscapedQuoteDoesNotEndTheLiteral()
    {
        // `"say \"hi\" |now"` is still inside the literal at the caret.
        var inserted = Split("""
            class C { void M() { var s = "say \"hi\" ~now"; } }
            """);

        Assert.Equal("\" +\n    \"", inserted);
    }

    [Fact]
    public void CaretInsideTheOpener_IsLeftAlone()
    {
        // Between the $ and its quote there is no literal open yet, so there is nothing to close.
        var code = "class C { void M() { var s = $\"hello\"; } }";
        var caret = code.IndexOf("$\"") + 1;

        Assert.Null(StringLiteralSplitter.Compute(code, caret, "\n"));
    }

    [Fact]
    public void TheCaretLandsAfterTheReopenedQuote()
    {
        var code = "class C { void M() { var s = $\"hello world\"; } }";
        var caret = code.IndexOf("world");

        var split = StringLiteralSplitter.Compute(code, caret, "\n");

        Assert.NotNull(split);
        Assert.Equal(caret + split!.Value.InsertedText.Length, split.Value.CaretOffset);

        // And the document that results is what the user asked for.
        var result = code.Insert(caret, split.Value.InsertedText);
        Assert.Contains("$\"hello \" +", result);
        Assert.Contains("$\"world\"", result);
    }
}
