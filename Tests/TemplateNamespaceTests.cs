using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using DoodleSharp.Execution;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// The generated project namespace must not shadow the API the templates import.
///
/// <para>
/// A project named "Mouse" produced <c>namespace Mouse</c>, and a namespace declaration is searched
/// before any <c>using</c> — so <c>Mouse.OnMove(...)</c> in the user's own file resolved against the
/// user's namespace and failed with CS0234 "the type or namespace name 'OnMove' does not exist in the
/// namespace 'Mouse'". Same class of failure as the <c>DoodleSharp.Canvas</c> shadowing in
/// CanvasApiTests: a name that is a namespace in one place and a type in another.
/// </para>
/// </summary>
public class TemplateNamespaceTests
{
    private static string[] CompileErrors(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TemplateProbe",
            new[] { SyntaxFactory.ParseSyntaxTree(source) },
            new ModuleCompiler().GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()} @ {d.Location.GetLineSpan().StartLinePosition}")
            .ToArray();
    }

    /// <summary>Body inserted into the generated entry point, exercising three imported namespaces.</summary>
    private const string ApiCall = """
                    Mouse.OnMove(e => VizConsole.Log(e.X));
                    Frame.Request(t => { });
                    var c = new VCircle(new VXYZ(0, 0), 5);
        """;

    private static string TemplateWithApiCall(string projectName)
    {
        var template = Templates.GetStartVizTemplate(projectName);
        return template.Replace("            var p = new VPoint(0, 0);", ApiCall);
    }

    [Theory]
    [InlineData("Mouse")]        // DoodleSharp.Animation.Mouse
    [InlineData("Frame")]        // DoodleSharp.Animation.Frame
    [InlineData("VCircle")]      // C2VGeometry.VCircle
    [InlineData("Canvas")]       // C2VGeometry.Canvas
    [InlineData("Console")]      // System.Console
    [InlineData("Math")]         // System.Math
    [InlineData("List")]         // System.Collections.Generic.List<T>, arity stripped
    public void AProjectNamedAfterAnImportedTypeGetsADifferentNamespace(string projectName)
    {
        Assert.NotEqual(projectName, Templates.SanitizeIdentifier(projectName));
    }

    [Fact]
    public void TheGeneratedTemplateCompilesForAProjectNamedMouse()
    {
        Assert.Empty(CompileErrors(TemplateWithApiCall("Mouse")));
    }

    /// <summary>
    /// The negative control. Without it the test above proves nothing: it would pass just as happily
    /// if the API call were silently unreachable rather than the namespace being renamed.
    /// </summary>
    [Fact]
    public void TheSameFileUnderTheShadowingNamespaceStillFails()
    {
        var shadowed = TemplateWithApiCall("Mouse")
            .Replace($"namespace {Templates.SanitizeIdentifier("Mouse")}", "namespace Mouse");

        var errors = CompileErrors(shadowed);

        Assert.Contains(errors, e => e.StartsWith("CS0234") && e.Contains("OnMove"));
    }

    [Fact]
    public void AnOrdinaryProjectNameIsUntouched()
    {
        Assert.Equal("MyProject", Templates.SanitizeIdentifier("MyProject"));
        Assert.Equal("Doodles", Templates.SanitizeIdentifier("Doodles"));
        Assert.Equal("My_Project", Templates.SanitizeIdentifier("My Project"));
        Assert.Equal("_2026_Plans", Templates.SanitizeIdentifier("2026 Plans"));

        // Case-sensitive: "mouse" shadows nothing, so renaming it would be gratuitous.
        Assert.Equal("mouse", Templates.SanitizeIdentifier("mouse"));
    }

    [Fact]
    public void AKeywordProjectNameBecomesALegalIdentifier()
    {
        foreach (var keyword in new[] { "class", "int", "namespace", "new" })
        {
            var identifier = Templates.SanitizeIdentifier(keyword);
            Assert.Equal(SyntaxKind.None, SyntaxFacts.GetKeywordKind(identifier));
        }
    }

    /// <summary>
    /// The new-module template took its two names raw, so the default file name ("Untitled-1")
    /// produced <c>public class Untitled-1</c> — a file that could not compile the moment it was
    /// created — and a project name with a space produced an equally invalid namespace.
    /// </summary>
    [Fact]
    public void TheNewModuleTemplateSanitizesBothNames()
    {
        var source = Templates.GetEmptyModuleTemplate("My Project", "Untitled-1");

        Assert.Contains("namespace My_Project", source);
        Assert.Contains("class Untitled_1", source);
        Assert.Empty(CompileErrors(source));
    }

    /// <summary>
    /// The entry point is looked up as "{sanitized project name}.Viz", so the template and the
    /// compiler have to agree on the sanitizer — that is the whole reason it lives in one place.
    /// </summary>
    [Fact]
    public void TheTemplateDeclaresTheNamespaceTheCompilerLooksFor()
    {
        Assert.Contains($"namespace {Templates.SanitizeIdentifier("Mouse")}",
            Templates.GetStartVizTemplate("Mouse"));
        Assert.Contains($"namespace {Templates.SanitizeIdentifier("Mouse")}",
            Templates.GetStartSketchTemplate("Mouse"));
    }

    /// <summary>
    /// Renaming the namespace would otherwise strand every project already on disk that carries the
    /// old one — plus anyone who renamed their namespace by hand. The scan is the safety net; the two
    /// tests below cover what it finds, and this one pins that the lookup actually calls it.
    /// </summary>
    [Fact]
    public void TheEntryPointLookupFallsBackToAScan()
    {
        var source = File.ReadAllText(
            Path.Combine(ArrowheadConsistencyTests.RepoRoot(), "Execution", "ModuleCompiler.cs"));

        Assert.Contains("entryType = FindEntryTypeByScan(assembly);", source);
    }

    /// <summary>The scan itself, against a real assembly whose namespace is nothing like its name.</summary>
    [Fact]
    public void TheScanFindsVizUnderAnyNamespace()
    {
        var assembly = EmitAssembly("""
            namespace Something.Entirely.Different
            {
                public class Helper { public static void Main() { } }
                public class Viz { public static void Main() { } }
            }
            """);

        var entry = ModuleCompiler.FindEntryTypeByScan(assembly);

        Assert.NotNull(entry);
        Assert.Equal("Something.Entirely.Different.Viz", entry!.FullName);
    }

    [Fact]
    public void TheScanFindsNothingWhenNoTypeHasAMain()
    {
        var assembly = EmitAssembly("namespace Empty { public class Viz { public void Main() { } } }");

        Assert.Null(ModuleCompiler.FindEntryTypeByScan(assembly));
    }

    private static System.Reflection.Assembly EmitAssembly(string source)
    {
        var compilation = CSharpCompilation.Create(
            "ScanProbe" + Guid.NewGuid().ToString("N"),
            new[] { SyntaxFactory.ParseSyntaxTree(source) },
            new ModuleCompiler().GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        return System.Reflection.Assembly.Load(stream.ToArray());
    }
}
