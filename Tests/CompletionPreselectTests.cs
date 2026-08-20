using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Execution;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// The completion list opens on the row the caret is about.
///
/// <para>
/// The order stays alphabetical (note 115, <see cref="CompletionOrderingTests"/>) — this is about
/// the selection only. After <c>VXYZ p = new </c> the alphabet opens the list on
/// <c>AccessViolationException</c> with <c>VXYZ</c> hundreds of rows below the fold, so the one key
/// that costs nothing (Tab) inserted the wrong type.
/// </para>
/// </summary>
public class CompletionPreselectTests
{
    private sealed class Item : ICompletionData
    {
        public Item(string text) { Text = text; }
        public string Text { get; }
        public object Content => Text;
        public object Description => Text;
        public System.Windows.Media.ImageSource? Image => null;
        public double Priority => 0;
        public void Complete(ICSharpCode.AvalonEdit.Editing.TextArea textArea,
            ICSharpCode.AvalonEdit.Document.ISegment completionSegment, EventArgs insertionRequestEventArgs) { }
    }

    private static IList<ICompletionData> List(params string[] names) =>
        names.Select(n => (ICompletionData)new Item(n)).ToList();

    [Fact]
    public void ExpectedTypeIsSelectedWhereverTheAlphabetPutIt()
    {
        var items = List("AccessViolationException", "AggregateException", "VXLine", "VXYZ");

        Assert.Equal(3, CompletionPreselect.IndexOf(items, "VXYZ"));
    }

    [Fact]
    public void FirstRowWinsWithoutAnExpectedType()
    {
        var items = List("AccessViolationException", "VXYZ");

        Assert.Equal(0, CompletionPreselect.IndexOf(items, null));
        Assert.Equal(0, CompletionPreselect.IndexOf(items, ""));
    }

    [Fact]
    public void AnExpectedTypeThatIsNotOfferedLeavesTheFirstRowSelected()
    {
        var items = List("AccessViolationException", "VXLine");

        Assert.Equal(0, CompletionPreselect.IndexOf(items, "VXYZ"));
    }

    [Fact]
    public void EmptyListHasNoRowToSelect()
    {
        Assert.Equal(-1, CompletionPreselect.IndexOf(List(), "VXYZ"));
    }

    [Fact]
    public void MatchIsExactAndOrdinal()
    {
        // A prefix or case-insensitive match would hand Tab a different type than the declaration
        // asked for, which is worse than leaving the first row selected.
        var items = List("vxyz", "VXYZLike");

        Assert.Equal(0, CompletionPreselect.IndexOf(items, "VXYZ"));
    }

    [Fact]
    public void ASnippetAtTheTopKeepsTheSelection()
    {
        // Note 101: snippets are poured in ahead of the symbols so that item 0 is what Tab expands.
        var items = new List<ICompletionData>
        {
            new SnippetCompletionData("for", "for loop", CodeSnippets.GetSnippet("for")!),
            new Item("VXYZ"),
        };

        Assert.Equal(0, CompletionPreselect.IndexOf(items, "VXYZ"));
    }

    [Fact]
    public async Task NewExpressionOpensOnTheDeclaredType()
    {
        // End to end over the real symbol list: what the service returns, ordered the way the hosts
        // order it, lands its selection on VXYZ rather than on the first row of the alphabet.
        const string code = """
            using System;
            using C2VGeometry;

            namespace T
            {
                public class Viz
                {
                    public static void Main()
                    {
                        VXYZ p1 = new $
                    }
                }
            }
            """;

        var position = code.IndexOf('$');
        var service = new RoslynCompletionService(new ModuleCompiler().GetReferences());
        var (completions, isAfterNew, _, expectedType) =
            await service.GetCompletionsAsync(code.Remove(position, 1), position);

        Assert.True(isAfterNew);

        var alphabetical = completions
            .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Text, StringComparer.Ordinal)
            .ToList();

        Assert.True(alphabetical.Count > 1, "the list must actually hold competitors");
        Assert.NotEqual("VXYZ", alphabetical[0].Text);
        Assert.Equal("VXYZ", alphabetical[CompletionPreselect.IndexOf(alphabetical, expectedType)].Text);
    }

    [Theory]
    [InlineData("MainWindow.xaml.cs")]
    [InlineData("Editor/SharedEditorController.cs")]
    public void BothEditorsPreselect(string file)
    {
        // Ordering and selection are only observable with a real editor, and there are two parallel
        // implementations of it (note 43), so this is a source scan over both.
        var source = File.ReadAllText(Path.Combine(ArrowheadConsistencyTests.RepoRoot(), file));

        Assert.Contains("CompletionPreselect.IndexOf(", source);
        // The expected type has to reach the window: discarding the service's fourth value with `_`
        // is how this was inert before.
        Assert.Contains("prefix, expectedType) = await service.GetCompletionsAsync", source);
        // Selecting a row does not scroll to it.
        Assert.Contains("ScrollIntoView(preselectedItem)", source);
    }
}
