using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Execution;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// What the completion list contains in the places where "every symbol in scope" is the wrong
/// answer: inside an argument list, inside a property's accessor list, and after <c>new</c> in a
/// property initialiser. Roslyn's <c>LookupSymbols</c> answers the same way everywhere, so each of
/// these is a context test the service has to make itself.
/// </summary>
public class CompletionContextTests
{
    private static RoslynCompletionService NewService() =>
        new RoslynCompletionService(new ModuleCompiler().GetReferences());

    private static (string Code, int Position) Marked(string markedCode)
    {
        var position = markedCode.IndexOf('$');
        Assert.True(position >= 0, "Test source must contain a '$' caret marker");
        return (markedCode.Remove(position, 1), position);
    }

    private static async Task<List<string>> CompleteAsync(string markedCode)
    {
        var (code, position) = Marked(markedCode);
        var (completions, _, _, _) = await NewService().GetCompletionsAsync(code, position);
        return completions.Select(c => c.Text).ToList();
    }

    private static async Task<string?> ExpectedTypeAsync(string markedCode)
    {
        var (code, position) = Marked(markedCode);
        var (_, _, _, expectedType) = await NewService().GetCompletionsAsync(code, position);
        return expectedType;
    }

    private const string Preamble = """
        using System;
        using System.Collections.Generic;

        namespace T
        {
            public class Viz
            {
                public static double Scale = 2.0;

                public static void Draw(double radius) { }

                public static void Draw2(object value) { }

                public static void Run(Action action) { }

                public static void Main()
                {
                    double radius = 5;
                    string label = "x";
        """;

    private const string Postamble = """
                }
            }
        }
        """;

    private static string InMain(string body) => $"{Preamble}\n            {body}\n{Postamble}";

    // ---- Argument position: values, not the whole world -------------------------------------

    [Fact]
    public async Task InsideAnArgumentList_LocalsAreOffered()
    {
        var items = await CompleteAsync(InMain("Draw($);"));

        Assert.Contains("radius", items);
        Assert.Contains("label", items);
    }

    [Fact]
    public async Task InsideAnArgumentList_FieldsAreOffered()
    {
        // "Global" from the user's point of view: something already in hand, just not a local.
        Assert.Contains("Scale", await CompleteAsync(InMain("Draw($);")));
    }

    [Fact]
    public async Task InsideAnArgumentList_MethodsAndTypesAndStatementKeywordsAreNot()
    {
        // An argument is a value. Offering every method, type and control-flow keyword in scope
        // buried the handful of names the user actually meant.
        var items = await CompleteAsync(InMain("Draw($);"));

        Assert.DoesNotContain("Draw", items);
        Assert.DoesNotContain("Viz", items);
        Assert.DoesNotContain("for", items);
        Assert.DoesNotContain("while", items);
    }

    [Fact]
    public async Task InsideAnArgumentList_TheKeywordsThatCanStartAnArgumentSurvive()
    {
        var items = await CompleteAsync(InMain("Draw($);"));

        Assert.Contains("new", items);
        Assert.Contains("null", items);
        Assert.Contains("true", items);
    }

    [Fact]
    public async Task InsideAnArgumentList_FilteringSurvivesATypedPrefix()
    {
        var items = await CompleteAsync(InMain("Draw(ra$);"));

        Assert.Contains("radius", items);
        Assert.DoesNotContain("Draw", items);
    }

    [Fact]
    public async Task AfterNewInsideAnArgumentList_TypesComeBack()
    {
        // The restriction lifts after `new`: naming a type is the entire point there.
        var items = await CompleteAsync(InMain("Draw2(new $);"));

        Assert.Contains("Viz", items);
    }

    [Fact]
    public async Task InsideALambdaBodyWithinAnArgumentList_TheFullListReturns()
    {
        // The caret is in a statement, not in the argument itself.
        var items = await CompleteAsync(InMain("Run(() => { $ });"));

        Assert.Contains("for", items);
    }

    [Fact]
    public async Task OutsideAnArgumentList_NothingChanges()
    {
        var items = await CompleteAsync(InMain("var x = $"));

        Assert.Contains("radius", items);
        Assert.Contains("for", items);
    }

    // ---- Accessor list: get and set, not GetHashCode --------------------------------------------

    [Fact]
    public async Task InsideAnAccessorList_TheAccessorsAreOffered()
    {
        var items = await CompleteAsync("""
            namespace T
            {
                public class Widget
                {
                    public int Count { g$ }
                }
            }
            """);

        Assert.Contains("get;", items);
        Assert.Contains("set;", items);
    }

    [Fact]
    public async Task InsideAnAccessorList_ObjectMembersAreNot()
    {
        // Typing `{get` offered GetHashCode and GetType and never `get;` itself.
        var items = await CompleteAsync("""
            namespace T
            {
                public class Widget
                {
                    public int Count { get; s$ }
                }
            }
            """);

        Assert.DoesNotContain("GetHashCode", items);
        Assert.DoesNotContain("GetType", items);
        Assert.Contains("set;", items);
    }

    [Fact]
    public async Task InsideAnAccessorBody_OrdinaryCompletionReturns()
    {
        // `get { return |; }` is code, and the accessor restriction must not reach into it.
        var items = await CompleteAsync("""
            namespace T
            {
                public class Widget
                {
                    private int _n;
                    public int Count { get { return $ } }
                }
            }
            """);

        Assert.Contains("_n", items);
    }

    // ---- Property initialisers know their own type ---------------------------------------------

    [Fact]
    public async Task PropertyInitialiser_KnowsTheDeclaredType()
    {
        // `public List<string> Names { get; set; } = new |` suggested nothing: a property's
        // initialiser hangs off the property, not off a variable declarator, so the expected type
        // was never worked out.
        var expected = await ExpectedTypeAsync("""
            namespace T
            {
                public class Widget
                {
                    public System.Collections.Generic.List<string> Names { get; set; } = new $
                }
            }
            """);

        Assert.Equal("List<string>", expected);
    }

    [Fact]
    public async Task PropertyInitialiser_OffersTheDeclaredType()
    {
        var items = await CompleteAsync("""
            namespace T
            {
                public class Widget
                {
                    public System.Collections.Generic.List<string> Names { get; set; } = new $
                }
            }
            """);

        Assert.Contains("List<string>", items);
    }

    [Fact]
    public async Task LocalDeclaration_StillKnowsItsType()
    {
        // The path that already worked, kept honest alongside the new one.
        var expected = await ExpectedTypeAsync(InMain("List<double> sizes = new $"));

        Assert.Equal("List<double>", expected);
    }
}
