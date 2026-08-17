using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Execution;
using DoodleSharp.Project;
using ICSharpCode.AvalonEdit.Document;
using Microsoft.CodeAnalysis;

namespace DoodleSharp.Tests;

/// <summary>
/// Mapping compiler diagnostics onto something the editor can underline. The interesting case is the
/// zero-width "missing token" diagnostic, which is what most half-typed code produces.
/// </summary>
public class DiagnosticRangeTests : IDisposable
{
    private readonly string _dir;

    public DiagnosticRangeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "C2V_diag_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void NonEmptySpanIsUsedAsIs()
    {
        var document = new TextDocument("var value = 1;");

        Assert.True(DiagnosticRange.TryResolve(document, 1, 4, 1, 9, out var offset, out var length));
        Assert.Equal(4, offset);
        Assert.Equal(5, length);
        Assert.Equal("value", document.GetText(offset, length));
    }

    [Fact]
    public void EmptySpanInsideAWordUnderlinesThatWord()
    {
        var document = new TextDocument("    counter");

        // Zero-width span at the start of "counter".
        Assert.True(DiagnosticRange.TryResolve(document, 1, 4, 1, 4, out var offset, out var length));
        Assert.Equal("counter", document.GetText(offset, length));
    }

    [Fact]
    public void EmptySpanAfterATokenUnderlinesThatToken()
    {
        // The shape of a real missing-token diagnostic: `for` with the span pointing just past it,
        // which is where the '(' should have been.
        var document = new TextDocument("            for");

        Assert.True(DiagnosticRange.TryResolve(document, 1, 15, 1, 15, out var offset, out var length));
        Assert.Equal("for", document.GetText(offset, length));
    }

    [Fact]
    public void EmptySpanOnAnEmptyLineStillProducesNoRange()
    {
        // Nothing on the line to underline, and no previous token on it either.
        var document = new TextDocument("");
        Assert.False(DiagnosticRange.TryResolve(document, 1, 0, 1, 0, out _, out _));
    }

    [Fact]
    public void OutOfRangeLinesAreRejectedRatherThanThrowing()
    {
        var document = new TextDocument("one line");

        Assert.False(DiagnosticRange.TryResolve(document, 99, 0, 99, 0, out _, out _));
        Assert.False(DiagnosticRange.TryResolve(document, 0, 0, 0, 0, out _, out _));
    }

    [Fact]
    public void ColumnsPastTheEndOfALineAreClamped()
    {
        var document = new TextDocument("short");

        Assert.True(DiagnosticRange.TryResolve(document, 1, 500, 1, 500, out var offset, out var length));
        Assert.InRange(offset, 0, document.TextLength);
        Assert.InRange(offset + length, 0, document.TextLength);
    }

    /// <summary>
    /// End to end against the real compiler: the reported case produced seven diagnostics, all
    /// zero-width, and therefore no squiggles and an error count of zero.
    /// </summary>
    [Fact]
    public async Task EveryDiagnosticFromABareForResolvesToAVisibleRange()
    {
        const string broken = """
            using System;
            namespace TestBed
            {
                public static class VectorManager
                {
                    public static void DrawVector()
                    {
                        for
                    }
                }
            }
            """;

        var project = VizCodeProject.CreateNew(_dir, "TestBed");
        var path = Path.Combine(_dir, "VectorManager.cs");
        File.WriteAllText(path, broken);
        project.Files.Add(new VizCodeFile { FilePath = path, Content = broken });

        var result = await new ModuleCompiler().CheckSyntaxAsync(project);
        var errors = (result.Diagnostics ?? Enumerable.Empty<Diagnostic>())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.NotEmpty(errors);
        // The premise of the fix: the compiler really does report these with no width.
        Assert.All(errors, d => Assert.Equal(0, d.Location.SourceSpan.Length));

        var document = new TextDocument(broken);
        foreach (var diagnostic in errors)
        {
            var span = diagnostic.Location.GetLineSpan();
            Assert.True(
                DiagnosticRange.TryResolve(document,
                    span.StartLinePosition.Line + 1, span.StartLinePosition.Character,
                    span.EndLinePosition.Line + 1, span.EndLinePosition.Character,
                    out var offset, out var length),
                $"{diagnostic.Id} produced no underlinable range");

            Assert.True(length > 0, $"{diagnostic.Id} resolved to a zero-width range");
            Assert.InRange(offset + length, 0, document.TextLength);
        }
    }
}
