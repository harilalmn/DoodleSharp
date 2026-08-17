using System;
using System.Linq;
using C2VGeometry;
using Xunit;

namespace DoodleSharp.Tests;

// GlobalParameters is a process-wide static registry, so these tests share the serialized
// "CanvasState" collection (see CanvasStateCollection.cs) rather than racing each other.
[Collection("CanvasState")]
public class GlobalParametersTests : IDisposable
{
    public GlobalParametersTests() => GlobalParameters.ClearAll();
    public void Dispose() => GlobalParameters.ClearAll();

    // ── Storage and typed reads ──────────────────────────────────────────────

    [Fact]
    public void Set_ThenGet_RoundTripsEachSupportedKind()
    {
        GlobalParameters.Set<double>("Length", 10);
        GlobalParameters.Set<bool>("Broken", true);
        GlobalParameters.Set<string>("Label", "String-A");
        var when = new DateTime(2026, 8, 3, 9, 30, 0);
        GlobalParameters.Set<DateTime>("Stamp", when);

        Assert.Equal(10.0, GlobalParameters.Get<double>("Length"));
        Assert.True(GlobalParameters.Get<bool>("Broken"));
        Assert.Equal("String-A", GlobalParameters.Get<string>("Label"));
        Assert.Equal(when, GlobalParameters.Get<DateTime>("Stamp"));

        Assert.Equal(ParamKind.Number, GlobalParameters.Find("Length")!.Kind);
        Assert.Equal(ParamKind.Boolean, GlobalParameters.Find("Broken")!.Kind);
        Assert.Equal(ParamKind.Text, GlobalParameters.Find("Label")!.Kind);
        Assert.Equal(ParamKind.Date, GlobalParameters.Find("Stamp")!.Kind);
    }

    [Fact]
    public void IntegerLiteral_IsStoredAsDouble()
    {
        GlobalParameters.Set("Count", 7);
        Assert.Equal(ParamKind.Number, GlobalParameters.Find("Count")!.Kind);
        Assert.Equal(7.0, GlobalParameters.Get<double>("Count"));
        Assert.Equal(7, (int)GlobalParameters.Get("Count"));
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        GlobalParameters.Set<double>("String Length", 10);
        Assert.Equal(10.0, GlobalParameters.Get("string length").Num);
        Assert.True(GlobalParameters.Has("STRING LENGTH"));
    }

    // ── The use-site ergonomics that motivated ParamValue ────────────────────

    [Fact]
    public void Get_ImplicitlyConvertsInArithmetic()
    {
        GlobalParameters.Set<double>("String Length", 10);

        double halfLength = GlobalParameters.Get("String Length") * 0.5;

        Assert.Equal(5.0, halfLength);
    }

    [Fact]
    public void Get_ImplicitlyConvertsInTernaryCondition()
    {
        GlobalParameters.Set<bool>("String Broken", true);

        string status = GlobalParameters.Get("String Broken") ? " " : " not ";

        Assert.Equal(" ", status);
    }

    [Fact]
    public void Get_ImplicitlyConvertsToStringAndDateTime()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5);
        GlobalParameters.Set<string>("Name", "String-A");
        GlobalParameters.Set<DateTime>("Stamp", when);

        string name = GlobalParameters.Get("Name");
        DateTime stamp = GlobalParameters.Get("Stamp");

        Assert.Equal("String-A", name);
        Assert.Equal(when, stamp);
    }

    [Fact]
    public void Get_OnUndeclaredParameter_ThrowsWithActionableMessage()
    {
        GlobalParameters.Set<double>("String Length", 10);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            double _ = GlobalParameters.Get("Strng Length") * 2;
        });

        Assert.Contains("has not been declared", ex.Message);
        Assert.Contains("'String Length'", ex.Message);   // lists what does exist
    }

    [Fact]
    public void Get_WrongType_ThrowsDescribingBothTypes()
    {
        GlobalParameters.Set<string>("Label", "abc");

        var ex = Assert.Throws<InvalidOperationException>(() => GlobalParameters.Get<bool>("Label"));

        Assert.Contains("String", ex.Message);
        Assert.Contains("Boolean", ex.Message);
    }

    [Fact]
    public void Get_WithFallback_DoesNotThrowForUndeclared()
    {
        Assert.Equal(42.0, GlobalParameters.Get("Missing", 42.0));
    }

    [Fact]
    public void UserDefinedTypes_AreRejected()
    {
        // Storing an instance from the user assembly would pin its collectible load context.
        var ex = Assert.Throws<ArgumentException>(() => GlobalParameters.Set("Bad", new object()));
        Assert.Contains("cannot hold", ex.Message);
    }

    // ── Declare-vs-override semantics ────────────────────────────────────────

    [Fact]
    public void Redeclaring_WithUnchangedDefault_PreservesOverride()
    {
        GlobalParameters.Set<double>("Length", 10);
        GlobalParameters.Assign("Length", 33.0);        // user drags the slider

        GlobalParameters.Set<double>("Length", 10);     // code re-runs

        Assert.Equal(33.0, GlobalParameters.Get<double>("Length"));
        Assert.True(GlobalParameters.Find("Length")!.IsOverridden);
    }

    [Fact]
    public void Redeclaring_WithChangedDefault_WinsOverOverride()
    {
        GlobalParameters.Set<double>("Length", 10);
        GlobalParameters.Assign("Length", 33.0);

        GlobalParameters.Set<double>("Length", 25);     // user edited the literal in code

        Assert.Equal(25.0, GlobalParameters.Get<double>("Length"));
        Assert.False(GlobalParameters.Find("Length")!.IsOverridden);
    }

    [Fact]
    public void Reset_RestoresDeclaredDefault()
    {
        GlobalParameters.Set<double>("Length", 10);
        GlobalParameters.Assign("Length", 33.0);

        GlobalParameters.Reset("Length");

        Assert.Equal(10.0, GlobalParameters.Get<double>("Length"));
        Assert.False(GlobalParameters.Find("Length")!.IsOverridden);
    }

    [Fact]
    public void PinnedRange_SurvivesRedeclaration()
    {
        GlobalParameters.Set<double>("Length", 10, min: 0, max: 20);
        GlobalParameters.SetRange("Length", -100, 100);

        GlobalParameters.Set<double>("Length", 10, min: 0, max: 20);

        Assert.Equal(-100, GlobalParameters.Find("Length")!.EffectiveMin);
        Assert.Equal(100, GlobalParameters.Find("Length")!.EffectiveMax);
    }

    [Fact]
    public void EffectiveRange_IsDerivedWhenNotDeclared()
    {
        GlobalParameters.Set<double>("Length", 10);

        var p = GlobalParameters.Find("Length")!;
        Assert.Equal(0, p.EffectiveMin);
        Assert.Equal(20, p.EffectiveMax);
    }

    // ── Run lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public void ChangeNotifications_AreSuppressedDuringARun()
    {
        // Without this, every Set(...) inside Main() would raise Changed, which re-runs Main(),
        // which raises again — an unbounded loop.
        int changes = 0;
        void Handler(Parameter _) => changes++;
        GlobalParameters.Changed += Handler;
        try
        {
            GlobalParameters.BeginRun();
            GlobalParameters.Set<double>("Length", 10);
            GlobalParameters.Assign("Length", 12.0);
            GlobalParameters.EndRun(pruneStale: true);

            Assert.Equal(0, changes);

            GlobalParameters.Assign("Length", 14.0);
            Assert.Equal(1, changes);
        }
        finally { GlobalParameters.Changed -= Handler; }
    }

    [Fact]
    public void EndRun_PrunesParametersNoLongerDeclared()
    {
        GlobalParameters.BeginRun();
        GlobalParameters.Set<double>("Kept", 1);
        GlobalParameters.Set<double>("Removed", 2);
        GlobalParameters.EndRun(pruneStale: true);

        // Second run: the "Removed" declaration was deleted from the code.
        GlobalParameters.BeginRun();
        GlobalParameters.Set<double>("Kept", 1);
        GlobalParameters.EndRun(pruneStale: true);

        Assert.True(GlobalParameters.Has("Kept"));
        Assert.False(GlobalParameters.Has("Removed"));
    }

    [Fact]
    public void EndRun_WithoutPruning_KeepsEverything()
    {
        GlobalParameters.BeginRun();
        GlobalParameters.Set<double>("A", 1);
        GlobalParameters.Set<double>("B", 2);
        GlobalParameters.EndRun(pruneStale: true);

        // A run that failed part-way must not blank the panel.
        GlobalParameters.BeginRun();
        GlobalParameters.Set<double>("A", 1);
        GlobalParameters.EndRun(pruneStale: false);

        Assert.True(GlobalParameters.Has("A"));
        Assert.True(GlobalParameters.Has("B"));
    }

    [Fact]
    public void All_IsInDeclarationOrder()
    {
        GlobalParameters.BeginRun();
        GlobalParameters.Set<double>("Third", 3);
        GlobalParameters.Set<double>("First", 1);
        GlobalParameters.Set<double>("Second", 2);
        GlobalParameters.EndRun(pruneStale: true);

        Assert.Equal(new[] { "Third", "First", "Second" },
            GlobalParameters.All.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void CallerInfo_RecordsTheDeclarationSite()
    {
        GlobalParameters.Set<double>("Length", 10);

        var p = GlobalParameters.Find("Length")!;
        Assert.EndsWith("GlobalParametersTests.cs", p.SourceFile);
        Assert.True(p.SourceLine > 0);
    }
}
