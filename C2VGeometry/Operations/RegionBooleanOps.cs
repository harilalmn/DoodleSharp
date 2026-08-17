using System;
using System.Collections.Generic;
using System.Linq;

namespace C2VGeometry;

/// <summary>
/// Provides boolean operations (union, intersection, difference, xor) on Regions.
/// Operations work by converting Region boundaries to high-resolution polygon approximations,
/// delegating to the existing PolygonClipper, then wrapping results back as Regions.
/// </summary>
public static class RegionBooleanOps
{
    /// <summary>Curve sampling density used when a caller does not specify one. Internal callers and
    /// the BooleanOps facade share it so the default cannot drift between the two entry points.</summary>
    public const int DefaultSegmentsPerCurve = 32;

    #region Standard Boolean Operations

    /// <summary>
    /// Computes the union of two regions.
    /// Returns a single Region if successful, or null if a single region cannot be formed.
    /// </summary>
    public static Region? Union(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.Union(polyA, polyB);
        if (result.Count == 0) return null;
        if (result.Count > 1) return null; // Disjoint regions

        return Region.FromPolygon(result[0]);
    }

    /// <summary>
    /// Computes the union of multiple regions.
    /// Returns a single Region if successful, or null if a single region cannot be formed.
    /// </summary>
    public static Region? Union(params Region[] regions)
        => Union((IEnumerable<Region>)regions);

    /// <summary>
    /// Computes the union of a list of regions, folding the binary operation across all of them.
    ///
    /// <para>
    /// <paramref name="segmentsPerCurve"/> is how finely a curve-bounded region is sampled before
    /// clipping. It was missing here while <see cref="Intersect(IEnumerable{Region}, int)"/>,
    /// <see cref="Difference(IEnumerable{Region}, int)"/> and <see cref="Xor(IEnumerable{Region}, int)"/>
    /// all accepted it — so a union was the one operation that silently ignored the caller's chosen
    /// precision and always used the default. Note the <c>params</c> overload cannot take it (an
    /// optional parameter cannot follow <c>params</c>), so pass a list to control precision.
    /// </para>
    /// </summary>
    public static Region? Union(IEnumerable<Region> regions, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var list = regions?.ToList() ?? new List<Region>();
        if (list.Count == 0) return null;
        if (list.Count == 1) return (Region)list[0].Clone();

        var current = list[0];
        for (int i = 1; i < list.Count; i++)
        {
            var result = Union(current, list[i], segmentsPerCurve);
            if (result == null) return null;
            current = result;
        }
        return current;
    }

    /// <summary>
    /// Computes the intersection of two regions.
    /// </summary>
    public static List<Region> Intersect(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.Intersect(polyA, polyB);
        return result.Select(Region.FromPolygon).ToList();
    }

    /// <summary>
    /// Computes the difference of two regions (a - b).
    /// </summary>
    public static List<Region> Difference(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.Difference(polyA, polyB);
        return result.Select(Region.FromPolygon).ToList();
    }

    /// <summary>
    /// Computes the symmetric difference (XOR) of two regions.
    /// </summary>
    public static List<Region> Xor(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.Xor(polyA, polyB);
        return result.Select(Region.FromPolygon).ToList();
    }

    #endregion

    #region Collection Operations

    // The following overloads accept a whole collection of regions (e.g. a List<Region>),
    // folding the binary operation across all of them:
    //   Intersect = the area common to ALL regions
    //   Difference = the first region minus every other region
    //   Xor        = the running symmetric difference of all regions
    // Union(IEnumerable<Region>) is defined above.

    /// <summary>Computes the intersection common to all regions. Returns an empty list if fewer than one region.</summary>
    public static List<Region> Intersect(IEnumerable<Region> regions, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var list = regions?.ToList() ?? new List<Region>();
        if (list.Count == 0) return new List<Region>();
        if (list.Count == 1) return new List<Region> { (Region)list[0].Clone() };

        var acc = new List<Region> { list[0] };
        for (int i = 1; i < list.Count && acc.Count > 0; i++)
        {
            var next = new List<Region>();
            foreach (var r in acc) next.AddRange(Intersect(r, list[i], segmentsPerCurve));
            acc = next;
        }
        return acc;
    }

    /// <summary>Computes the first region minus every subsequent region.</summary>
    public static List<Region> Difference(IEnumerable<Region> regions, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var list = regions?.ToList() ?? new List<Region>();
        if (list.Count == 0) return new List<Region>();
        if (list.Count == 1) return new List<Region> { (Region)list[0].Clone() };

        var acc = new List<Region> { list[0] };
        for (int i = 1; i < list.Count && acc.Count > 0; i++)
        {
            var next = new List<Region>();
            foreach (var r in acc) next.AddRange(Difference(r, list[i], segmentsPerCurve));
            acc = next;
        }
        return acc;
    }

    /// <summary>Computes the running symmetric difference (XOR) of all regions.</summary>
    public static List<Region> Xor(IEnumerable<Region> regions, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var list = regions?.ToList() ?? new List<Region>();
        if (list.Count == 0) return new List<Region>();
        if (list.Count == 1) return new List<Region> { (Region)list[0].Clone() };

        var acc = new List<Region> { list[0] };
        for (int i = 1; i < list.Count; i++)
        {
            var next = list[i];

            // (acc - next)
            var part = new List<Region>();
            foreach (var r in acc) part.AddRange(Difference(r, next, segmentsPerCurve));

            // (next - acc): subtract every accumulator region from next
            var remainder = new List<Region> { next };
            foreach (var r in acc)
            {
                var tmp = new List<Region>();
                foreach (var p in remainder) tmp.AddRange(Difference(p, r, segmentsPerCurve));
                remainder = tmp;
            }

            // the two parts are disjoint by construction
            part.AddRange(remainder);
            acc = part;
        }
        return acc;
    }

    /// <summary>Computes the intersection common to all supplied regions.</summary>
    public static List<Region> Intersect(params Region[] regions) => Intersect((IEnumerable<Region>)regions);

    /// <summary>Computes the first region minus every subsequent region.</summary>
    public static List<Region> Difference(params Region[] regions) => Difference((IEnumerable<Region>)regions);

    /// <summary>Computes the running symmetric difference (XOR) of all supplied regions.</summary>
    public static List<Region> Xor(params Region[] regions) => Xor((IEnumerable<Region>)regions);

    #endregion

    #region Boolean Operations with Hole Support

    /// <summary>
    /// Computes the intersection of two regions, returning results with hole information.
    /// </summary>
    public static List<Region> IntersectWithHoles(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.IntersectWithHoles(polyA, polyB);
        return result.Select(Region.FromPolygonWithHoles).ToList();
    }

    /// <summary>
    /// Computes the union of two regions, returning results with hole information.
    /// </summary>
    public static List<Region> UnionWithHoles(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.UnionWithHoles(polyA, polyB);
        return result.Select(Region.FromPolygonWithHoles).ToList();
    }

    /// <summary>
    /// Computes the difference of two regions, returning results with hole information.
    /// </summary>
    public static List<Region> DifferenceWithHoles(Region a, Region b, int segmentsPerCurve = DefaultSegmentsPerCurve)
    {
        var polyA = a.ToPolygonHighRes(segmentsPerCurve);
        var polyB = b.ToPolygonHighRes(segmentsPerCurve);

        var result = PolygonClipper.DifferenceWithHoles(polyA, polyB);
        return result.Select(Region.FromPolygonWithHoles).ToList();
    }

    #endregion

    #region Analysis

    /// <summary>
    /// Checks if a point is inside a region.
    /// </summary>
    public static bool PointInRegion(Region region, VXYZ point)
    {
        return region.Contains(point);
    }

    /// <summary>
    /// Calculates the area of a region.
    /// </summary>
    public static double Area(Region region)
    {
        return region.Area;
    }

    #endregion
}

/// <summary>
/// Extension methods for Region boolean operations.
/// </summary>
public static class RegionBooleanExtensions
{
    /// <summary>
    /// Computes the union of this region with another.
    /// Returns a single Region if successful, or null if a single region cannot be formed.
    /// </summary>
    public static Region? Union(this Region region, Region other)
    {
        return RegionBooleanOps.Union(region, other);
    }

    /// <summary>
    /// Computes the intersection of this region with another.
    /// </summary>
    public static List<Region> Intersect(this Region region, Region other)
    {
        return RegionBooleanOps.Intersect(region, other);
    }

    /// <summary>
    /// Computes the difference of this region with another (this - other).
    /// </summary>
    public static List<Region> Difference(this Region region, Region other)
    {
        return RegionBooleanOps.Difference(region, other);
    }

    /// <summary>
    /// Computes the symmetric difference (XOR) of this region with another.
    /// </summary>
    public static List<Region> Xor(this Region region, Region other)
    {
        return RegionBooleanOps.Xor(region, other);
    }

    /// <summary>
    /// Checks if a point is inside this region.
    /// </summary>
    public static bool ContainsPoint(this Region region, VXYZ point)
    {
        return region.Contains(point);
    }

    /// <summary>
    /// Calculates the area of this region.
    /// </summary>
    public static double GetArea(this Region region)
    {
        return region.Area;
    }
}
