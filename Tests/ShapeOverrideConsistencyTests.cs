using System;
using System.Linq;
using System.Reflection;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// <see cref="Shape.Contains"/> and <see cref="Shape.DistanceTo"/> are bounding-box stubs on the base
/// class: <c>DistanceTo</c> measures to the box centre and <c>Contains</c> tests the box. Either one
/// left un-overridden on a real shape is silently, confidently wrong.
///
/// <para>
/// This asymmetry has already been shipped twice — VLine had neither, and VCircle, VHatch and Region
/// had an exact <c>Contains</c> while still inheriting the bounding-box <c>DistanceTo</c>, so a point
/// exactly on a circle reported a distance equal to its radius. Reflection catches the next one at
/// build time instead of leaving it for a reader of the source to spot.
/// </para>
/// </summary>
public class ShapeOverrideConsistencyTests
{
    private static Type[] AllShapes() =>
        typeof(Shape).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && typeof(Shape).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToArray();

    /// <summary>
    /// Shapes with no enclosed area, for which "is this point inside?" has no useful answer beyond
    /// the bounding box.
    /// </summary>
    private static readonly string[] ContainsExempt =
    {
        "VPoint",        // a marker: the box is the point
        "VText",         // hit-tested as its layout box, which is what the box is
        "VGrid",         // a cloud of points, not an outline
        "VSpatialGrid",  // a grid of cells; use GetCellAt
        "VDimension", "VRadialDimension",  // annotations
        "VArrow",        // annotation
        // VGroup is deliberately absent: it implements both by delegating to its children.
    };

    /// <summary>
    /// Shapes with no geometry to measure to. Deliberately shorter than
    /// <see cref="ContainsExempt"/> — VGrid, VSpatialGrid and VPoint all have a meaningful nearest
    /// distance (nearest grid point, nearest cell, the point itself) even though they enclose
    /// nothing, so exempting them from both checks would let a real regression through.
    /// </summary>
    private static readonly string[] DistanceExempt =
    {
        "VText",         // measured to its layout box
        "VDimension", "VRadialDimension",
        "VArrow",
    };

    private static bool Overrides(Type type, string method) =>
        type.GetMethod(method, BindingFlags.Public | BindingFlags.Instance,
                       null, new[] { typeof(VXYZ) }, null)?.DeclaringType != typeof(Shape);

    [Fact]
    public void EveryShapeWithGeometryImplementsDistanceTo()
    {
        var missing = AllShapes()
            .Where(t => !DistanceExempt.Contains(t.Name))
            .Where(t => !Overrides(t, nameof(Shape.DistanceTo)))
            .ToArray();

        Assert.True(missing.Length == 0,
            "these shapes still measure to their bounding-box centre: " +
            string.Join(", ", missing.Select(t => t.Name)));
    }

    [Fact]
    public void EveryShapeThatEnclosesAreaImplementsContains()
    {
        var missing = AllShapes()
            .Where(t => !ContainsExempt.Contains(t.Name))
            .Where(t => !Overrides(t, nameof(Shape.Contains)))
            .ToArray();

        Assert.True(missing.Length == 0,
            "these shapes still hit-test their bounding box: " +
            string.Join(", ", missing.Select(t => t.Name)));
    }

    [Fact]
    public void ExemptionListsNameOnlyRealShapes()
    {
        // Keeps the lists honest when a shape is renamed or removed.
        var names = AllShapes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var stale = ContainsExempt.Concat(DistanceExempt).Distinct()
            .Where(e => !names.Contains(e)).ToArray();

        Assert.True(stale.Length == 0, "exemption lists name missing types: " + string.Join(", ", stale));
    }

    [Fact]
    public void ExemptionsAreNotClaimedForShapesThatActuallyImplementTheMethod()
    {
        // An exemption that is no longer true is misleading documentation in test form.
        var overreach = ContainsExempt.Where(name =>
        {
            var type = AllShapes().FirstOrDefault(t => t.Name == name);
            return type != null && Overrides(type, nameof(Shape.Contains));
        }).ToArray();

        Assert.True(overreach.Length == 0,
            "these are listed as Contains-exempt but do override it: " + string.Join(", ", overreach));
    }
}
