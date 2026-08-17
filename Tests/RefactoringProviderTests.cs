using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// Tests for the quick-action analyser, focused on the question the old implementation got wrong:
/// <b>where</b> does generated code go.
///
/// <para>
/// Each test builds a throwaway project on disk (the provider compiles from
/// <see cref="VizCodeProject"/>, which discovers <c>.cs</c> files by walking the project directory)
/// and asks for the actions at a caret position.
/// </para>
/// </summary>
public class RefactoringProviderTests : IDisposable
{
    private readonly string _dir;

    public RefactoringProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "C2V_refactor_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private VizCodeProject NewProject(params (string Name, string Content)[] files)
    {
        var project = VizCodeProject.CreateNew(_dir, "TestProj");
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);

            var existing = project.Files.FirstOrDefault(f =>
                string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Content = content;
            else project.Files.Add(new VizCodeFile { FilePath = path, Content = content, HasUnsavedChanges = false });
        }
        return project;
    }

    private static RefactoringProvider NewProvider() =>
        new RefactoringProvider(new DoodleSharp.Execution.ModuleCompiler());

    private const string VectorManagerSource = """
        namespace TestProj
        {
            public static class VectorManager
            {
                public static void Existing() { }
            }
        }
        """;

    private static string StartVizCalling(string call) => $$"""
        namespace TestProj
        {
            public class Viz
            {
                public static void Main()
                {
                    {{call}}
                }
            }
        }
        """;

    [Fact]
    public async Task GenerateMethod_TargetsTheReceiversFile_NotTheOpenOne()
    {
        var caller = StartVizCalling("VectorManager.DrawVector(1.0);");
        var project = NewProject(
            ("StartViz.cs", caller),
            ("VectorManager.cs", VectorManagerSource));

        var offset = caller.IndexOf("DrawVector", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var generate = actions.SingleOrDefault(a => a.ActionId == "GenerateMethod");
        Assert.NotNull(generate);

        // The whole point: the insertion site is VectorManager.cs, not the file being edited.
        Assert.Equal("VectorManager", generate!.Data["TargetType"]);
        Assert.Equal("VectorManager.cs", Path.GetFileName(generate.Data["TargetFilePath"]));
        Assert.Contains("in VectorManager", generate.Title);

        // Called through a type name, so it must be static, and reachable from the other file.
        Assert.Equal("True", generate.Data["IsStatic"]);
        Assert.Equal("public", generate.Data["TargetAccessibility"]);

        // And the offset lands at the start of the line holding that type's closing brace.
        var target = File.ReadAllText(Path.Combine(_dir, "VectorManager.cs"));
        var insertOffset = int.Parse(generate.Data["TargetInsertOffset"]);
        Assert.InRange(insertOffset, 0, target.Length);
        Assert.Equal("    }", target[insertOffset..].Split('\n')[0].TrimEnd('\r'));
    }

    [Fact]
    public async Task GenerateMethod_KeepsTheClosingBraceIndented()
    {
        // Regression: inserting at the closing-brace token slid the stub between the brace and its
        // indentation, leaving a whitespace-only line and pulling the brace out to column 0.
        var caller = StartVizCalling("VectorManager.DrawVector(1.0);");
        var project = NewProject(
            ("StartViz.cs", caller),
            ("VectorManager.cs", VectorManagerSource));

        var offset = caller.IndexOf("DrawVector", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var stub = MethodStubBuilder.Build(actions.Single(a => a.ActionId == "GenerateMethod").Data, "\n");
        var patched = File.ReadAllText(Path.Combine(_dir, "VectorManager.cs"))
            .Replace("\r\n", "\n")
            .Insert(stub.Offset, stub.Text);

        var lines = patched.Split('\n');

        // The class brace keeps its 4-space indent and the namespace brace stays at column 0.
        Assert.Contains("    }", lines);
        Assert.Contains("}", lines);
        Assert.DoesNotContain(lines, l => l.Length > 0 && l.Trim().Length == 0 && l != "");

        // Body indented one level past the signature.
        Assert.Contains("        public static void DrawVector(double arg0)", lines);
        Assert.Contains("            throw new NotImplementedException();", lines);
    }

    [Fact]
    public async Task GenerateMethod_ForReceiverlessCall_TargetsTheEnclosingType()
    {
        var caller = StartVizCalling("DrawLocal(2.0);");
        var project = NewProject(("StartViz.cs", caller));

        var offset = caller.IndexOf("DrawLocal", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var generate = actions.SingleOrDefault(a => a.ActionId == "GenerateMethod");
        Assert.NotNull(generate);
        Assert.Equal("Viz", generate!.Data["TargetType"]);
        Assert.Equal("private", generate.Data["TargetAccessibility"]);
        // Called from a static Main, so the new member has to be static too.
        Assert.Equal("True", generate.Data["IsStatic"]);
        Assert.DoesNotContain(" in ", generate.Title);
    }

    [Fact]
    public async Task GenerateMethod_IsNotOfferedForTypesWeCannotEdit()
    {
        // VXYZ lives in C2VGeometry.dll. Generating "into" it is impossible, and generating into the
        // current class instead would silently produce something the call site cannot reach.
        var caller = StartVizCalling("C2VGeometry.VXYZ.NoSuchMethod();");
        var project = NewProject(("StartViz.cs", caller));

        var offset = caller.IndexOf("NoSuchMethod", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        Assert.DoesNotContain(actions, a => a.ActionId == "GenerateMethod");
    }

    [Fact]
    public async Task GenerateMethod_InfersParameterTypesFromArguments()
    {
        var caller = StartVizCalling("VectorManager.DrawVector(\"label\", 3);");
        var project = NewProject(
            ("StartViz.cs", caller),
            ("VectorManager.cs", VectorManagerSource));

        var offset = caller.IndexOf("DrawVector", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var generate = actions.Single(a => a.ActionId == "GenerateMethod");
        Assert.Equal("string arg0, int arg1", generate.Data["Parameters"]);
    }

    [Fact]
    public async Task GenerateMethod_TargetOffsetSurvivesBracesInStringsAndComments()
    {
        // The replaced implementation counted '}' characters backwards through the raw text, so a
        // brace inside a comment or a string literal moved the insertion point into the wrong place.
        var target = """
            namespace TestProj
            {
                public static class Helper
                {
                    // a closing brace in a comment: }
                    public static string Text = "} not a brace }";
                }
            }
            """;

        var caller = StartVizCalling("Helper.Missing();");
        var project = NewProject(("StartViz.cs", caller), ("Helper.cs", target));

        var offset = caller.IndexOf("Missing", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var generate = actions.Single(a => a.ActionId == "GenerateMethod");
        var insertOffset = int.Parse(generate.Data["TargetInsertOffset"]);
        var text = File.ReadAllText(Path.Combine(_dir, "Helper.cs"));

        // End-to-end check: write a member at the reported offset and confirm it lands inside
        // Helper and still parses. This is the property that actually matters, and it holds
        // regardless of how many stray braces the comments and literals contain.
        var patched = text.Insert(insertOffset, "    public static void Missing() { }\r\n");
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(patched).GetRoot();

        Assert.Empty(root.GetDiagnostics().Where(d =>
            d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var helper = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "Helper");

        Assert.Contains(helper.Members
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>(),
            m => m.Identifier.Text == "Missing");
    }

    /// <summary>
    /// The whole reported bug, end to end: analyse the call, build the stub, apply it, and check the
    /// method really exists on the other class and that the call now binds.
    /// </summary>
    [Fact]
    public async Task GenerateMethod_EndToEnd_ProducesACompilableMemberOnTheTargetClass()
    {
        var caller = StartVizCalling("VectorManager.DrawVector(1.0);");
        var project = NewProject(
            ("StartViz.cs", caller),
            ("VectorManager.cs", VectorManagerSource));

        var offset = caller.IndexOf("DrawVector", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), caller, offset, 0);

        var generate = actions.Single(a => a.ActionId == "GenerateMethod");
        var stub = MethodStubBuilder.Build(generate.Data, newLine: "\r\n");
        Assert.True(stub.IsValid);

        // Apply exactly as the editor does.
        var targetPath = Path.Combine(_dir, Path.GetFileName(stub.TargetFilePath!));
        var patched = File.ReadAllText(targetPath).Insert(stub.Offset, stub.Text);
        File.WriteAllText(targetPath, patched);

        var vectorManagerFile = project.Files.Single(f => f.FileName == "VectorManager.cs");
        vectorManagerFile.Content = patched;

        // The generated member is on VectorManager, not on Viz.
        var root = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(patched).GetRoot();
        var manager = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .Single(c => c.Identifier.Text == "VectorManager");

        var generated = manager.Members
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .SingleOrDefault(m => m.Identifier.Text == "DrawVector");

        Assert.NotNull(generated);
        Assert.Contains(generated!.Modifiers, m => m.Text == "public");
        Assert.Contains(generated.Modifiers, m => m.Text == "static");

        // And the original call site now resolves.
        var (compilation, _) = await new DoodleSharp.Execution.ModuleCompiler().CreateCompilationAsync(project);
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        Assert.DoesNotContain(errors, e => e.Contains("DrawVector"));
    }

    [Fact]
    public async Task GetQuickActions_OffersRenameOnAnIdentifier()
    {
        var source = StartVizCalling("var x = 1;");
        var project = NewProject(("StartViz.cs", source));

        var offset = source.IndexOf("Main", StringComparison.Ordinal);
        var actions = await NewProvider().GetQuickActionsAsync(
            project, Path.Combine(_dir, "StartViz.cs"), source, offset, 0);

        Assert.Contains(actions, a => a.ActionId == "Rename");
    }
}
