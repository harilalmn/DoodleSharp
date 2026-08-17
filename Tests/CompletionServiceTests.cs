using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoodleSharp.Editor;
using DoodleSharp.Execution;

namespace DoodleSharp.Tests;

/// <summary>
/// Tests for what the completion list contains — the filtering rules that decide whether a member
/// the user is looking for actually shows up.
/// </summary>
public class CompletionServiceTests
{
    private static RoslynCompletionService NewService() =>
        new RoslynCompletionService(new ModuleCompiler().GetReferences());

    /// <summary>Completes at the position marked by <c>$</c> in <paramref name="markedCode"/>.</summary>
    private static async Task<List<string>> CompleteAsync(string markedCode)
    {
        var position = markedCode.IndexOf('$');
        Assert.True(position >= 0, "Test source must contain a '$' caret marker");
        var code = markedCode.Remove(position, 1);

        var (completions, _, _, _) = await NewService().GetCompletionsAsync(code, position);
        return completions.Select(c => c.Text).ToList();
    }

    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using C2VGeometry;

        namespace T
        {
            public static class Helper
            {
                public static int StaticValue;
                public static void StaticMethod() { }
            }

            public class Widget
            {
                public int Instance;
                public void InstanceMethod() { }
                public static void WidgetStatic() { }
            }

            public class Viz
            {
                public static void Main()
                {
        """;

    private const string Postamble = """
                }
            }
        }
        """;

    private static string InMain(string body) => $"{Preamble}\n            {body}\n{Postamble}";

    [Fact]
    public async Task ObjectMembers_AreOfferedOnMemberAccess()
    {
        // ToString/GetType/Equals used to be filtered out of every list, on every type, including
        // where they were overridden. Users read that as broken IntelliSense.
        var items = await CompleteAsync(InMain("var w = new Widget(); w.$"));

        Assert.Contains("ToString", items);
        Assert.Contains("GetType", items);
        Assert.Contains("Equals", items);
    }

    [Fact]
    public async Task InstanceAccess_HidesStaticMembers()
    {
        var items = await CompleteAsync(InMain("var w = new Widget(); w.$"));

        Assert.Contains("InstanceMethod", items);
        Assert.Contains("Instance", items);
        // Not callable through an instance, so listing it only produces code that will not compile.
        Assert.DoesNotContain("WidgetStatic", items);
    }

    [Fact]
    public async Task StaticAccess_ShowsOnlyStaticMembers()
    {
        var items = await CompleteAsync(InMain("Helper.$"));

        Assert.Contains("StaticMethod", items);
        Assert.Contains("StaticValue", items);
    }

    [Fact]
    public async Task StaticAccess_OnATypeInAnotherPartOfTheProject_ListsItsMembers()
    {
        // The cross-file case behind "VectorManager. shows nothing".
        var other = """
            namespace T
            {
                public static class VectorManager
                {
                    public static void DrawVector(double x) { }
                }
            }
            """;

        var code = InMain("VectorManager.$");
        var position = code.IndexOf('$');
        var (completions, _, _, _) = await NewService()
            .GetCompletionsAsync(code.Remove(position, 1), position, new[] { other });

        Assert.Contains("DrawVector", completions.Select(c => c.Text));
    }

    [Fact]
    public async Task UnresolvableReceiver_ReturnsNothingRatherThanTheGlobalList()
    {
        // Falling back to the global lookup after a dot filled the list with locals and keywords
        // that are not members of anything — worse than showing nothing.
        var items = await CompleteAsync(InMain("NoSuchThing.$"));

        Assert.Empty(items);
    }

    [Fact]
    public async Task TypeParameters_AreOfferedInsideAGenericMethod()
    {
        var code = """
            namespace T
            {
                public class G
                {
                    public static void Do<TItem>()
                    {
                        TIt$
                    }
                }
            }
            """;

        var items = await CompleteAsync(code);
        Assert.Contains("TItem", items);
    }

    [Fact]
    public async Task GlobalCompletion_StillListsLocalsAndTypes()
    {
        var items = await CompleteAsync(InMain("var counter = 1; cou$"));

        Assert.Contains("counter", items);
    }

    // ── Reported from the live app ──────────────────────────────────────────

    [Fact]
    public async Task AllUppercaseGeometryTypesAreOffered()
    {
        // VXYZ is the core coordinate type, and it is spelled in all caps. A "hide all-uppercase
        // names" declutter rule aimed at interop types (ABI, MS) was removing it from every list,
        // so `VXYZ p = new VX` offered VXLine and nothing else.
        var afterNew = await CompleteAsync(InMain("VXYZ point = new $"));
        Assert.Contains("VXYZ", afterNew);

        var partial = await CompleteAsync(InMain("VXYZ point = new VX$"));
        Assert.Contains("VXYZ", partial);
        Assert.Contains("VXLine", partial);
    }

    [Fact]
    public async Task ExpectedTypeIsReportedForNewExpressions()
    {
        var marked = InMain("VXYZ point = new $");
        var position = marked.IndexOf('$');

        var (items, isAfterNew, _, expectedType) =
            await NewService().GetCompletionsAsync(marked.Remove(position, 1), position);

        Assert.True(isAfterNew);
        Assert.Equal("VXYZ", expectedType);
        // The host ranks the expected type first; it can only do that if it is in the list.
        Assert.Contains(items, i => i.Text == "VXYZ");
    }

    [Theory]
    [InlineData("VXYZ poin$")]                  // naming a local
    [InlineData("List<double> parame$")]        // naming a local of a generic type
    [InlineData("foreach (var ite$")]           // naming a foreach element
    public async Task NoSuggestionsWhileNamingSomethingNew(string body)
    {
        // The identifier being typed is the user's own choice. Offering existing symbols is noise,
        // and a commit character would replace what they were halfway through typing.
        Assert.Empty(await CompleteAsync(InMain(body)));
    }

    [Fact]
    public async Task SuggestionsStillAppearWhenReferencingAnExistingName()
    {
        // The guard above must not swallow ordinary identifier completion.
        var items = await CompleteAsync(InMain("var counter = 1;\n            coun$"));
        Assert.Contains("counter", items);
    }

    [Fact]
    public async Task KeywordsAreOffered()
    {
        // Without keywords in the list, typing `int` fuzzy-matched type names and ranked
        // IntersectionResult first — one commit character away from being inserted.
        var items = await CompleteAsync(InMain("for (int$"));
        Assert.Contains("int", items);

        var statement = await CompleteAsync(InMain("for$"));
        Assert.Contains("for", statement);
        Assert.Contains("foreach", statement);
    }

    [Fact]
    public async Task KeywordsAreNotOfferedAfterADotOrNew()
    {
        var afterDot = await CompleteAsync(InMain("var w = new Widget(); w.$"));
        Assert.DoesNotContain("for", afterDot);
        Assert.DoesNotContain("int", afterDot);

        var afterNew = await CompleteAsync(InMain("VXYZ p = new $"));
        Assert.DoesNotContain("for", afterNew);
    }

    [Fact]
    public async Task MemberAccessWorksWhenTheNextStatementFollowsTheDot()
    {
        // The reported "no IntelliSense, not even with Ctrl+Space" case. A dot at the end of a line
        // makes the parser read `circle.` plus the FOLLOWING statement as one qualified name
        // (`circle.Animation`), and in that shape Roslyn reports no symbol for the receiver and
        // GetTypeInfo returns an ERROR type — non-null, so "did it bind?" answered yes and the
        // member lookup ran against a type with no members.
        var code = """
            using System;
            using C2VGeometry;
            using DoodleSharp.Animation;

            namespace T
            {
                public class Viz
                {
                    public static void Main()
                    {
                        VCircle circle = new VCircle(new VXYZ(0, 0), 10);
                        circle.$
                        Animation a = new ValueAnimation<VCircle>(circle, c => c.Radius, 1, 10, 0.5);
                    }
                }
            }
            """;

        var items = await CompleteAsync(code);

        Assert.Contains("Radius", items);
        Assert.Contains("Center", items);
    }

    [Fact]
    public async Task MemberAccessStillWorksAsTheLastStatement()
    {
        // The shape that always worked — kept so a fix for the case above cannot break it.
        var items = await CompleteAsync(InMain("var w = new Widget();\n            w.$"));

        Assert.Contains("Instance", items);
        Assert.Contains("InstanceMethod", items);
    }

    [Fact]
    public async Task MemberAccessOnAnUnknownReceiverStillOffersNothing()
    {
        // The receiver fallback must not resurrect the old "fall back to the global list" behaviour.
        Assert.Empty(await CompleteAsync(InMain("NoSuchThing.$")));
    }

    [Fact]
    public async Task SignatureHelpListsEveryOverloadOfAResolvedCall()
    {
        // A call that already binds used to report only the overload the compiler picked, so
        // `new VXYZ()` showed one signature and no way to see the others.
        var marked = InMain("var p = new VXYZ($);");
        var position = marked.IndexOf('$');

        var (signatures, _) = await NewService().GetSignatureHelpAsync(marked.Remove(position, 1), position);

        Assert.True(signatures.Count > 1,
            $"expected several VXYZ constructor overloads, got: {string.Join(" | ", signatures)}");
        Assert.Contains(signatures, s => s.Contains("double") && s.Contains("x"));
    }
}
