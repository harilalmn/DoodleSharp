using System;
using System.Collections.Generic;
using C2VGeometry;
using DoodleSharp.Canvas;
using Xunit;

namespace DoodleSharp.Tests;

/// <summary>
/// Contracts on the snapping API that are easy to break silently.
///
/// <para>
/// All three came out of a documentation pass: writing the F1 pages for <see cref="SnapEngine"/>
/// meant reading it closely for the first time, which is what surfaced a value that carries no
/// information, an enum member never returned, and a null argument answered with "no snap" rather
/// than an error.
/// </para>
/// </summary>
public class SnapEngineTests
{
    [Fact]
    public void NoneIsTheZeroValue()
    {
        // SnapType.None is never returned — FindSnapPoint reports "nothing to snap to" as null — so
        // it reads like dead weight. It is not: it pins the zero value, which is what makes an
        // unassigned SnapType field mean "no snap kind" instead of Endpoint. Removing it, or
        // reordering the enum so something else is 0, changes default(SnapType) silently.
        Assert.Equal(0, (int)SnapType.None);
        Assert.Equal(SnapType.None, default(SnapType));
        Assert.NotEqual(SnapType.Endpoint, default(SnapType));
    }

    [Fact]
    public void ANullSceneIndexIsAnError()
    {
        // It used to return null, which is indistinguishable from "nothing near the cursor" — so a
        // caller that passed no index lost snapping entirely, on every mouse move, with nothing to
        // notice. This overload has no shape source of its own and so cannot fall back to a scan;
        // the message names the overload that takes the shapes.
        var engine = new SnapEngine();

        var ex = Assert.Throws<ArgumentNullException>(
            () => engine.FindSnapPoint(new VXYZ(0, 0), (Rendering.SceneIndex)null!, 1.0));

        Assert.Contains("IReadOnlyList<IDrawable> overload", ex.Message);
    }

    [Fact]
    public void NothingNearTheCursorIsStillNullNotAnError()
    {
        // The list overload keeps the "no snap" contract: an empty scene is a normal answer.
        var engine = new SnapEngine();

        Assert.Null(engine.FindSnapPoint(new VXYZ(0, 0), new List<IDrawable>(), 1.0));
    }

    [Fact]
    public void ConstraintPointIsDeprecatedRatherThanDeleted()
    {
        // It is always exactly Point — the perpendicular's foot and the tangent's touch point ARE
        // the snap point — so it carries nothing Point does not. Deprecated, not removed, the same
        // call as VDimension.ExtensionLength (note 92): a warning naming the replacement beats
        // breaking every existing read. This test fails if someone drops the attribute or the
        // property.
        var property = typeof(SnapResult).GetProperty(nameof(SnapResult.ConstraintPoint));

        Assert.NotNull(property);
        var obsolete = (ObsoleteAttribute?)Attribute.GetCustomAttribute(property!, typeof(ObsoleteAttribute));

        Assert.NotNull(obsolete);
        Assert.Contains("Point", obsolete!.Message);
    }

    [Fact]
    public void AnEndpointSnapReportsTheEndpointItself()
    {
        // A behavioural anchor, so the contract tests above are not the only cover: a cursor near a
        // line's end snaps to that end, with the type that gives it priority.
        var engine = new SnapEngine { EndpointSnapEnabled = true };
        var line = VLine.Internal(new VXYZ(0, 0), new VXYZ(100, 0));

        var hit = engine.FindSnapPoint(new VXYZ(99, 1), new List<IDrawable> { line }, 1.0);

        Assert.NotNull(hit);
        Assert.Equal(SnapType.Endpoint, hit!.Type);
        Assert.Equal(100, hit.Point.X, 6);
        Assert.Equal(0, hit.Point.Y, 6);
    }
}
