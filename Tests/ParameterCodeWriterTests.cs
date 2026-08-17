using System;
using C2VGeometry;
using DoodleSharp.Project;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Exercises the argument-list scanner that locates the value literal in a
/// <c>GlobalParameters.Set(...)</c> call, so an edit made in the Global Parameters panel can be
/// written back into the user's source instead of living only for the current run.
/// </summary>
// One test touches the GlobalParameters static registry, so this class joins the serialized
// collection alongside GlobalParametersTests rather than racing it.
[Collection("CanvasState")]
public class ParameterCodeWriterTests
{
    private static string Rewrite(string source, string name, int line, string literal) =>
        ParameterCodeWriter.TryRewrite(source, name, line, literal)!;

    [Fact]
    public void Rewrites_SimpleNumericDeclaration()
    {
        var source = """
            void Main()
            {
                GlobalParameters.Set<double>("String Length", 10);
            }
            """;

        var result = Rewrite(source, "String Length", 3, "42.5");

        Assert.Contains("Set<double>(\"String Length\", 42.5);", result);
    }

    [Fact]
    public void Rewrites_LeavingTrailingArgumentsIntact()
    {
        var source = """GlobalParameters.Set<double>("Length", 10, min: 0, max: 50, group: "Strings");""";

        var result = Rewrite(source, "Length", 1, "31");

        Assert.Equal("""GlobalParameters.Set<double>("Length", 31, min: 0, max: 50, group: "Strings");""", result);
    }

    [Fact]
    public void Rewrites_BooleanDeclaration()
    {
        var source = "GlobalParameters.Set<bool>(\"String Broken\", true);";

        Assert.Equal("GlobalParameters.Set<bool>(\"String Broken\", false);",
            Rewrite(source, "String Broken", 1, "false"));
    }

    [Fact]
    public void Rewrites_StringDeclaration()
    {
        var source = "GlobalParameters.Set<string>(\"Name\", \"String-A\");";

        Assert.Equal("GlobalParameters.Set<string>(\"Name\", \"String-B\");",
            Rewrite(source, "Name", 1, "\"String-B\""));
    }

    [Fact]
    public void SkipsCommasInsideNestedCalls()
    {
        var source = """GlobalParameters.Set<double>("Length", Math.Max(1, 2), min: 0);""";

        Assert.Equal("""GlobalParameters.Set<double>("Length", 9, min: 0);""",
            Rewrite(source, "Length", 1, "9"));
    }

    [Fact]
    public void SkipsCommasInsideStringLiterals()
    {
        var source = """GlobalParameters.Set<string>("Label, with comma", "a, b");""";

        Assert.Equal("""GlobalParameters.Set<string>("Label, with comma", "z");""",
            Rewrite(source, "Label, with comma", 1, "\"z\""));
    }

    [Fact]
    public void HandlesNamedValueArgument()
    {
        var source = """GlobalParameters.Set<double>("Length", value: 10, min: 0);""";

        Assert.Equal("""GlobalParameters.Set<double>("Length", value: 4, min: 0);""",
            Rewrite(source, "Length", 1, "4"));
    }

    [Fact]
    public void HandlesNonGenericCall()
    {
        var source = "GlobalParameters.Set(\"Length\", 10.0);";

        Assert.Equal("GlobalParameters.Set(\"Length\", 12);", Rewrite(source, "Length", 1, "12"));
    }

    [Fact]
    public void HandlesDeclarationSpanningMultipleLines()
    {
        var source = """
            GlobalParameters.Set<double>(
                "Length",
                10,
                min: 0);
            """;

        var result = Rewrite(source, "Length", 1, "77");

        Assert.Contains("77", result);
        Assert.Contains("min: 0", result);
        Assert.DoesNotContain("10", result);
    }

    [Fact]
    public void PicksTheMatchingCall_WhenSeveralShareALine()
    {
        var source = """GlobalParameters.Set<double>("A", 1); GlobalParameters.Set<double>("B", 2);""";

        Assert.Equal("""GlobalParameters.Set<double>("A", 1); GlobalParameters.Set<double>("B", 5);""",
            Rewrite(source, "B", 1, "5"));
    }

    [Fact]
    public void FindsDeclarationOnALaterLine()
    {
        // The recorded line points at the statement; scanning continues forward from there.
        var source = """
            // comment
            GlobalParameters.Set<double>("Length", 10);
            """;

        Assert.Contains("\"Length\", 3);", Rewrite(source, "Length", 1, "3"));
    }

    [Fact]
    public void ReturnsNull_WhenDeclarationIsNotFound()
    {
        Assert.Null(ParameterCodeWriter.TryRewrite("// the declaration was deleted", "Length", 1, "5"));
    }

    [Fact]
    public void ReturnsNull_WhenNameDoesNotMatch()
    {
        var source = "GlobalParameters.Set<double>(\"Other\", 10);";

        Assert.Null(ParameterCodeWriter.TryRewrite(source, "Length", 1, "5"));
    }

    [Fact]
    public void ReturnsSourceUnchanged_WhenLiteralAlreadyMatches()
    {
        var source = """GlobalParameters.Set<double>("Length", 10);""";

        Assert.Equal(source, ParameterCodeWriter.TryRewrite(source, "Length", 1, "10"));
    }

    /// <summary>The Parameter overload must format the literal the same way the panel displays it.</summary>
    [Fact]
    public void ParameterOverload_UsesTheParametersOwnLiteralFormatting()
    {
        GlobalParameters.ClearAll();
        try
        {
            // Declared here, so CallerLineNumber points at this line — which is what makes the
            // write-back work without any searching.
            var p = GlobalParameters.Set<string>("Quote", "plain");
            GlobalParameters.Assign("Quote", "He said \"hi\"");

            var source = new string('\n', p.SourceLine - 1) +
                         "GlobalParameters.Set<string>(\"Quote\", \"plain\");";

            var result = ParameterCodeWriter.TryRewrite(source, p);

            Assert.NotNull(result);
            Assert.EndsWith("GlobalParameters.Set<string>(\"Quote\", \"He said \\\"hi\\\"\");", result);
        }
        finally { GlobalParameters.ClearAll(); }
    }
}
