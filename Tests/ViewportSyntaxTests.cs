using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using DoodleSharp.Execution;
using DoodleSharp.Project;

namespace DoodleSharp.Tests;

/// <summary>
/// The spelling <c>Viewports[0][1]</c> has to compile in the user's own files.
///
/// <para>
/// It cannot be a static class: C# has no static indexers (CS0720) and no namespace-level members,
/// so a bare type name can never be indexed. It is a static <i>property</i> on
/// <see cref="C2VGeometry.ViewportRoot"/>, reachable unqualified because the compiler injects
/// <c>global using static C2VGeometry.ViewportRoot;</c> as its own syntax tree into every
/// compilation. That is invisible from the source, which is exactly why it needs a test.
/// </para>
/// </summary>
public class ViewportSyntaxTests
{
    private static string[] Errors(params SyntaxTree[] trees)
    {
        var compilation = CSharpCompilation.Create(
            "ViewportProbe",
            trees,
            new ModuleCompiler().GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.GetMessage()}")
            .ToArray();
    }

    /// <summary>User code shaped like the real generated entry point: its own namespace, class Viz.</summary>
    private static string UserFile(string body) => $$"""
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using C2VGeometry;
        using DoodleSharp.Animation;
        using DoodleSharp.Console;

        namespace MyDrawing
        {
            public class Viz
            {
                public static void Main()
                {
        {{body}}
                }
            }
        }
        """;

    private const string UsesViewports = """
                    Viewports.Rows = 2;
                    Viewports.Columns = 3;

                    new VLine(new VXYZ(0, 0), new VXYZ(10, 0)).Place(Viewports[1][2]);
                    new VCircle(new VXYZ(0, 0), 5).Place();

                    Viewport right = Viewports[0][2];
                    right.Rows = 2;
                    new VPolygon(new VXYZ(0, 0), new VXYZ(1, 0), new VXYZ(0, 1)).Place(right[1][0]);
        """;

    [Fact]
    public void UserCodeCanIndexViewportsUnqualified()
    {
        Assert.Empty(Errors(SyntheticUsings.Tree, SyntaxFactory.ParseSyntaxTree(UserFile(UsesViewports))));
    }

    /// <summary>
    /// The negative control. Without the injected tree the same file must fail, or the test above
    /// would pass just as happily against a mechanism that had quietly stopped being needed — or
    /// stopped being exercised.
    /// </summary>
    [Fact]
    public void WithoutTheInjectedUsingTheSameFileFails()
    {
        var errors = Errors(SyntaxFactory.ParseSyntaxTree(UserFile(UsesViewports)));

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.StartsWith("CS0103", StringComparison.Ordinal));   // name does not exist
    }

    /// <summary>
    /// A second, hand-written file has to work too — which is the whole reason the directive is
    /// injected rather than written into the project templates. A template only covers the files it
    /// generates.
    /// </summary>
    [Fact]
    public void ASecondHandWrittenFileCanUseViewportsToo()
    {
        var helper = """
            using C2VGeometry;

            namespace MyDrawing
            {
                public static class Helper
                {
                    public static void DrawInto(int row, int column) =>
                        new VLine(new VXYZ(0, 0), new VXYZ(1, 1)).Place(Viewports[row][column]);
                }
            }
            """;

        Assert.Empty(Errors(
            SyntheticUsings.Tree,
            SyntaxFactory.ParseSyntaxTree(UserFile("            Helper.DrawInto(0, 0);")),
            SyntaxFactory.ParseSyntaxTree(helper)));
    }

    /// <summary>
    /// The injected tree must not shift anything in the user's files, because the same compilation
    /// path serves the offset-based editor features. Being a separate file is what guarantees it;
    /// this pins that it stays a separate file.
    /// </summary>
    [Fact]
    public void TheInjectedTreeIsItsOwnFileAndTouchesNoUserOffsets()
    {
        var source = UserFile(UsesViewports);
        var userTree = SyntaxFactory.ParseSyntaxTree(source);

        var compilation = CSharpCompilation.Create(
            "ViewportProbe",
            new[] { SyntheticUsings.Tree, userTree },
            new ModuleCompiler().GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var roundTripped = compilation.SyntaxTrees.Single(t => t != SyntheticUsings.Tree);

        Assert.Equal(source, roundTripped.ToString());
        Assert.NotEqual(SyntheticUsings.FilePath, roundTripped.FilePath);
    }

    /// <summary>
    /// <c>Viewports</c> is reachable through a <c>using static</c>, so it is exactly as shadowable
    /// as a type name — a project called "Viewports" would make it unreachable inside its own
    /// namespace. The reserved-name set is built by reflecting over <i>types</i>, which cannot see a
    /// member, so this one has to be added by hand and therefore has to be tested.
    /// </summary>
    [Fact]
    public void ViewportsIsReservedEvenThoughItIsAMemberNotAType()
    {
        Assert.True(ReservedNames.IsApiName("Viewports"));
        Assert.NotEqual("Viewports", Templates.SanitizeIdentifier("Viewports"));
    }

    [Theory]
    [InlineData("Viewport")]        // C2VGeometry.Viewport, found by reflection
    [InlineData("ViewportRow")]     // C2VGeometry.ViewportRow, found by reflection
    [InlineData("ViewportRoot")]    // the holder class itself
    public void TheViewportTypesAreReservedByReflection(string name)
    {
        Assert.True(ReservedNames.IsApiName(name));
    }

    /// <summary>Ordinal, because C# is case-sensitive: a project called "viewports" shadows nothing.</summary>
    [Fact]
    public void ReservationIsCaseSensitive()
    {
        Assert.False(ReservedNames.IsApiName("viewports"));
        Assert.Equal("viewports", Templates.SanitizeIdentifier("viewports"));
    }
}
