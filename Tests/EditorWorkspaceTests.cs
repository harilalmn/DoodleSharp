using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Execution;
using DoodleSharp.Project;
using Microsoft.CodeAnalysis;

namespace DoodleSharp.Tests;

/// <summary>
/// Tests for the compilation the editor features run against: that it tracks the project's live file
/// set, and that its character offsets match what the user actually typed.
/// </summary>
public class EditorWorkspaceTests : IDisposable
{
    private readonly string _dir;

    public EditorWorkspaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ds_ws_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Workspace_TracksAddedAndRemovedFiles()
    {
        var workspace = new CachedCompilationWorkspace(new ModuleCompiler().GetReferences());

        workspace.UpdateFile("A.cs", "class A { }");
        Assert.Contains("A.cs", workspace.GetFileIds());

        // A file created mid-session has to become visible immediately, not at the next project load.
        workspace.UpdateFile("B.cs", "class B { }");
        Assert.Contains("B.cs", workspace.GetFileIds());
        Assert.NotNull(workspace.GetCompilation().GetTypeByMetadataName("B"));

        // And a deleted one has to stop resolving, rather than lingering forever.
        workspace.RemoveFile("B.cs");
        Assert.DoesNotContain("B.cs", workspace.GetFileIds());
        Assert.Null(workspace.GetCompilation().GetTypeByMetadataName("B"));
    }

    [Fact]
    public void Workspace_ReplacesContentOnUpdate()
    {
        var workspace = new CachedCompilationWorkspace(new ModuleCompiler().GetReferences());

        workspace.UpdateFile("A.cs", "class Before { }");
        workspace.UpdateFile("A.cs", "class After { }");

        Assert.Single(workspace.GetFileIds());
        Assert.Null(workspace.GetCompilation().GetTypeByMetadataName("Before"));
        Assert.NotNull(workspace.GetCompilation().GetTypeByMetadataName("After"));
    }

    /// <summary>
    /// The invariant every offset-based editor feature depends on. The compiler applies source
    /// rewriters (shape/animation naming, stack guards) that insert text — if they ran for the
    /// editor, go-to-definition, find-references and rename would all resolve the wrong token in any
    /// file containing a named shape declaration.
    /// </summary>
    [Fact]
    public async Task EditorCompilation_PreservesSourceOffsets()
    {
        const string source = """
            using C2VGeometry;
            namespace TestProj
            {
                public class Viz
                {
                    public static void Main()
                    {
                        var circle = new VCircle(0, 0, 10);
                        var marker = circle;
                    }
                }
            }
            """;

        var project = VizCodeProject.CreateNew(_dir, "TestProj");
        var entry = project.EntryPointFile!;
        entry.Content = source;
        File.WriteAllText(entry.FilePath, source);

        var compiler = new ModuleCompiler();
        var (editorCompilation, _) = await compiler.CreateCompilationAsync(project);

        var tree = editorCompilation.SyntaxTrees.Single(t =>
            Path.GetFileName(t.FilePath).Equals("StartViz.cs", StringComparison.OrdinalIgnoreCase));

        // Character-for-character identical to the file on disk.
        Assert.Equal(source, tree.GetText().ToString());

        // So an editor offset resolves to the token the user is actually pointing at.
        var offset = source.IndexOf("marker", StringComparison.Ordinal);
        Assert.Equal("marker", tree.GetRoot().FindToken(offset).Text);
    }

    /// <summary>
    /// The other half of the contract: the execute path must still rewrite, or HideUnnamedShapes
    /// cannot tell a named shape from an anonymous one and the stack guard disappears.
    /// </summary>
    [Fact]
    public async Task ExecutionCompilation_StillAppliesTheRewriters()
    {
        const string source = """
            using C2VGeometry;
            namespace TestProj
            {
                public class Viz
                {
                    public static void Main()
                    {
                        var circle = new VCircle(0, 0, 10);
                    }
                }
            }
            """;

        var project = VizCodeProject.CreateNew(_dir, "TestProj");
        var entry = project.EntryPointFile!;
        entry.Content = source;
        File.WriteAllText(entry.FilePath, source);

        var compiler = new ModuleCompiler();
        var (runCompilation, _) = await compiler.CreateCompilationAsync(project, forExecution: true);

        var text = runCompilation.SyntaxTrees
            .Single(t => Path.GetFileName(t.FilePath).Equals("StartViz.cs", StringComparison.OrdinalIgnoreCase))
            .GetText().ToString();

        // The rewriter builds the initializer with SyntaxFactory, which emits no whitespace trivia,
        // so match on the tokens rather than on a formatted rendering of them.
        Assert.Contains("Name", text);
        Assert.Contains("\"circle\"", text);
        Assert.Contains("EnsureSufficientExecutionStack", text);
    }
}
