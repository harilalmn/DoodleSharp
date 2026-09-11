using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using C2VGeometry;
using DoodleSharp.Execution;
using DoodleSharp.Project;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Note 144: a declaration that spells out a shape type is named after its variable whatever the
/// initializer is, so <c>VPoint vp1 = p1.AsVPoint();</c> survives <c>HideUnnamedShapes</c>. It used
/// to be named only when the initializer was a <c>new</c>, and a method result was hidden however
/// carefully it had been declared.
/// </summary>
[Collection("CanvasState")]
public class ShapeNamingRewriteTests : IDisposable
{
    private readonly string _dir;

    public ShapeNamingRewriteTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ds_name_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private const string Source = """
        using C2VGeometry;
        namespace TestProj
        {
            public class Viz
            {
                static VPoint field = new VXYZ(1, 1).AsVPoint();

                public static void Main() { }

                public static object[] Build()
                {
                    VXYZ p1 = new VXYZ(10, 12);
                    VPoint vp1 = p1.AsVPoint();
                    VPoint? maybe = p1.AsVPoint();
                    VPoint none = null;
                    VPoint pick = true ? vp1 : null;
                    var viaVar = p1.AsVPoint();
                    VPoint kept = AlreadyNamed();
                    VPoint multi =
                        p1
                            .AsVPoint();
                    return new object[] { vp1, maybe, none, pick, viaVar, kept, field, multi };
                }

                static VPoint AlreadyNamed() => new VPoint(0, 0) { Name = "original" };
            }
        }
        """;

    private async Task<Compilation> RunCompilationAsync()
    {
        var project = VizCodeProject.CreateNew(_dir, "TestProj");
        var entry = project.EntryPointFile!;
        entry.Content = Source;
        File.WriteAllText(entry.FilePath, Source);

        var (compilation, _) = await new ModuleCompiler().CreateCompilationAsync(project, forExecution: true);
        return compilation;
    }

    [Fact]
    public async Task MethodResultsOnShapeTypedDeclarationsAreNamed()
    {
        var compilation = await RunCompilationAsync();

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join("\n",
            emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var assembly = System.Reflection.Assembly.Load(ms.ToArray());
        object[] shapes;
        using (Shape.SuspendAutoRegistration())
            shapes = (object[])assembly.GetType("TestProj.Viz")!.GetMethod("Build")!.Invoke(null, null)!;

        string NameOf(int i) => ((Shape)shapes[i]).Name;

        Assert.Equal("vp1", NameOf(0));
        Assert.Equal("maybe", NameOf(1));                 // nullable shape type
        Assert.Null(shapes[2]);                           // `= null` still compiles and stays null
        Assert.Equal("vp1", NameOf(3));                   // an already-named shape keeps its name
        Assert.Equal("", NameOf(4));                      // var: out of reach of a syntax-only rewrite
        Assert.Equal("original", NameOf(5));              // a method's own name is not overwritten
        Assert.Equal("field", NameOf(6));                 // field initialisers too
        Assert.Equal("multi", NameOf(7));
    }

    [Fact]
    public async Task TheWrapKeepsLineNumbers()
    {
        // Stack traces report user lines; the wrap may shift columns, never lines. Each wrapped
        // declaration is compared line-for-line rather than counting the whole file: the stack
        // guard, not this rewrite, is what the file's total line count answers to.
        var compilation = await RunCompilationAsync();

        var rewritten = compilation.SyntaxTrees.Single(t =>
                Path.GetFileName(t.FilePath).Equals("StartViz.cs", StringComparison.OrdinalIgnoreCase))
            .GetText().ToString();

        static int LineWhere(string text, Func<string, bool> match) =>
            Array.FindIndex(text.Split('\n'), l => match(l));

        foreach (var name in new[] { "field", "vp1", "maybe", "none", "pick", "kept" })
        {
            int expected = LineWhere(Source, l => l.Contains($" {name} ="));
            Assert.True(expected >= 0);
            Assert.Equal(expected, LineWhere(rewritten, l => l.Contains($"\"{name}\"")));
        }

        // A multi-line initializer closes on its own last line.
        Assert.Equal(
            LineWhere(Source, l => l.TrimStart().StartsWith(".AsVPoint();")),
            LineWhere(rewritten, l => l.Contains("\"multi\"")));
    }

    [Fact]
    public async Task TheHelperTreeStaysOutOfTheEditorCompilation()
    {
        var project = VizCodeProject.CreateNew(_dir, "TestProj");
        var (editor, _) = await new ModuleCompiler().CreateCompilationAsync(project);

        Assert.DoesNotContain(editor.SyntaxTrees, t => t.FilePath == ShapeNamingHelper.FilePath);
    }
}
