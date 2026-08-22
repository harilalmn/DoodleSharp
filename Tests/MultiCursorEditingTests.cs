using System;
using System.IO;
using DoodleSharp.Editor;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Multi-cursor editing (note 140). The document edits themselves need a live TextArea, so what is
/// pinned here is the pure decision behind multi-cursor paste plus source scans over the two key
/// handlers — <c>MainWindow</c> and <see cref="SharedEditorController"/> are parallel
/// implementations (note 43), and a fix applied to only one of them is the recurring failure here.
/// </summary>
public class MultiCursorEditingTests
{
    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void FourCopiedWordsGoToFourCursors_OneEach()
    {
        // The bug: copying four words under four cursors joined them with newlines, and pasting
        // that joined text at every cursor put all four words in all four places.
        var parts = MultiSelectionRenderer.SplitForCursors($"one{Environment.NewLine}two{Environment.NewLine}three{Environment.NewLine}four", 4);

        Assert.NotNull(parts);
        Assert.Equal(new[] { "one", "two", "three", "four" }, parts!);
    }

    [Fact]
    public void ATrailingNewlineIsNotACursorsWorthOfText()
    {
        // Copying whole lines leaves the trailing separator; the empty tail it produces must not
        // count as a fragment, or a four-line copy would refuse to spread over four cursors.
        var parts = MultiSelectionRenderer.SplitForCursors("one\r\ntwo\r\nthree\r\nfour\r\n", 4);

        Assert.NotNull(parts);
        Assert.Equal(4, parts!.Count);
        Assert.Equal("four", parts[3]);
    }

    [Fact]
    public void CarriageReturnsAndMixedSeparatorsSplitTheSame()
    {
        Assert.Equal(new[] { "a", "b", "c" }, MultiSelectionRenderer.SplitForCursors("a\rb\nc", 3)!);
    }

    [Theory]
    [InlineData("one\ntwo", 4)]        // fewer lines than cursors
    [InlineData("a\nb\nc\nd\ne", 4)]   // more lines than cursors
    [InlineData("just one line", 3)]   // nothing to spread
    public void TextThatDoesNotDivideEvenlyGoesToEveryCursorWhole(string clipboard, int cursors)
    {
        // Null is the signal to fall back to "paste the whole thing at each cursor", which is the
        // right behaviour when the clipboard did not come from a matching multi-cursor copy.
        Assert.Null(MultiSelectionRenderer.SplitForCursors(clipboard, cursors));
    }

    [Fact]
    public void ASingleCursorNeverSpreads()
    {
        Assert.Null(MultiSelectionRenderer.SplitForCursors("one\ntwo", 1));
        Assert.Null(MultiSelectionRenderer.SplitForCursors("", 4));
    }

    [Fact]
    public void BothKeyHandlersRouteTabThroughTheRenderer()
    {
        // Left to AvalonEdit, Tab sees only the main selection: the first cursor was indented while
        // the rest were outdented by the same keystroke, and they drifted apart.
        foreach (var file in new[] { "MainWindow.xaml.cs", Path.Combine("Editor", "SharedEditorController.cs") })
        {
            var source = Read(file);
            Assert.Contains("IndentAtAllCursors", source);
            Assert.Contains("OutdentAtAllCursors", source);
        }
    }

    [Fact]
    public void DuplicateLineRestoresTheCaretExplicitly()
    {
        // An insert at the caret's own offset carries the caret with it, so `Caret.Line += n` moved
        // a caret sitting at the end of the line one line too far. Both copies of the operation
        // have to remember the position and put it back.
        foreach (var file in new[] { "MainWindow.xaml.cs", Path.Combine("Editor", "SharedEditorController.cs") })
        {
            var source = Read(file);
            Assert.DoesNotContain("textArea.Caret.Line = textArea.Caret.Line + lineCount;", source);
            Assert.Contains("new TextViewPosition(caretLine + lineCount, caretColumn)", source);
        }
    }
}
