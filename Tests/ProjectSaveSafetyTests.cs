using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Writing the <c>.vizproj</c> must never be able to close the app.
///
/// <para>
/// This is the synchronous half of the rule <see cref="AsyncVoidSafetyTests"/> enforces for
/// <c>async void</c> (note 134). A plain routed-event handler is no safer: its exception walks the
/// WPF event route, reaches the dispatcher, and ends the process. It happened — unticking Auto-Run
/// called <c>SaveProjectFile()</c>, OneDrive had the file open for the instant the atomic rename
/// needed it, and the resulting <see cref="IOException"/> had nowhere to go. The user lost the
/// session over a checkbox.
/// </para>
///
/// <para>
/// <c>DurableFile</c> now retries that rename, which makes the failure rare rather than impossible —
/// a full disk, a revoked network share and a read-only file all still fail on the first try. The
/// rule is therefore about the call site, not the odds: the project file records preferences that
/// are already applied in memory, so a failed write is a status-bar message, not an exit. Saving the
/// user's <em>source</em> is a different matter and keeps its loud failure.
/// </para>
/// </summary>
public class ProjectSaveSafetyTests
{
    [Theory]
    [InlineData("MainWindow.xaml.cs")]
    [InlineData("AddReferenceWindow.xaml.cs")]
    public void EveryProjectFileSaveInAWindowIsGuarded(string relativePath)
    {
        var path = Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relativePath);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

        var unguarded = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(IsSaveProjectFile)
            .Where(call => !IsInsideATryBlock(call))
            .Select(call => EnclosingMethod(call) ?? "<unknown>")
            .ToArray();

        Assert.True(unguarded.Length == 0,
            $"SaveProjectFile() can crash the app from these methods in {relativePath}: " +
            string.Join(", ", unguarded) +
            " — route it through MainWindow.TrySaveProjectFile, or catch it where it is called.");
    }

    private static bool IsSaveProjectFile(InvocationExpressionSyntax call) =>
        call.Expression is MemberAccessExpressionSyntax m && m.Name.Identifier.ValueText == "SaveProjectFile";

    /// <summary>
    /// Inside the <c>try</c> itself — a call sitting in the <c>catch</c> or <c>finally</c> of some
    /// outer statement is not covered by it.
    /// </summary>
    private static bool IsInsideATryBlock(SyntaxNode node) =>
        node.Ancestors().OfType<TryStatementSyntax>()
            .Any(t => t.Block.Span.Contains(node.Span));

    private static string? EnclosingMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
}
