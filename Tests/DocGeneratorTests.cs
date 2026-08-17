using System;
using System.Linq;
using DoodleSharp.Documentation;

namespace DoodleSharp.Tests;

/// <summary>
/// Guards the F1 Help window against the failure that has already shipped once: `DocGenerator`
/// builds its three lookup dictionaries with collection initialisers keyed by `type.Name`, and a
/// duplicate key throws <see cref="ArgumentException"/> straight out of the constructor — so the
/// Help window dies on open rather than showing a slightly wrong entry.
///
/// <para>
/// Documentation is edited far more often than it is exercised, and by more than one author at a
/// time, so this needs to fail in CI rather than in someone's Help window.
/// </para>
/// </summary>
public class DocGeneratorTests
{
    [Fact]
    public void ConstructsWithoutDuplicateKeys()
    {
        // The constructor populates every dictionary; a duplicate key throws here.
        var exception = Record.Exception(() => new DocGenerator());

        Assert.True(exception == null,
            "DocGenerator failed to construct — most likely a duplicate key in _summaries, " +
            $"_csharpSamples or _memberDescriptions. This crashes F1 Help on open. {exception?.Message}");
    }

    [Fact]
    public void DocumentsTheTypesUsersActuallyWriteAgainst()
    {
        var generator = new DocGenerator();
        var types = generator.GetDocumentableTypes();

        Assert.NotEmpty(types);

        // VXYZ especially: it is the coordinate type every example depends on, and it was recently
        // being filtered out of IntelliSense by an all-uppercase heuristic.
        var names = types.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[] { "VXYZ", "VCircle", "VLine", "VPolygon", "VText" })
            Assert.Contains(expected, names);
    }

    /// <summary>
    /// The member tables were reflected with <c>Public | Instance | DeclaredOnly</c>. Static was
    /// missing, so every static class rendered a page with no members at all — VColor, BooleanOps,
    /// Chart, GlobalParameters, ArrayOps, GeometryHelper, Frame, EasingFunctions and fifteen more —
    /// and the static factories on the shapes were invisible too. 339 member descriptions had
    /// already been written for members the reader could never reach.
    /// </summary>
    [Theory]
    [InlineData(typeof(C2VGeometry.VColor), "Crimson")]
    [InlineData(typeof(C2VGeometry.BooleanOps), "UnionAll")]
    [InlineData(typeof(C2VGeometry.Chart), "Scatter")]
    [InlineData(typeof(C2VGeometry.GlobalParameters), "SetRange")]
    [InlineData(typeof(C2VGeometry.ArrayOps), "CircularArray")]
    [InlineData(typeof(C2VGeometry.GeometryHelper), "RotatePoint")]
    [InlineData(typeof(C2VGeometry.DoubleExtensions), "ToRadians")]
    [InlineData(typeof(DoodleSharp.Animation.Frame), "Request")]
    [InlineData(typeof(DoodleSharp.Animation.EasingFunctions), "EaseInOutCubic")]
    // Static factories and statics declared on otherwise-instance types:
    [InlineData(typeof(C2VGeometry.VCircle), "FromCenterDiameter")]
    [InlineData(typeof(C2VGeometry.VArc), "FromStartCenterEnd")]
    [InlineData(typeof(C2VGeometry.VXYZ), "BasisX")]
    [InlineData(typeof(C2VGeometry.VTransform), "CreateRotationDegrees")]
    [InlineData(typeof(C2VGeometry.VRay), "AtAngle")]
    [InlineData(typeof(C2VGeometry.VXLine), "Horizontal")]
    public void StaticMembersAppearOnTheirPage(Type type, string expectedMember)
    {
        Assert.Contains(expectedMember, RenderedText(type));
    }

    /// <summary>
    /// An enum declares no properties or methods, so with only property/method tables an enum page
    /// listed nothing whatsoever — including ColorName's 83 colours and BuiltInHatch's 73 patterns,
    /// which are the two pages a user is most likely to open looking for a name to type.
    /// </summary>
    [Theory]
    [InlineData(typeof(C2VGeometry.ColorName), "Crimson")]
    [InlineData(typeof(C2VGeometry.BuiltInHatch), "ANSI31")]
    [InlineData(typeof(C2VGeometry.LineType), "DashDot")]
    [InlineData(typeof(C2VGeometry.VTextAnchor), "MiddleCenter")]
    [InlineData(typeof(C2VGeometry.ControlPointType), "CurveControl")]
    public void EnumValuesAppearOnTheirPage(Type type, string expectedValue)
    {
        Assert.Contains(expectedValue, RenderedText(type));
    }

    /// <summary>
    /// Public events had no section of their own, so none of them appeared on any page. The methods
    /// query drops their <c>add_</c>/<c>remove_</c> accessors as <c>IsSpecialName</c>, and nothing
    /// else looked at <c>GetEvents</c> — so five public events were unreachable in Help while four of
    /// them already had descriptions written. That is note 91's exact shape: the prose exists, so
    /// checking the dictionaries looks healthy and only the rendered page is missing them.
    /// </summary>
    [Theory]
    [InlineData(typeof(DoodleSharp.Animation.Mouse), "CallbackFailed")]
    [InlineData(typeof(DoodleSharp.Animation.Frame), "CallbackFailed")]
    [InlineData(typeof(C2VGeometry.GlobalParameters), "Changed")]
    [InlineData(typeof(C2VGeometry.GlobalParameters), "Reloaded")]
    public void PublicEventsAppearOnTheirPage(Type type, string expectedEvent)
    {
        var text = RenderedText(type);

        Assert.Contains("Events", text);
        Assert.Contains(expectedEvent, text);
    }

    /// <summary>
    /// A static event must say so, for the same reason a static method does: it changes how the
    /// member is reached. Staticness lives on the accessors, so the switch needs an EventInfo arm.
    /// </summary>
    [Fact]
    public void StaticEventsAreFlaggedStatic()
    {
        var callbackFailed = typeof(DoodleSharp.Animation.Mouse)
            .GetEvent("CallbackFailed", DocGenerator.MemberFlags);

        Assert.NotNull(callbackFailed);
        Assert.True(DocGenerator.IsStaticMember(callbackFailed!));
    }

    /// <summary>Constants are the whole public surface of GeometryTolerance's tolerance trio.</summary>
    [Fact]
    public void ConstantFieldsAppearOnTheirPage()
    {
        var text = RenderedText(typeof(C2VGeometry.GeometryTolerance));

        Assert.Contains("Epsilon", text);
        Assert.Contains("VisualEpsilon", text);
        Assert.Contains("AngleEpsilon", text);
    }

    /// <summary>
    /// Rendering a page correctly is worth nothing if the type never appears in the tree. The type
    /// filter was <c>IsClass || IsAbstract</c>, which covers classes and interfaces (an interface is
    /// abstract in metadata) but silently excluded **every enum and every struct** — so the enum-value
    /// rendering added alongside this test was unreachable in the actual Help window. The other tests
    /// here call <c>GenerateDocForType</c> directly and so passed throughout, which is exactly how the
    /// gap hid: reachability and rendering are separate failures and need separate assertions.
    /// </summary>
    [Theory]
    [InlineData(typeof(C2VGeometry.ColorName))]      // enum, 83 values
    [InlineData(typeof(C2VGeometry.BuiltInHatch))]   // enum, 73 values
    [InlineData(typeof(C2VGeometry.LineType))]
    [InlineData(typeof(C2VGeometry.VTextAnchor))]
    [InlineData(typeof(C2VGeometry.ParamValue))]     // struct
    [InlineData(typeof(C2VGeometry.RayHit))]         // struct
    [InlineData(typeof(C2VGeometry.VXYZ))]           // the coordinate type every example uses
    [InlineData(typeof(C2VGeometry.ICurve))]         // interface
    [InlineData(typeof(C2VGeometry.VColor))]         // static class
    public void EnumsAndStructsAreReachableInTheTree(Type expected)
    {
        var types = new DocGenerator().GetDocumentableTypes();

        Assert.Contains(expected, types);
    }

    private static string RenderedText(Type type)
    {
        var doc = new DocGenerator().GenerateDocForType(type);
        return new System.Windows.Documents.TextRange(doc.ContentStart, doc.ContentEnd).Text;
    }

    [Fact]
    public void EveryDocumentedTypeRendersWithoutThrowing()
    {
        // GenerateDocForType reads the sample and member dictionaries per type; a malformed or
        // mismatched entry surfaces here rather than when the user opens that page in Help.
        var generator = new DocGenerator();

        foreach (var type in generator.GetDocumentableTypes())
        {
            var exception = Record.Exception(() => generator.GenerateDocForType(type));
            Assert.True(exception == null, $"Help page for {type.Name} threw: {exception?.Message}");
        }
    }
}
