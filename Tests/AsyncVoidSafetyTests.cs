using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// An <c>async void</c> method that lets an exception escape takes the process down.
///
/// <para>
/// This is not the usual "unhandled exceptions are bad" advice. An exception from an
/// <c>async Task</c> faults the task and waits for someone to observe it; an exception from an
/// <c>async void</c> is captured by the synchronisation context and <b>re-thrown on the dispatcher
/// thread</b>, where WPF has nowhere to put it. The editor's Go To Definition, Find All References,
/// Rename, quick actions and the completion popup are all <c>async void</c> handlers bound to
/// keystrokes, and all of them await Roslyn over a file the user is halfway through typing. Every
/// one of them was a single throw away from closing the app over an editor convenience — losing
/// whatever was unsaved with it.
/// </para>
///
/// <para>
/// Parsed with Roslyn rather than scanned as text: brace counting cannot survive the interpolated
/// verbatim strings these files are full of, and a <c>try</c> matched by <c>IndexOf</c> may belong
/// to a nested lambda rather than the method.
/// </para>
/// </summary>
public class AsyncVoidSafetyTests
{
    /// <summary>
    /// The two editor hosts. Note 43: <c>MainWindow</c> has its own inlined editor implementation
    /// alongside <see cref="DoodleSharp.Editor.SharedEditorController"/>, so a rule that holds for
    /// one has to be checked against both or it only half exists.
    /// </summary>
    public static IEnumerable<object[]> EditorHosts => new[]
    {
        new object[] { Path.Combine("MainWindow.xaml.cs") },
        new object[] { Path.Combine("Editor", "SharedEditorController.cs") },
    };

    [Theory]
    [MemberData(nameof(EditorHosts))]
    public void NoAsyncVoidMethodAwaitsOutsideATry(string relativePath)
    {
        var path = Path.Combine(ArrowheadConsistencyTests.RepoRoot(), relativePath);
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();

        var unguarded = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(IsAsyncVoid)
            .Where(m => m.Body != null)
            .Where(HasAnAwaitOfItsOwn)
            .Where(m => !IsFullyGuarded(m))
            .Select(m => m.Identifier.ValueText)
            .ToArray();

        Assert.True(unguarded.Length == 0,
            $"these async void methods in {relativePath} can crash the app on a throw: " +
            string.Join(", ", unguarded));
    }

    private static bool IsAsyncVoid(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.AsyncKeyword) &&
        method.ReturnType is PredefinedTypeSyntax p && p.Keyword.IsKind(SyntaxKind.VoidKeyword);

    /// <summary>
    /// True when the method itself awaits — not merely when some nested lambda inside it does,
    /// since a lambda's awaits belong to the lambda's own return type.
    /// </summary>
    private static bool HasAnAwaitOfItsOwn(MethodDeclarationSyntax method) =>
        method.Body!.DescendantNodes(descendIntoChildren: n =>
                n is not AnonymousFunctionExpressionSyntax && n is not LocalFunctionStatementSyntax)
            .OfType<AwaitExpressionSyntax>()
            .Any();

    /// <summary>
    /// True when the body is a single <c>try</c> covering everything. A guard that starts partway
    /// through leaves whatever precedes it unprotected, which is exactly the shape the bug had.
    /// </summary>
    private static bool IsFullyGuarded(MethodDeclarationSyntax method)
    {
        var statements = method.Body!.Statements;
        return statements.Count == 1
            && statements[0] is TryStatementSyntax tryStatement
            && tryStatement.Catches.Any(c => c.Declaration == null ||
                                             c.Declaration.Type.ToString() is "Exception" or "System.Exception");
    }
}
