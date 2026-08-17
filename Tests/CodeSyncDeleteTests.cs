using System.Linq;
using C2VGeometry;
using DoodleSharp.Canvas;

namespace DoodleSharp.Tests;

/// <summary>
/// Deleting a shape on the canvas must delete its code.
///
/// <para>
/// The declaration used to be located with <c>new VRay\([^)]*\)</c> — an argument list of
/// "anything that isn't a closing parenthesis". That cannot span a nested call, so
/// <c>new VRay(p1, new VXYZ(1, 2))</c> never matched: the shape vanished from the canvas, the
/// status bar said "Deleted 1 shape", and the code was left untouched. Nested constructor
/// arguments are the norm in this API, not an edge case.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class CodeSyncDeleteTests
{
    private const string Header = """
        using C2VGeometry;

        namespace TestBed
        {
            public class Viz
            {
                public static void Main()
                {
        """;

    private const string Footer = """
                }
            }
        }
        """;

    private static string Body(params string[] lines) =>
        Header + "\n" + string.Join("\n", lines.Select(l => "            " + l)) + "\n" + Footer;

    [Fact]
    public void RemovesADeclarationWithNestedConstructorArguments()
    {
        var code = Body(
            "VXYZ p1 = new VXYZ(10, 20);",
            "VRay ray = new VRay(p1, new VXYZ(1, 2));");

        var ray = new VRay(new VXYZ(10, 20), new VXYZ(1, 2)) { Name = "ray" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, ray);

        Assert.True(found, "the declaration should have been located");
        Assert.DoesNotContain("new VRay", result);
        Assert.Contains("VXYZ p1", result);   // the other statement survives
    }

    [Fact]
    public void RemovesASimpleDeclaration()
    {
        var code = Body("VCircle c = new VCircle(0, 0, 10);");

        var circle = new VCircle(0, 0, 10) { Name = "c" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, circle);

        Assert.True(found);
        Assert.DoesNotContain("new VCircle", result);
    }

    [Fact]
    public void RemovesTheFollowingPropertyAssignments()
    {
        var code = Body(
            "VCircle c = new VCircle(new VXYZ(0, 0), 10);",
            "c.Color = \"Red\";",
            "c.LineWeight = 2;",
            "VXYZ keep = new VXYZ(1, 1);");

        var circle = new VCircle(new VXYZ(0, 0), 10) { Name = "c" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, circle);

        Assert.True(found);
        Assert.DoesNotContain("new VCircle", result);
        Assert.DoesNotContain("c.Color", result);
        Assert.DoesNotContain("c.LineWeight", result);
        Assert.Contains("VXYZ keep", result);
    }

    [Fact]
    public void RemovesADeclarationWithAnObjectInitializer()
    {
        var code = Body("VCircle c = new VCircle(new VXYZ(0, 0), 10) { Color = \"Red\" };");

        var circle = new VCircle(new VXYZ(0, 0), 10) { Name = "c" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, circle);

        Assert.True(found);
        Assert.DoesNotContain("new VCircle", result);
    }

    [Fact]
    public void ASemicolonInsideAStringDoesNotEndTheStatement()
    {
        var code = Body(
            "VText t = new VText(new VXYZ(0, 0), \"a; b\", 12);",
            "VXYZ keep = new VXYZ(1, 1);");

        var text = new VText(new VXYZ(0, 0), "a; b", 12) { Name = "t" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, text);

        Assert.True(found);
        Assert.DoesNotContain("new VText", result);
        Assert.Contains("VXYZ keep", result);   // not truncated mid-statement
    }

    [Fact]
    public void LeavesUnrelatedCodeAloneWhenTheShapeIsNotFound()
    {
        var code = Body("VCircle c = new VCircle(0, 0, 10);");

        var other = new VLine(0, 0, 5, 5) { Name = "notPresent" };
        var (result, found) = CodeSyncManager.RemoveShapeCode(code, other);

        Assert.False(found);
        Assert.Equal(code, result);
    }

    // ── Planning a deletion across the whole project ────────────────────────
    //
    // A shape can be declared in any module, so the delete has to search every file. Capturing
    // before/after per file is also what makes the deletion undoable in one step.

    [Fact]
    public void PlanDeletionFindsAShapeDeclaredInANonEntryFile()
    {
        var main = Body("VCircle c = new VCircle(0, 0, 10);");
        var helper = Body("VLine tail = new VLine(0, 0, 5, 5);");

        var line = new VLine(0, 0, 5, 5) { Name = "tail" };
        var (edits, notFound) = CodeSyncManager.PlanDeletion(
            new[] { ("Main.cs", main), ("Helper.cs", helper) }, new[] { (Shape)line });

        Assert.Empty(notFound);
        var edit = Assert.Single(edits);
        Assert.Equal("Helper.cs", edit.File);
        Assert.DoesNotContain("new VLine", edit.After);
        Assert.Contains("new VLine", edit.Before);   // the "before" is the original text
    }

    [Fact]
    public void PlanDeletionSpansSeveralFilesAtOnce()
    {
        var main = Body("VCircle c = new VCircle(0, 0, 10);");
        var helper = Body("VLine tail = new VLine(0, 0, 5, 5);");

        var circle = new VCircle(0, 0, 10) { Name = "c" };
        var line = new VLine(0, 0, 5, 5) { Name = "tail" };

        var (edits, notFound) = CodeSyncManager.PlanDeletion(
            new[] { ("Main.cs", main), ("Helper.cs", helper) }, new Shape[] { circle, line });

        Assert.Empty(notFound);
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.NotEqual(e.Before, e.After));
    }

    [Fact]
    public void PlanDeletionReportsShapesItCouldNotFind()
    {
        // The case that used to be reported as a clean delete while the code stayed put.
        var main = Body("VCircle c = new VCircle(0, 0, 10);");

        var missing = new VLine(0, 0, 5, 5) { Name = "nowhere" };
        var (edits, notFound) = CodeSyncManager.PlanDeletion(
            new[] { ("Main.cs", main) }, new[] { (Shape)missing });

        Assert.Empty(edits);
        Assert.Single(notFound);
        Assert.Equal("nowhere", notFound[0].Name);
    }

    [Fact]
    public void PlanDeletionTouchesNothingWhenItFindsNothing()
    {
        var main = Body("VCircle c = new VCircle(0, 0, 10);");
        var missing = new VLine(0, 0, 5, 5) { Name = "nowhere" };

        var (edits, _) = CodeSyncManager.PlanDeletion(
            new[] { ("Main.cs", main) }, new[] { (Shape)missing });

        Assert.Empty(edits);   // no edit means undo has nothing to restore, which is correct
    }

    [Fact]
    public void PlanDeletionSkipsEmptyFiles()
    {
        var circle = new VCircle(0, 0, 10) { Name = "c" };
        var (edits, notFound) = CodeSyncManager.PlanDeletion(
            new[] { ("Empty.cs", ""), ("Main.cs", Body("VCircle c = new VCircle(0, 0, 10);")) },
            new[] { (Shape)circle });

        Assert.Empty(notFound);
        Assert.Equal("Main.cs", Assert.Single(edits).File);
    }

    [Fact]
    public void PlanDeletionStopsSearchingForAShapeOnceItIsFound()
    {
        // Two files declaring the same variable name: only the first should be edited, or deleting
        // one shape would silently gut an unrelated file that happens to reuse the name.
        var first = Body("VCircle c = new VCircle(0, 0, 10);");
        var second = Body("VCircle c = new VCircle(50, 50, 4);");

        var circle = new VCircle(0, 0, 10) { Name = "c" };
        var (edits, _) = CodeSyncManager.PlanDeletion(
            new[] { ("First.cs", first), ("Second.cs", second) }, new[] { (Shape)circle });

        Assert.Equal("First.cs", Assert.Single(edits).File);
    }
}
