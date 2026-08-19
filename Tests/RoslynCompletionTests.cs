using Xunit;
using DoodleSharp.Editor;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DoodleSharp.Tests;

public class RoslynCompletionTests
{
    [Fact]
    public async Task GetCompletions_ShouldReturnLocalVariables()
    {
        var code = @"
using System;
class Test {
    void Method() {
        int myVar = 10;
        my//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        Assert.Contains(completions, c => c.Text == "myVar");
    }

    [Fact]
    public async Task GetCompletions_ShouldReturnMembers()
    {
        var code = @"
using System;
class Test {
    void Method() {
        string s = ""hello"";
        s.//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
        };
        var service = new RoslynCompletionService(references);

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        Assert.Contains(completions, c => c.Text == "Length");
        Assert.Contains(completions, c => c.Text == "Substring");
    }

    [Fact]
    public async Task GetCompletions_MemberAccess_InsideIncompleteForeachIn()
    {
        // Repro: `foreach (var vertex in pol.)` — member access on `pol` where the
        // type is not enumerable (so the foreach itself is an error) AND the dot is
        // immediately followed by `)`. Completion should still list pol's members.
        var code = @"
using System;
public class VPolygon { public System.Collections.Generic.List<int> Points { get; set; } public System.Collections.Generic.List<int> Vertices { get; set; } }
class MySketch {
    VPolygon pol = new VPolygon();
    void Draw() {
        foreach (var vertex in pol.)
        {
        }
    }
}";
        var position = code.IndexOf("pol.") + "pol.".Length;
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        Assert.Contains(completions, c => c.Text == "Points");
        Assert.Contains(completions, c => c.Text == "Vertices");
    }

    [Fact]
    public async Task GetCompletions_MemberAccess_RealVPolygon_ForeachIn()
    {
        // Faithful repro of the reported case: real C2VGeometry.VPolygon, injected
        // global usings (incl. `global using C2VGeometry;`), workspace overload, and the
        // dot inside an incomplete `foreach (var vertex in pol.)`.
        var trusted = ((string?)System.AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(System.IO.Path.PathSeparator)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        trusted.Add(MetadataReference.CreateFromFile(typeof(C2VGeometry.Shape).Assembly.Location));

        var workspace = new CachedCompilationWorkspace(trusted);
        workspace.UpdateFile("_GlobalUsings.g.cs",
            "global using System;\nglobal using System.Linq;\nglobal using System.Collections.Generic;\nglobal using C2VGeometry;\n");

        var code = @"public class MySketch
{
    VPolygon pol = new VPolygon(new VXYZ(0,0), new VXYZ(1,0), new VXYZ(1,1));
    public void Draw()
    {
        foreach (var vertex in pol.)
        {
        }
    }
}";
        var position = code.IndexOf("pol.", code.IndexOf("foreach")) + "pol.".Length;
        workspace.UpdateFile("Sketch.cs", code);

        var service = new RoslynCompletionService(workspace);
        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position, workspace, "Sketch.cs");

        Assert.Contains(completions, c => c.Text == "Points");
        Assert.Contains(completions, c => c.Text == "Vertices");
    }

    [Fact]
    public async Task GetCompletions_ShouldReturnExpectedType_Assignment()
    {
        var code = @"
using System;
public class VPoint { }
class Test {
    void Method() {
        VPoint p = //CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, expectedType) = await service.GetCompletionsAsync(code, position);

        Assert.Equal("VPoint", expectedType);
        // We expect VPoint to be prioritized or explicitly available
        Assert.Contains(completions, c => c.Text == "VPoint");
    }

    [Fact]
    public async Task GetCompletions_ShouldReturnExpectedType_MethodArg()
    {
        var code = @"
using System;
public class VPoint { }
class Test {
    void Draw(VPoint p) { }
    void Method() {
        Draw(//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, expectedType) = await service.GetCompletionsAsync(code, position);

        Assert.Equal("VPoint", expectedType);
        Assert.Contains(completions, c => c.Text == "VPoint");
    }

    [Fact]
    public async Task GetCompletions_ShouldHideIrrelevantTypes()
    {
        var code = @"
using System;
class Test {
    void Method() {
        System.Runtime.//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        // Must include references to see system types
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)
        };
        var service = new RoslynCompletionService(references);

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        // Should NOT contain low-level runtime types like GCSettings if we filter them
        Assert.DoesNotContain(completions, c => c.Text == "GCSettings");
    }
    [Fact]
    public async Task GetCompletions_ShouldHideIrrelevantNamespacesAndStructs()
    {
        var code = @"
using System;
namespace MS { public class Internal {} }
namespace ABI { public class Internal {} }
class Test {
    void Method() {
        //CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        // These should be hidden by our filter
        Assert.DoesNotContain(completions, c => c.Text == "MS");
        Assert.DoesNotContain(completions, c => c.Text == "ABI");
        Assert.DoesNotContain(completions, c => c.Text == "Void"); // System.Void

        // This should be present because of using System;
        Assert.Contains(completions, c => c.Text == "Math");
    }

    [Fact]
    public async Task GetCompletions_ShouldHidePrimitivesAndSystemTypes()
    {
        var code = @"
using System;
class Test {
    void Method() {
        //CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        // Primitives (should be hidden in favor of keywords or just noise reduction)
        Assert.DoesNotContain(completions, c => c.Text == "Byte");
        Assert.DoesNotContain(completions, c => c.Text == "Int32");
        Assert.DoesNotContain(completions, c => c.Text == "String");
        Assert.DoesNotContain(completions, c => c.Text == "Boolean");
        Assert.DoesNotContain(completions, c => c.Text == "Single");
        Assert.DoesNotContain(completions, c => c.Text == "Double");

        // System Types
        Assert.DoesNotContain(completions, c => c.Text == "Guid");
        Assert.DoesNotContain(completions, c => c.Text == "Type");
        Assert.DoesNotContain(completions, c => c.Text == "Array");
        Assert.DoesNotContain(completions, c => c.Text == "Exception");
        Assert.DoesNotContain(completions, c => c.Text == "Attribute");
        
        // Context-less delegates
        Assert.DoesNotContain(completions, c => c.Text == "Func");
        Assert.DoesNotContain(completions, c => c.Text == "Action");
        
        // Check random one from screenshot
        Assert.DoesNotContain(completions, c => c.Text == "Char");
    }

    // ---- Phase 1: CachedCompilationWorkspace Tests ----

    [Fact]
    public async Task CachedWorkspace_ShouldReturnCompletions()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var workspace = new CachedCompilationWorkspace(references);

        var code = @"
using System;
class Test {
    void Method() {
        int myVar = 10;
        my//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        workspace.UpdateFile("test.cs", code);

        var service = new RoslynCompletionService(workspace);
        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position, workspace, "test.cs");

        Assert.Contains(completions, c => c.Text == "myVar");
    }

    [Fact]
    public void CachedWorkspace_ShouldSupportIncrementalUpdates()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var workspace = new CachedCompilationWorkspace(references);

        workspace.UpdateFile("file1.cs", "class A { }");
        workspace.UpdateFile("file2.cs", "class B : A { }");

        // Counted excluding the synthetic global-using tree the workspace also carries, so this
        // keeps measuring what it is about — the user's files — rather than the tree total.
        Assert.Equal(2, UserTrees(workspace));

        // Update file1 - should replace, not add
        workspace.UpdateFile("file1.cs", "class A { int X; }");
        Assert.Equal(2, UserTrees(workspace));
    }

    [Fact]
    public void CachedWorkspace_RemoveFile_ShouldWork()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };
        var workspace = new CachedCompilationWorkspace(references);

        workspace.UpdateFile("a.cs", "class A {}");
        workspace.UpdateFile("b.cs", "class B {}");
        Assert.Equal(2, UserTrees(workspace));

        workspace.RemoveFile("a.cs");
        Assert.Equal(1, UserTrees(workspace));
    }

    /// <summary>
    /// The workspace's trees minus the synthetic global-using one it always carries (so that
    /// <c>Viewports</c> resolves in IntelliSense the same way it does at compile time).
    /// </summary>
    private static int UserTrees(CachedCompilationWorkspace workspace) =>
        workspace.GetCompilation().SyntaxTrees
            .Count(t => t.FilePath != DoodleSharp.Execution.SyntheticUsings.FilePath);

    // ---- Phase 2: FuzzyMatcher Tests ----

    [Fact]
    public void FuzzyMatcher_ExactMatch_ReturnsHighScore()
    {
        var score = FuzzyMatcher.Score("color", "color");
        Assert.NotNull(score);
        Assert.True(score > 50); // Exact match should be very high
    }

    [Fact]
    public void FuzzyMatcher_PrefixMatch_ReturnsHighScore()
    {
        var score = FuzzyMatcher.Score("col", "color");
        Assert.NotNull(score);
        Assert.True(score > 20); // Prefix match should be high
    }

    [Fact]
    public void FuzzyMatcher_SubsequenceMatch_ReturnsScore()
    {
        var score = FuzzyMatcher.Score("clr", "color");
        Assert.NotNull(score); // c-l-o-r: c matches c, l matches l, r matches r
    }

    [Fact]
    public void FuzzyMatcher_CamelCaseMatch_ReturnsScore()
    {
        var score = FuzzyMatcher.Score("VPt", "VPoint");
        Assert.NotNull(score);
        Assert.True(score > 10);
    }

    [Fact]
    public void FuzzyMatcher_NoMatch_ReturnsNull()
    {
        var score = FuzzyMatcher.Score("xyz", "color");
        Assert.Null(score);
    }

    [Fact]
    public void FuzzyMatcher_EmptyPattern_MatchesEverything()
    {
        var score = FuzzyMatcher.Score("", "anything");
        Assert.Equal(0, score);
    }

    [Fact]
    public void FuzzyMatcher_GetMatchPositions_ReturnsCorrectIndices()
    {
        var positions = FuzzyMatcher.GetMatchPositions("VPt", "VPoint");
        Assert.NotNull(positions);
        Assert.Contains(0, positions); // V at index 0
    }

    [Fact]
    public void FuzzyMatcher_PrefixBetterThanSubsequence()
    {
        var prefixScore = FuzzyMatcher.Score("col", "color");
        var subseqScore = FuzzyMatcher.Score("col", "camelObject_label"); // c...o...l subsequence

        Assert.NotNull(prefixScore);
        Assert.NotNull(subseqScore);
        Assert.True(prefixScore > subseqScore);
    }

    // ---- Phase 3: Context Detection Tests ----

    [Fact]
    public async Task IsInGenericTypeArgument_DetectsGenericContext()
    {
        var code = "List</*CURSOR*/> x;";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();
        var pos = code.IndexOf("/*CURSOR*/");

        Assert.True(RoslynCompletionService.IsInGenericTypeArgument(root, pos));
    }

    [Fact]
    public async Task IsInObjectInitializer_DetectsInitializerContext()
    {
        var code = @"
class Point { public int X { get; set; } public int Y { get; set; } }
class Test {
    void M() {
        var p = new Point { /*CURSOR*/ };
    }
}";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();
        var pos = code.IndexOf("/*CURSOR*/");

        Assert.True(RoslynCompletionService.IsInObjectInitializer(root, pos));
    }

    [Fact]
    public async Task IsInAttributeContext_DetectsAttributeContext()
    {
        var code = @"
[/*CURSOR*/]
class Test { }";
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = await tree.GetRootAsync();
        var pos = code.IndexOf("/*CURSOR*/");

        Assert.True(RoslynCompletionService.IsInAttributeContext(root, pos));
    }

    // ---- Phase 5: Scope Priority Tests ----

    [Fact]
    public async Task GetCompletions_ShouldTagLocalScope()
    {
        var code = @"
using System;
class Test {
    void Method() {
        int localVar = 10;
        local//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        var localVarItem = completions.OfType<CompletionData>().FirstOrDefault(c => c.Text == "localVar");
        Assert.NotNull(localVarItem);
        Assert.Equal(SymbolScope.Local, localVarItem.Scope);
    }

    [Fact]
    public async Task GetCompletions_ShouldStoreSymbol()
    {
        var code = @"
using System;
class Test {
    void Method() {
        int myVar = 10;
        my//CURSOR
    }
}";
        var position = code.IndexOf("//CURSOR");
        var service = new RoslynCompletionService();

        var (completions, _, _, _) = await service.GetCompletionsAsync(code, position);

        var item = completions.OfType<CompletionData>().FirstOrDefault(c => c.Text == "myVar");
        Assert.NotNull(item);
        Assert.NotNull(item.Symbol); // Symbol should be stored for documentation sidecar
    }
}
