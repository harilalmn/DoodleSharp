using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using DoodleSharp.Execution;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// A shadowed DoodleSharp name is reported at the declaration that caused it, not at the use site.
///
/// <para>
/// Roslyn blames the token it failed to look up: for <c>namespace Mouse</c> containing
/// <c>Mouse.OnMove(...)</c> it reports CS0234 on <c>OnMove</c> — the one token that is not wrong.
/// <see cref="ShadowedNameDiagnostics"/> maps that back onto the declaration. These tests pin both
/// halves: that the remap fires where it should, and — the half that matters more — that it stays
/// out of the way of every ordinary error.
/// </para>
/// </summary>
/// <remarks>
/// In the "CanvasState" collection (note 9): <c>TheRunPathReportsAtTheDeclaration</c> drives
/// <see cref="ModuleCompiler.CompileAndExecuteAsync"/>, which clears the
/// <c>CanvasRenderer.Instance</c> and <c>ConsoleOutput.Instance</c> singletons.
/// </remarks>
[Collection("CanvasState")]
public class ShadowedNameDiagnosticsTests
{
    private const string Usings = """
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using C2VGeometry;
        using DoodleSharp.Animation;
        using DoodleSharp.Console;

        """;

    private static CSharpCompilation Compile(string source, string fileName = "StartViz.cs") =>
        CSharpCompilation.Create(
            "ShadowProbe",
            new[] { CSharpSyntaxTree.ParseText(source, path: fileName) },
            new ModuleCompiler().GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static Diagnostic[] Remap(string source)
    {
        var compilation = Compile(source);
        return ShadowedNameDiagnostics
            .Remap(compilation.GetDiagnostics(), compilation)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }

    /// <summary>The 1-based line the diagnostic underlines, matching what the console prints.</summary>
    private static int Line(Diagnostic d) => d.Location.GetLineSpan().StartLinePosition.Line + 1;

    /// <summary>The source text the diagnostic actually underlines.</summary>
    private static string UnderlinedText(Diagnostic d) =>
        d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan);

    // ── The reported case ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The bug as reported: a project named "Mouse". The error must move off <c>OnMove</c> and onto
    /// the <c>Mouse</c> token of the namespace declaration.
    /// </summary>
    [Fact]
    public void AShadowingNamespaceIsReportedAtTheNamespace()
    {
        var source = Usings + """
            namespace Mouse
            {
                public class Viz
                {
                    public static void Main()
                    {
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var errors = Remap(source);

        var error = Assert.Single(errors);
        Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, error.Id);
        Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());

        // The declaration, not the use site.
        Assert.Equal("Mouse", UnderlinedText(error));
        Assert.Equal(UsingsLineCount, Line(error));
    }

    /// <summary>
    /// Without the remap this is the diagnostic the user saw — pinned so the test above is known to
    /// be measuring a real change rather than passing on an already-clean compile.
    /// </summary>
    [Fact]
    public void WithoutTheRemapTheErrorLandsOnTheUseSite()
    {
        var source = Usings + """
            namespace Mouse
            {
                public class Viz
                {
                    public static void Main()
                    {
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var raw = Compile(source).GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        var error = Assert.Single(raw);
        Assert.Equal("CS0234", error.Id);

        // Roslyn underlines the use site — the namespace declaration, which is what has to change,
        // is not mentioned at all.
        Assert.Equal("Mouse.OnMove", UnderlinedText(error));
        Assert.Contains("Mouse.OnMove(e => { });", SourceLine(source, Line(error)));
        Assert.NotEqual(UsingsLineCount, Line(error));
    }

    // ── The other declaration kinds the user named ───────────────────────────────────────────

    [Fact]
    public void AShadowingClassIsReportedAtTheClass()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Frame { }

                public class Viz
                {
                    public static void Main()
                    {
                        Frame.Request(t => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, error.Id);
        Assert.Equal("Frame is a keyword. try another name", error.GetMessage());
        Assert.Equal("Frame", UnderlinedText(error));
        Assert.Contains("public class Frame", SourceLine(source, Line(error)));
    }

    [Fact]
    public void AShadowingLocalVariableIsReportedAtTheVariable()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Viz
                {
                    public static void Main()
                    {
                        var Mouse = 5;
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
        Assert.Contains("var Mouse = 5;", SourceLine(source, Line(error)));
    }

    [Fact]
    public void AShadowingFieldIsReportedAtTheField()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Viz
                {
                    private static int Mouse = 5;

                    public static void Main()
                    {
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
        Assert.Contains("private static int Mouse", SourceLine(source, Line(error)));
    }

    [Fact]
    public void AShadowingParameterIsReportedAtTheParameter()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Viz
                {
                    public static void Main() => Helper(1);

                    private static void Helper(int Mouse)
                    {
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
        Assert.Contains("private static void Helper(int Mouse)", SourceLine(source, Line(error)));
    }

    // ── One error per declaration, however many uses it broke ────────────────────────────────

    /// <summary>
    /// Every use of the shadowed name fails, and repeating the same advice once per use is noise —
    /// there is one thing to change.
    /// </summary>
    [Fact]
    public void ManyBrokenUsesCollapseToOneErrorAtTheDeclaration()
    {
        var source = Usings + """
            namespace Mouse
            {
                public class Viz
                {
                    public static void Main()
                    {
                        Mouse.OnMove(e => { });
                        Mouse.OnDown(e => { });
                        Mouse.OnUp(e => { });
                        Mouse.OnWheel(e => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
    }

    /// <summary>Two different shadowed names are two different things to fix, so two errors.</summary>
    [Fact]
    public void TwoShadowedNamesAreReportedSeparately()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Frame { }
                public class Mouse { }

                public class Viz
                {
                    public static void Main()
                    {
                        Frame.Request(t => { });
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var errors = Remap(source);

        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, e.Id));
        Assert.Contains(errors, e => e.GetMessage() == "Frame is a keyword. try another name");
        Assert.Contains(errors, e => e.GetMessage() == "Mouse is a keyword. try another name");
    }

    // ── Staying out of the way: the half that keeps this safe ────────────────────────────────

    /// <summary>
    /// The sanitized namespace the templates now generate shadows nothing, so a healthy project
    /// must compile clean — the remap must not invent an error.
    /// </summary>
    [Fact]
    public void TheGeneratedTemplateProducesNoDiagnostic()
    {
        var source = Templates.GetStartVizTemplate("Mouse")
            .Replace("            var p = new VPoint(0, 0);", "            Mouse.OnMove(e => { });");

        Assert.Empty(Remap(source));
    }

    /// <summary>
    /// An ordinary typo against the real API must survive untouched: the qualifier binds to the
    /// library type, not to anything the user declared, so nothing was shadowed.
    /// </summary>
    [Fact]
    public void AnOrdinaryTypoAgainstTheRealApiIsLeftAlone()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Viz
                {
                    public static void Main()
                    {
                        Mouse.OnMoveee(e => { });
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.NotEqual(ShadowedNameDiagnostics.DiagnosticId, error.Id);
        Assert.Equal("OnMoveee", UnderlinedText(error));
    }

    /// <summary>
    /// The dangerous over-reach: the user declares a reserved name, and then makes an unrelated
    /// mistake against it. "Mouse is a keyword" would be actively misleading — the member lookup on
    /// their own <c>Mouse</c> is what failed, and the remap is restricted to the lookup-failure ids
    /// so a wrong argument count stays a wrong argument count.
    /// </summary>
    [Fact]
    public void AnUnrelatedErrorOnAShadowedNameKeepsItsOwnDiagnostic()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Shape
                {
                    public static void Go(int a) { }
                }

                public class Viz
                {
                    public static void Main()
                    {
                        Shape.Go(1, 2, 3);
                    }
                }
            }
            """;

        var error = Assert.Single(Remap(source));
        Assert.NotEqual(ShadowedNameDiagnostics.DiagnosticId, error.Id);
    }

    /// <summary>A case-different name shadows nothing, matching the sanitizer's ordinal rule.</summary>
    [Fact]
    public void ACaseDifferentNameIsNotReserved()
    {
        var source = Usings + """
            namespace Doodles
            {
                public class Viz
                {
                    public static void Main()
                    {
                        var mouse = new VXYZ(0, 0);
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        Assert.Empty(Remap(source));
    }

    /// <summary>
    /// Warnings are not remapped, and unrelated errors elsewhere in the file must still be reported
    /// alongside the naming error rather than swallowed with it.
    /// </summary>
    [Fact]
    public void UnrelatedErrorsSurviveAlongsideTheNamingError()
    {
        var source = Usings + """
            namespace Mouse
            {
                public class Viz
                {
                    public static void Main()
                    {
                        Mouse.OnMove(e => { });
                        int broken = "not an int";
                    }
                }
            }
            """;

        var errors = Remap(source);

        Assert.Contains(errors, e => e.Id == ShadowedNameDiagnostics.DiagnosticId);
        Assert.Contains(errors, e => e.Id == "CS0029");
    }

    /// <summary>
    /// The cause is listed before its consequences: it is the only one of the reported errors the
    /// user can act on, and the console prints them in order.
    /// </summary>
    [Fact]
    public void TheNamingErrorIsReportedFirst()
    {
        var source = Usings + """
            namespace Mouse
            {
                public class Viz
                {
                    public static void Main()
                    {
                        int broken = "not an int";
                        Mouse.OnMove(e => { });
                    }
                }
            }
            """;

        var errors = Remap(source);

        Assert.True(errors.Length >= 2);
        Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, errors[0].Id);
    }

    // ── Wiring ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A clean compile must come back byte-identical, and cost nothing: the remap gates on there
    /// being a reserved declaration at all before it touches a semantic model.
    /// </summary>
    [Fact]
    public void ACleanCompilationIsReturnedUnchanged()
    {
        var source = Templates.GetStartVizTemplate("Doodles");
        var compilation = Compile(source);
        var original = compilation.GetDiagnostics();

        var remapped = ShadowedNameDiagnostics.Remap(original, compilation);

        Assert.Equal(original, remapped);
        Assert.DoesNotContain(remapped, d => d.Id == ShadowedNameDiagnostics.DiagnosticId);
    }

    [Fact]
    public void NullInputsAreHandled()
    {
        Assert.Empty(ShadowedNameDiagnostics.Remap(null, null));
        Assert.Empty(ShadowedNameDiagnostics.Remap(Array.Empty<Diagnostic>(), null));
    }

    /// <summary>
    /// The sanitizer and the diagnostic must agree on what a DoodleSharp keyword is — two
    /// definitions would let a name be renamed at creation time but not reported when typed by
    /// hand, or the reverse.
    /// </summary>
    [Fact]
    public void TheReservedSetIsSharedWithTheSanitizer()
    {
        foreach (var name in new[] { "Mouse", "Frame", "VCircle", "Canvas", "Shape", "Console", "Math", "List" })
        {
            Assert.True(ReservedNames.IsApiName(name), $"{name} should be reserved");
            Assert.NotEqual(name, Templates.SanitizeIdentifier(name));
        }

        // "Viz" is the template's own class name, so the sanitizer avoids it for a *project* name —
        // but it is not a DoodleSharp keyword, and every project declares `class Viz`.
        Assert.False(ReservedNames.IsApiName("Viz"));
    }

    /// <summary>
    /// Every project declares <c>class Viz</c> and the sketch template declares
    /// <c>class MySketch</c>. Reusing the sanitizer's full reserved set here would have made the
    /// entry point of every project an error.
    /// </summary>
    [Fact]
    public void TheTemplatesOwnClassNamesAreNotReportedAsKeywords()
    {
        Assert.Empty(Remap(Templates.GetStartVizTemplate("Doodles")));
        Assert.Empty(Remap(Templates.GetStartSketchTemplate("Doodles")));
    }

    /// <summary>
    /// The remapper working in isolation is worth nothing if the compile path does not call it.
    /// This drives the real <see cref="ModuleCompiler.CheckSyntaxAsync"/> — the method behind the
    /// editor squiggles and the error count in the status bar — over a project on disk.
    /// </summary>
    [Fact]
    public async Task TheSyntaxCheckPathReportsAtTheDeclaration()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DoodleSharpShadowProbe", Guid.NewGuid().ToString("N"));
        try
        {
            var project = VizCodeProject.CreateNew(dir, "ShadowProbe");
            var entry = project.EntryPointFile!;

            // A project that already carries the shadowing namespace — created before the template
            // learned to rename it, or renamed by hand.
            entry.Content = Usings + """
                namespace Mouse
                {
                    public class Viz
                    {
                        public static void Main()
                        {
                            Mouse.OnMove(e => { });
                        }
                    }
                }
                """;

            var result = await new ModuleCompiler().CheckSyntaxAsync(project);

            Assert.False(result.Success);
            var errors = result.Diagnostics!
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToArray();

            var error = Assert.Single(errors);
            Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, error.Id);
            Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
            Assert.Equal("Mouse", UnderlinedText(error));
            Assert.Equal(UsingsLineCount, Line(error));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The other half of the wiring: pressing Run goes through
    /// <see cref="ModuleCompiler.CompileAndExecuteAsync"/>, which compiles with
    /// <c>forExecution: true</c> and so applies both source rewriters (note 41). Those shift
    /// character offsets, so the declaration has to still be located correctly in the rewritten
    /// tree — which is why this is tested separately from the syntax-check path rather than assumed
    /// to behave the same.
    /// </summary>
    [Fact]
    public async Task TheRunPathReportsAtTheDeclaration()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DoodleSharpShadowProbe", Guid.NewGuid().ToString("N"));
        try
        {
            var project = VizCodeProject.CreateNew(dir, "ShadowProbe");
            project.EntryPointFile!.Content = Usings + """
                namespace Mouse
                {
                    public class Viz
                    {
                        public static void Main()
                        {
                            Mouse.OnMove(e => { });
                        }
                    }
                }
                """;

            var result = await new ModuleCompiler().CompileAndExecuteAsync(project);

            Assert.False(result.Success);

            var error = Assert.Single(result.Diagnostics!
                .Where(d => d.Severity == DiagnosticSeverity.Error));

            Assert.Equal(ShadowedNameDiagnostics.DiagnosticId, error.Id);
            Assert.Equal("Mouse is a keyword. try another name", error.GetMessage());
            Assert.Equal(UsingsLineCount, Line(error));

            // The console prints Error verbatim when there are no diagnostics, and the status bar
            // counts the diagnostics — either way the user must not still be told about OnMove.
            Assert.DoesNotContain("OnMove", result.Error ?? string.Empty);
            Assert.Contains("Mouse is a keyword. try another name", result.Error ?? string.Empty);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string SourceLine(string source, int oneBasedLine) =>
        source.Replace("\r\n", "\n").Split('\n')[oneBasedLine - 1];

    /// <summary>
    /// The 1-based line the appended snippet starts on: <see cref="Usings"/> ends with a blank line,
    /// so its line count is the line the <c>namespace</c> declaration lands on.
    /// </summary>
    private static int UsingsLineCount => Usings.Replace("\r\n", "\n").Split('\n').Length;
}
