using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DoodleSharp.Documentation
{
    public class DocGenerator
    {
        private Assembly[] _assemblies;
        private string[] _namespacePrefixes;
        private Dictionary<string, string> _summaries;
        private Dictionary<string, string> _csharpSamples;
        private Dictionary<string, string> _memberDescriptions;

        public DocGenerator()
            : this(
                new[] { Assembly.GetExecutingAssembly(), typeof(C2VGeometry.Shape).Assembly },
                new[]
                {
                    "C2VGeometry",
                    "DoodleSharp.Animation",
                    "DoodleSharp.Export",
                    "DoodleSharp.Console"
                })
        {
        }

        public DocGenerator(Assembly assembly, params string[] namespacePrefixes)
            : this(new[] { assembly }, namespacePrefixes)
        {
        }

        public DocGenerator(Assembly[] assemblies, string[] namespacePrefixes)
        {
            _assemblies = (assemblies ?? Array.Empty<Assembly>()).Where(a => a != null).Distinct().ToArray();
            _namespacePrefixes = namespacePrefixes ?? Array.Empty<string>();
            InitializeSummaries();
            InitializeCSharpSamples();
            InitializeMemberDescriptions();
        }

        private void InitializeSummaries()
        {
            _summaries = new Dictionary<string, string>
            {
                { "DoodleSharp", "Root namespace for the DoodleSharp application." },
                { "C2VGeometry", "Contains classes and interfaces for 2D geometric shapes and operations, plus the VXYZ coordinate type. This is the single geometry namespace used throughout DoodleSharp." },
                { "DoodleSharp.Editor", "Contains classes related to the code editor, including formatting, completion, and snippets." },

                // Base classes
                { "Shape", "Abstract base class for all drawable shapes; implements IDrawable. Every shape auto-registers on construction (Shape.DefaultRegistry is wired to the canvas), so nothing extra is needed to make one visible. Place() is the call for everything else: it puts a shape on the canvas and keeps it there (registering it and setting IsExplicitlyDrawn, which exempts it from the post-Main() sweep that hides unnamed shapes). Reach for it on method results (booleans, ArrayOps, Chart), on the query results that deliberately do not draw their answer (GeometryHelper.IntersectLineLine and friends, VRay.ToFiniteLine, VRay.ToXLine, VXLine.ToFiniteLine), and on anything built while AutoRegister was false. It is idempotent, and Remove() is its inverse. Draw() is the historical name for Place() and is exactly equivalent; existing files that call it keep working unchanged, and there is nothing to migrate. The drawing tools and editor snippets now write Place(). Identity: Id (long, assigned automatically, reset to 1 at the start of each run) and Name (string, default \"\"). Styling: Color, FillColor (both color-name or hex strings), LineWeight, LineType, LineTypeScale. State: IsVisible, IsSelected, IsPlaced, IsExplicitlyDrawn. Animation: DrawFactor (0-1 progressive drawing), OffsetX, OffsetY, RotationAngle (virtual — VRectangle overrides it with real geometry, so RotateAnimation works on a rectangle too), RotationPivot, FlipProgress, FlipAxis, Opacity. Static configuration: DefaultRegistry, AutoRegister, DefaultColor (\"Cyan\"), DefaultFillColor (\"Transparent\"), DefaultLineWeight (2.0), DefaultLineType (Continuous), DefaultLineTypeScale (1.0), ResetDefaults(), ResetIdCounter(). Methods: Place() (and its historical alias Draw()), Remove(), Show(), Hide(), Clone() (returns the same type via covariant return), CopyStyleTo(target) (copies the five styling members onto another shape and returns it), Move(), Rotate(), Flip(), Scale(), GetBounds() (returns BoundingBox), Contains(), DistanceTo(), Intersect(), DoesIntersect(), GetControlPoints(), MoveControlPoint(), BringAbove(otherShape), SendBehind(otherShape). Contains() and DistanceTo() are bounding-box fallbacks on the base class, but every shape with a real outline overrides them with true geometry: VLine, VPolyline, VArc, VBezier, VSpline, VXLine and VRay test/measure against the stroke; VPolygon, VRectangle, VCircle, VEllipse, VGroup, VHatch and Region do a genuine interior Contains and measure to the outline, which means zero on it and positive on both sides — not a signed depth, so pair DistanceTo with Contains. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension keep the bounding-box answer, because for those the box is the shape or there is no outline to test; a reflection test (ShapeOverrideConsistencyTests) fails the build if a new shape is added without both overrides. Visibility note: after Main() returns, shapes with empty Name and IsExplicitlyDrawn=false are auto-hidden. The auto-naming pass only fills Name for `var x = new VShape(...)` and field declarations — for List.Add, array-slot assignments, and helper-returned shapes, set Name explicitly in the initializer or call .Place(). The console logs a warning when shapes get hidden." },
                { "BoundingBox", "Represents an axis-aligned bounding box with Min (lower-left) and Max (upper-right) corner points, both VXYZ. Returned by Shape.GetBounds() on every shape. Read-only properties: Min, Max, Width (Max.X - Min.X), Height (Max.Y - Min.Y), Center, Area (Width × Height). Methods: Contains(point) — inclusive of the boundary and ignoring Z; Intersects(other) — true when the boxes overlap or merely touch; Union(other) — the smallest box containing both; Expand(distance) — grown by the distance on all four sides (negative values contract, and may invert the box). Constructible directly: new BoundingBox(min, max). Supports tuple deconstruction: var (min, max) = bounds. Infinite shapes (VRay, VXLine) return boxes with non-finite corners." },
                { "IDrawable", "Interface for any object that can be drawn on the canvas. Defines Draw() plus the five styling properties every drawable exposes: Color, FillColor, LineWeight, LineType and LineTypeScale. Shape implements it, and ICurve extends it. Both Place() and Draw() are declared here, so either reaches the same behaviour through an IDrawable or ICurve reference exactly as it does through Shape." },
                { "ICurve", "Interface for geometric shapes that can be treated as curves. Implemented by VLine, VCircle, VArc, VEllipse, VPolyline, VPolygon, VBezier, VSpline, VRay and VXLine (VRectangle and VCell inherit it through VPolygon). Extends IDrawable, so all curves have Draw(), Color, FillColor, LineWeight, LineType and LineTypeScale. Properties: StartPoint, EndPoint (VXYZ; equal for closed curves), Vertices (List<VXYZ> of the defining points), SelfIntersecting. Methods: GetLength(), Divide(n), Measure(segmentLength), Project(point), PointAtSegmentLength(len), Offset(distance), Offset(List<double>), PointsAtChordLengthFromPoint(point, chordLength), SplitAtPoint(point), NormalAtPoint(point), Intersect(otherCurve), PointAtParameter(t), ParameterAtPoint(point), SetBounds(startParam, endParam). All coordinate results are VXYZ. PointAtParameter() takes a normalized 0-1 position; ParameterAtPoint() is its inverse for the closest point on the curve. SetBounds() trims a curve in place and throws NotSupportedException on VCircle, VPolygon, VRay and VXLine, whose trimmed form would be a different shape type." },
                { "IShapeRegistry", "The hook that connects the geometry library to a canvas. Shape.DefaultRegistry holds the active implementation; when it is non-null and Shape.AutoRegister is true, every shape constructor calls Register(this) — which is why shapes appear without any explicit call. Members: Register(shape), Unregister(shape) (what Shape.Remove() calls), MoveAbove(shape, reference) and MoveBehind(shape, reference) (what BringAbove/SendBehind call). DoodleSharp supplies CanvasRenderer as the implementation. You rarely implement this yourself — it exists so C2VGeometry stays free of any UI dependency." },
                { "IGlyphOutlineProvider", "Supplies vector outlines for the characters of a VText. C2VGeometry has no font engine of its own, so the host application implements this and assigns it to VText.GlyphOutlineProvider at startup (the same injection pattern as Shape.DefaultRegistry). Single member: GetCharContours(text, charIndex) returning List<List<VXYZ>>? — one inner list per closed contour, in world coordinates that match where the character is rendered (honouring font, height, anchor and rotation), or null for whitespace. With no provider set, VText.ToCharShape/LiftChar/LiftChars all return null." },
                { "ControlPoint", "One draggable handle exposed by a shape for interactive editing on the canvas. Returned by Shape.GetControlPoints() and consumed by Shape.MoveControlPoint(index, newPosition). Read-only Type (ControlPointType) and Label; settable X and Y; ToVXYZ() converts the position to a VXYZ. Constructor: new ControlPoint(type, x, y, label = \"\"). Index 0 is by convention the whole-shape Move handle." },
                { "ControlPointType", "The role of a ControlPoint: Move (drag the whole shape), Vertex (an endpoint or polygon vertex), Radius (resize a circle or arc), Rotation, or CurveControl (a Bezier/spline handle)." },
                { "GeometryTolerance", "Static class holding the library's floating-point tolerances and the comparison helpers built on them. Constants: Epsilon (1e-9, the general comparison tolerance and the default for VXYZ equality), VisualEpsilon (1e-6, for on-screen coincidence), AngleEpsilon (1e-5 radians). Helpers, all taking an optional epsilon: AreEqual, IsZero, IsLessThan, IsGreaterThan, IsLessOrEqual, IsGreaterOrEqual, IsInRange, PointsAreEqual, VectorsAreEqual, AnglesAreEqual, Sign. Plus NormalizeAngle (radians into [0, 2π)), NormalizeAngleDegrees ([0, 360)), ClampParametric (clamp to [0,1]), Clamp, Distance / DistanceSquared, PointOnSegment, PointToLineDistance, Orientation (sign of the cross product) and AreCollinear." },
                { "IntersectionResult", "Represents the result of an intersection operation between curves. Contains Points (list of intersection points) and Curves (list of overlapping segments). Properties: HasIntersection (true if any intersection), IsSinglePoint (exactly one point), HasOverlap (curves share a segment), Count (total elements). Use Intersect() method on any ICurve to compute intersections." },
                { "CurveIntersection", "Static utility class providing curve intersection algorithms. Supports Line-Line, Line-Circle, Line-Arc, Line-Ellipse, Circle-Circle, Circle-Arc, Arc-Arc intersections with specialized algorithms. Complex curves use segment-based approximation. Also provides IsSelfIntersecting() for detecting self-intersections." },
                { "CurveGeometry", "Static helper class holding the point-to-curve measurement the curve shapes share — it is what VLine, VPolyline, VPolygon, VBezier and VSpline call from their Contains and DistanceTo overrides, and it is public so you can use it on your own vertex lists. Methods: DistanceToSegment(point, a, b) — shortest distance to the segment [a, b], falling back to the distance to the point itself for a degenerate zero-length segment; DistanceToPath(point, IReadOnlyList<VXYZ> vertices, bool closed = false) — the nearest of every segment through the vertices, adding the closing edge when closed is true, and returning double.PositiveInfinity for a null or empty list; DistanceToCurve(point, ICurve curve, int samples = 96) — samples any ICurve into a polyline and measures to that, for curves with no practical closed form; IsOnStroke(distance, curveExtent) — whether a distance counts as lying on a stroke of that size, using a tolerance of max(GeometryTolerance.Epsilon, |curveExtent| × 1e-6) so the answer does not depend on the units the drawing happens to use." },
                { "GeometryDiagnostics", "Static class where the geometry library reports something you should know about but that is not exceptional — most visibly, why a BooleanOps.Union returned null. C2VGeometry has no user interface of its own, so the host application plugs a sink into it at startup (the same injection pattern as Shape.DefaultRegistry and VText.GlyphOutlineProvider); DoodleSharp routes it to the console panel, where the messages appear tagged \"Geometry\". Members: Sink (Action<string>?, null by default — a null sink discards messages, so a library consumer with no console pays nothing) and Report(string message), which forwards to the sink and never throws (an exception from a broken sink is swallowed rather than breaking the geometry operation). Set Sink yourself to capture the messages, for instance into a List<string> for assertion or logging." },

                // Shapes
                { "VArc", "Represents a 2D arc defined by a center point (VXYZ), radius, start angle, and end angle (in degrees, counter-clockwise from the positive X axis). The arc sweeps counter-clockwise from StartAngle to EndAngle; if EndAngle <= StartAngle the constructor adds 360 so the sweep is always positive. Also constructible through three points, and via ten static factories (FromStartCenterEnd, FromCenterStartEnd, FromStartCenterAngle, FromCenterStartAngle, FromStartCenterLength, FromCenterStartLength, FromStartEndRadius, FromStartEndAngle, Continue). Default stroke color is Orange. DistanceTo(point) is computed exactly (not by sampling) and honours the sweep: a point outside the swept sector measures to the nearer endpoint, not to the full circle; a point at the centre returns Radius. Contains(point) means \"lies on the arc\". Implements ICurve, so Divide/Measure/Project/Offset/SplitAtPoint/SetBounds all apply." },
                { "VCircle", "Represents a 2D circle defined by a center point (VXYZ) and a radius. Constructors: (center, radius), (centerX, centerY, radius), and (p1, p2, p3) for the circumcircle through three points — which throws ArgumentException when the points are collinear. Static factories: FromCenterDiameter(center, diameter), FromCenterDiameter(cx, cy, diameter), FromTwoPoints(p1, p2) where the two points are the ends of a diameter. Computed properties: Area (πr²), Circumference (2πr). Default stroke color is Yellow. Implements ICurve; the parameter domain runs counter-clockwise from angle 0 (the point at (Center.X + Radius, Center.Y)), and SetBounds throws NotSupportedException because a trimmed circle is an arc." },
                { "VRectangle", "Represents a 2D rectangle defined by a corner point (bottom-left), width, and height. Inherits from VPolygon, so all polygon members (Points, Area, SignedArea, Slice, Offset, boolean ops) are available. Constructors: (VXYZ corner, width, height), (x, y, width, height), (VXYZ bottomLeft, VXYZ topRight). Setting Corner, Width, Height or RotationAngle rebuilds the four corner points in place. RotationAngle OVERRIDES Shape.RotationAngle (it no longer shadows it with `new`): there is one property, so it means the same thing whether you reach the rectangle through a VRectangle or a Shape variable, and RotateAnimation on a rectangle works — the animation's writes rebuild the corners. Rotation is in degrees counter-clockwise about the rectangle's own centre. Negative Width/Height are allowed and simply mirror the rectangle. Contains(point) is an exact interior test that honours the rotation; DistanceTo(point) is inherited from VPolygon and measures to the boundary. Default stroke color is Magenta." },
                { "VPolygon", "Represents a closed 2D polygon defined by a list of VXYZ vertices. The closing edge from the last point back to the first is implicit — do not repeat the first point. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>), and (List<ICurve> curves) which orders open curves into one continuous closed loop and throws ArgumentException on a closed curve, a gap, a branch, or a self-intersection. Properties: Points (mutable list), Curves (the internal edge representation, non-registering VLines), Area (shoelace, always positive), SignedArea (positive for counter-clockwise winding, negative for clockwise), SelfIntersecting (computed once at construction). Methods: AddPoint(point), AddPoint(x, y), and Slice(linePoint1, linePoint2) which cuts the polygon along the infinite line through two points and returns List<VPolygon> (there is no Slice overload taking a VXLine or VRay — pass VXLine.GetTwoPoints() or a ray's Origin and GetPointAtDistance instead). Slice is area-preserving: the pieces sum back to Area, and a concave polygon crossed more than twice comes back as three or more pieces, so never assume exactly two. A line that misses, or merely grazes a vertex or an edge, returns a single piece copying the original, and the pieces inherit the source's styling. Contains(point) is a genuine interior test (even-odd ray cast), not a bounding-box check; DistanceTo(point) measures to the BOUNDARY, so it is zero on an edge and positive both inside and outside — it is not a signed depth. Default stroke color is LightBlue. Implements ICurve; SetBounds throws NotSupportedException because a trimmed polygon is a polyline." },
                { "VPolyline", "Represents an open sequence of connected line segments through a list of VXYZ points. Unlike VPolygon it does not close automatically — repeat the first point as the last to close it manually. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>). Properties: Points (mutable), SelfIntersecting. Methods: AddPoint(point), AddPoint(x, y). DistanceTo(point) is the exact distance to the nearest segment (no closing edge is added — a closed polyline repeats its first point as the last, so the closing segment is already in the list); Contains(point) means \"lies on the path\". Implements ICurve; parameterisation is arc-length based across all segments, and SetBounds trims the point list in place." },
                { "VLine", "Represents a straight line segment between two points. The most basic geometric primitive. Endpoints are the settable VXYZ properties Start and End — there are no StartPoint/EndPoint properties on a concrete VLine (those exist only as explicit ICurve implementations, so generic ICurve code still works). Constructors: (VXYZ start, VXYZ end), (x1, y1, x2, y2), (VXYZ startPoint, angleInDegrees, length). Properties: Start, End, MidPoint, Direction (unit vector), Vertices, SelfIntersecting (always false). DistanceTo(point) is the exact point-to-segment distance, clamped to the endpoints so a point beyond the end measures to that endpoint rather than to the infinite line. Contains(point) means \"lies on the segment\" — a line encloses no area — judged with a tolerance scaled to the line's own length." },
                { "VXLine", "Represents an infinite construction line (like AutoCAD's XLine). Extends infinitely in both directions through a base point along a direction. Useful for construction geometry and slicing polygons. Constructors: new VXLine(VXYZ basePoint, VXYZ direction) — the second argument is a DIRECTION, not a second point — and new VXLine(x1, y1, x2, y2), which is the through-two-points form. Watch that distinction: passing a second point to the two-VXYZ overload compiles and silently builds a differently-aimed line; write new VXLine(p1, p2 - p1) if you hold two VXYZ. Static helpers: Horizontal(y), Vertical(x). Its point property is BasePoint (VRay's is Origin). DistanceTo(point) is the perpendicular distance to the infinite line — nothing is clamped, because the line has no ends; Contains(point) is true anywhere on it." },
                { "VRay", "Represents a semi-infinite ray (like AutoCAD's Ray). Starts at an origin point and extends infinitely in one direction. Constructors: new VRay(VXYZ origin, VXYZ direction) — the second argument is a DIRECTION, not a point the ray passes through — and new VRay(originX, originY, throughX, throughY), which IS the through-point form. Watch that distinction: passing a target point to the two-VXYZ overload compiles and silently aims the ray elsewhere; write new VRay(origin, target - origin) if you hold two VXYZ. Static helpers: HorizontalRight, HorizontalLeft, VerticalUp, VerticalDown, AtAngle(origin, angleDegrees). Its point property is Origin (VXLine's is BasePoint); RenderExtent (default 10000) is how far it is actually drawn and what its bounds are computed from, since the ray itself has no end. Also: GetPointAtDistance(d), ContainsPoint(p), ToFiniteLine() and ToXLine(). The last two return a real VLine/VXLine you can measure and intersect, but it is deliberately NOT drawn — converting a ray for a calculation should not add a second line to the drawing. Call .Place() on the result if you do want to see it (VXLine.ToFiniteLine() behaves the same way). DistanceTo(point) is perpendicular where the point projects onto the ray and measured to Origin for anything behind the start; Contains(point) is false behind the origin." },
                { "VEllipse", "Represents a 2D ellipse defined by a center point (VXYZ), X radius (horizontal) and Y radius (vertical). Constructors: (center, radiusX, radiusY), (centerX, centerY, radiusX, radiusY), and (center, radiusX, radiusY, startAngle, endAngle) for an elliptical arc — angles in degrees, defaults 0 and 360. Computed properties: Area (π·rx·ry), Circumference (Ramanujan approximation; exact only for a circle). Implements ICurve and is ARC-LENGTH parameterised like every other curve: Evaluate(t) and PointAtParameter(t) walk the parameter along the length of the curve, so Divide(n) returns evenly spaced points and SetBounds(s, e) trims to that stretch of curve rather than that stretch of sweep angle. EvaluateByAngle(t) gives the angle-linear reading instead (t interpolated from StartAngle to EndAngle) — use it for radial spokes and sector boundaries. On a circle the two agree; they diverge as the ellipse becomes more eccentric. Contains(point) is an exact interior test for a FULL ellipse and an on-the-curve test for a partial sweep (which encloses no area); DistanceTo(point) is the sampled distance to the curve and honours the sweep." },
                { "VPoint", "Represents a visible point marker on the canvas — a drawn dot, not a coordinate. For coordinates and vectors use VXYZ; constructing a VPoint auto-registers a shape. Constructors: (x, y) and (VXYZ position). X and Y are settable. Converts to VXYZ implicitly, or explicitly via AsVXYZ(). Full arithmetic operator set (+, -, *, /) against VPoint, VXYZ and scalars — every overload returns a plain VXYZ so intermediates never pollute the canvas. Default Color and FillColor are both White, and unusually they are assigned OUTRIGHT rather than through ShapeDefaults: VPoint is the one shape that does NOT honour ShapeDefaults.GlobalColor / GlobalFillColor, so set point.Color yourself if you are styling globally." },
                { "VBezier", "Represents a 2D cubic Bezier curve defined by four VXYZ control points: P0 (start), P1 and P2 (control handles), P3 (end). Constructors: (p0, p1, p2, p3) and (x0, y0, x1, y1, x2, y2, x3, y3). The Segments property (default 32) controls how finely the curve is tessellated for rendering and for length/parameter queries. Evaluate(t) gives the exact point at the Bernstein parameter t. DistanceTo(point) is the shortest distance to the curve, found by sampling it (96 samples), and Contains(point) means \"lies on the curve\". Implements ICurve; SetBounds performs an exact De Casteljau trim in place." },
                { "VSpline", "Represents a smooth Catmull-Rom spline passing through every one of its control points. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>). Properties: ControlPoints, SegmentsPerSpan (default 16 — tessellation density between adjacent control points), Tension (default 0.5; 0 is angular, 1 is loose). DistanceTo(point) is the shortest distance to the curve, found by sampling it (96 samples), and Contains(point) means \"lies on the curve\". Implements ICurve; SetBounds resamples the trimmed range rather than dropping control points, because Catmull-Rom tangents depend on the neighbouring points." },
                { "VText", "Represents text drawn at a specific position. Supports font size via Height property or constructor parameter. Constructors: VText(point, text), VText(point, text, height), VText(x, y, text), VText(x, y, text, height). Supports Font, FontWeight, Anchor, and Angle properties for styling, alignment, and rotation. Individual characters can be converted to vector outline shapes: ToCharShape(i) (non-mutating), LiftChar(i) and the indexer text[i] (extract the glyph as a shape AND replace the character with a space), and LiftChars(start, count) for a selection. These let you morph a letter into another shape, e.g. new TransformAnimation(text[0], circle, 2)." },
                { "VTextAnchor", "Enum specifying the anchor (alignment) point for VText. Values: BottomLeft (default), BottomCenter, BottomRight, MiddleLeft, MiddleCenter, MiddleRight, TopLeft, TopCenter, TopRight. Controls which point of the text bounding box is placed at the text's position." },
                { "VGroup", "Represents a collection of shapes treated as a single unit. Supports multiple constructors (empty, params, IEnumerable, List), group transformations (Move, Rotate, Scale, Flip), style application (ApplyStyle, ApplyColor, ApplyFillColor), and utility methods (Flatten, ForEach, Where, GetShapesOfType). When drawn, the group is rendered and selected as a single entity on the canvas." },
                { "VGrid", "Represents a rectangular grid of VPoint markers. Constructors: VGrid(location, xcount, ycount, xSpacing = 1.0, ySpacing = null, centered = true) — ySpacing is double? and null means \"same as xSpacing\", so VGrid(loc, 5, 5, 10) is a square grid with spacing 10 on both axes; VGrid(location, xcount, ycount, spacing, centered) for uniform spacing with an explicit centered (it deliberately has no default, which is what keeps the four-argument call unambiguous); VGrid(location, xcount, ycount, centered) for spacing 1.0. If centered=true, grid is centered at location; if false, location is bottom-left corner. Access points via Points property, indexers [index] or [col, row], or GetRow()/GetColumn() methods. Supports all Shape transformations (Move, Rotate, Scale, Flip) and ApplyStyle() to set colors on all points." },
                { "VCell", "Represents a square cell with a VPolygon boundary. Extends VPolygon. Properties: UniqueId (int), Neighbours (List<VCell>), Center (VXYZ), CellSize (double), Column (int), Row (int), Blocked (bool). Used as a building block for VSpatialGrid. Neighbours are set by the parent grid (4-connectivity: left, right, below, above)." },
                { "VSpatialGrid", "Represents a grid of square VCell instances with neighbour connectivity and A* pathfinding. Constructor: VSpatialGrid(location, xCount, yCount, cellSize). Location is the center of the bottom-left cell. Each cell knows its adjacent neighbours (4-connectivity). Access cells via Cells property, indexers [index] or [col, row], or GetRow()/GetColumn(). Use FindPath(start, end) for A* shortest path, GetClosestCell(point) for O(log n) nearest-cell lookup via KD-tree." },
                { "VArrow", "Represents an arrow: a straight shaft from Start to End with a V-shaped head. Constructors: (VXYZ start, VXYZ end), (x1, y1, x2, y2), (VXYZ startPoint, VXYZ direction, double length). Properties: Start, End (settable VXYZ — there are no StartPoint/EndPoint aliases), MidPoint, HeadLength (default 15 world units), HeadAngle (default 30 degrees — half-angle of each wing off the shaft), DoubleEnded (default false; when true a head is drawn at Start as well). GetEndArrowhead() and GetStartArrowhead() return the two wing tip coordinates. VArrow is a plain Shape, not an ICurve." },
                { "RayCaster", "Accelerated 2D ray-casting against an explicit collection of shapes. Constructor `new RayCaster(IEnumerable<Shape> shapes, int leafSize = 8)` — you pass the shapes to index; there is no canvas-snapshot constructor (the geometry library has no canvas). To cast against everything currently drawn, pass `CanvasRenderer.Instance.GetShapes().OfType<Shape>()` (add `using DoodleSharp.Canvas;` and `using System.Linq;`). It builds an axis-aligned BVH with Surface Area Heuristic splitting, so each subsequent ray query runs in O(log N) average time and scales to millions of shapes. Only shapes with IsVisible == true are indexed; VPoint markers are always excluded (zero area, not a useful ray target), as are shapes with null or non-finite bounds (VRay, VXLine). The collection is snapshotted at construction — shapes added or removed afterwards are not reflected, but Refit() refreshes cached AABBs in O(N) when indexed shapes move. Query methods: FindIntersection(location, direction, exclusionList = null) returns RayHit? for the closest hit, with an optional List<Shape> of shapes to skip (useful for casting off a known source shape or finding the next hit past a set of shapes); FindIntersection(location, direction, maxDistance, exclusionList = null) also caps the search distance and prunes BVH sub-trees beyond the cap; HasIntersection(location, direction, maxDistance) returns true on the first hit (faster shadow-ray query); FindIntersections(queries, parallel = true) batches over IReadOnlyList<RayQuery>. Queries run on the XY plane (Z ignored); direction need not be normalised. Inline ray-vs-shape math handles VLine, VCircle, VArc, VEllipse, VPolygon (and VRectangle), VPolyline with zero allocation; other shape types fall back to AABB hit. Queries are thread-safe after construction." },
                { "RayHit", "Readonly record struct returned by RayCaster.FindIntersection. Fields: Shape (the hit shape), Point (VXYZ world-space hit location), Distance (Euclidean distance from ray origin to the hit point)." },
                { "RayQuery", "Readonly record struct used by RayCaster.FindIntersections to describe a single ray. Fields: Origin (VXYZ), Direction (VXYZ, need not be normalised)." },
                { "VDimension", "Represents a dimension line showing the distance between two points with text annotation. AutoCAD-style properties: Offset, ArrowSize, TextHeight, DecimalPlaces, ExtendBeyondDimLines, OffsetFromOrigin, SuppressExtLine1/2, SuppressDimensionLine, Prefix, Suffix, TextBackgroundOpaque. Per-element colors: ExtensionLineColor, DimensionLineColor, TextColor (null = use base Color). The dimension line is always split around the text for readability. Renders arrowheads at both ends of the dimension line." },
                { "VRadialDimension", "Represents a radial or diameter dimension for circles and arcs. Draws a leader line from center to circumference with an arrowhead and text label (R for radius, \u2300 for diameter). Constructors: VRadialDimension(circle), VRadialDimension(arc), VRadialDimension(center, radius). Properties: LeaderAngle (direction of leader), ShowDiameter (diameter mode), ArrowSize, TextHeight, DecimalPlaces, Prefix, Suffix, CustomText, TextBackgroundOpaque. Per-element colors: DimensionLineColor, TextColor." },

                // Legacy aliases (for backward compatibility)
                { "Arc2D", "Represents a 2D arc defined by a center, radius, start angle, and end angle." },
                { "Circle2D", "Represents a 2D circle defined by a center point and a radius." },
                { "Rectangle2D", "Represents a 2D axis-aligned rectangle defined by a corner, width, and height." },
                { "Polygon2D", "Represents a closed 2D polygon defined by a list of vertices." },
                { "Polyline2D", "Represents an open sequence of connected line segments." },
                { "Line2D", "Represents a straight line segment between two points." },
                { "Ellipse2D", "Represents a 2D ellipse defined by a center, X radius, and Y radius." },
                { "Point2D", "Represents a visible point marker on the canvas. For coordinate storage, use VXYZ." },
                { "Bezier2D", "Represents a 2D cubic Bezier curve." },
                { "Spline2D", "Represents a smooth spline curve passing through a series of points." },
                { "Text2D", "Represents text drawn at a specific position." },
                { "Group2D", "Represents a collection of shapes treated as a single unit." },
                { "Grid2D", "Represents a rectangular grid of points." },

                // Support classes
                { "VXYZ", "3D coordinate type (X, Y, Z) used for every position, vector and direction parameter in the library — the counterpart to Revit's XYZ. Its components are read-only: every operation returns a new instance, so a VXYZ can be shared without aliasing bugs. Constructors: (x, y, z), (x, y) with Z = 0, and () for the origin. Never registers on the canvas — use it freely for intermediate maths, and reach for VPoint only when you want a dot drawn. Vector operations: Add, Subtract, Multiply, Divide, Negate, Normalize (returns Zero for a zero-length vector rather than throwing), GetLength, DistanceTo, DotProduct, CrossProduct, TripleProduct, AngleTo (radians, 0 to π), Rotate(degrees) about the Z axis, Clone, AsVPoint. Tests: IsZeroLength, IsUnitLength, IsAlmostEqualTo(other, tolerance = 1e-9), static IsWithinLengthLimits. Indexer [0]/[1]/[2] reads X/Y/Z and throws IndexOutOfRangeException otherwise. Operators +, -, * and / work with scalars and with VPoint (mixed operations return a plain VXYZ, never a drawable point); == and != are fuzzy comparisons using IsAlmostEqualTo, so GetHashCode rounds to 8 decimals to match. Static properties: Zero, BasisX, BasisY, BasisZ." },
                { "VFont", "Font family for VText. Values: Arial (default), TimesNewRoman, CourierNew, Verdana, Georgia, Tahoma, TrebuchetMS, Consolas, Calibri, Cambria, SegoeUI, ComicSansMS, Impact, LucidaConsole." },
                { "VFontWeight", "Font weight for VText: Normal (default) or Bold." },
                { "VPlane", "An infinite plane in 3D, used as the mirror for VTransform.CreateReflection and as the source for VCoordinateSystem.ByPlane. It has no public constructor — build one with the static factories CreateByNormalAndOrigin(normal, origin), CreateByOriginAndBasis(origin, xVec, yVec) or CreateByThreePoints(p1, p2, p3). Read-only properties: Origin, Normal, XVec, YVec — all four are normalised on construction, and the two basis vectors are derived automatically when you supply only a normal. There is no ProjectPoint or DistanceTo on VPlane; project a point yourself with VCoordinateSystem.ByPlane(plane).ToLocal(point), whose Z component is the signed distance to the plane. The drawing canvas is the XY plane, so VPlane matters only for 3D vector maths — nothing on it renders." },
                { "VTransform", "An affine 3D transform stored as three basis vectors plus an origin (not a 4x4 matrix — there is no Matrix property). Members: BasisX, BasisY, BasisZ, Origin (all settable VXYZ), the static Identity, the static factories CreateRotationDegrees(axis, angleDegrees), CreateRotationRadians(axis, angleRadians) and CreateReflection(plane), and the two application methods OfPoint(point) (applies the basis AND the origin translation) and OfVector(vec) (basis only, translation ignored). There is no Multiply, Inverse or CreateTranslation — compose by hand, or set Origin directly for a translation. Rotation is the one place this type departs from the library's degrees convention, so there are two explicitly-named factories: CreateRotationDegrees(axis, 90) matches Shape.Rotate, VXYZ.Rotate, VCoordinateSystem.Rotate and GeometryHelper.RotatePoint, and is the one to prefer; CreateRotationRadians(axis, Math.PI / 2) is for when you already hold radians. The original name, CreateRotation, is the radians overload and is now [Obsolete] — it compiles and behaves exactly as before, but it never said which unit it took. Nothing here registers on the canvas." },
                { "VCoordinateSystem", "An origin plus three orthonormal axes, for converting between world coordinates and a local frame — Dynamo-style, so it is built through factories rather than a constructor: ByOrigin(origin), ByOrigin(x, y, z), ByOriginVectors(origin, x, y, z), ByOriginXY(origin, x, y) (Z from the cross product, Y re-orthogonalised), ByOriginZAxis(origin, z) (X and Y chosen arbitrarily but consistently), ByPlane(plane), and the static Identity. Read-only properties: Origin, XAxis, YAxis, ZAxis — the axis names are XAxis/YAxis/ZAxis, not BasisX/BasisY/BasisZ (those are VTransform's). Methods: ToLocal(worldPoint) and ToWorld(localPoint) / ToWorld(x, y, z) convert in both directions; Translate(vector) and Rotate(axis, angleDegrees) each return a NEW system, leaving this one unchanged. Rotate takes DEGREES, like every other rotation in the library — Rotate(VXYZ.BasisZ, 90) is a genuine quarter turn, and agrees with VXYZ.Rotate(90). Purely computational: nothing draws." },
                { "GeometryHelper", "Static point-and-shape maths used by the shapes themselves and available to you. Point transforms, all returning a plain VXYZ: RotatePoint(point, pivot, angleDegrees), FlipPoint(point, mirrorLine), MovePoint(point, vector), ScalePoint(point, center, factor). Angles in degrees: NormalizeAngle(deg) folds into [0, 360); AngleDifference(target, source) gives the smallest signed turn in [-180, 180], which is what you want for shortest-path rotation. Analysis: IntersectCircleCircle(c1, r1, c2, r2) returns a List<VXYZ> of 0, 1 (tangent) or 2 points; GetPolylineNormalAtPoint(points, p, isClosed) returns the unit normal of the segment nearest p. The three Intersect* methods return Shape? — IntersectLineLine(l1, l2), IntersectLineRect(line, rect) and IntersectRectRect(r1, r2) — because the answer carries its own type: a crossing is a VPoint, a collinear overlap is a VLine, a rectangle overlap is a VRectangle. That shape is NOT drawn: asking where two lines meet should not add anything to the canvas. Read the coordinates off the result and let it go, or call .Place() on it if you want it placed. IntersectLineLine returns a VPoint for a crossing, a VLine for a collinear overlap, or null; IntersectLineRect returns a VPoint when the line only grazes a corner; the two rectangle methods assume axis-aligned rectangles. When you would rather have plain coordinates than a shape, use curve.Intersect(other) (see CurveIntersection), which returns an IntersectionResult of VXYZ points." },
                { "DoubleExtensions", "Two extension methods on double, ToRadians() and ToDegrees(), for the boundary between this library and System.Math. Every angle in C2VGeometry is in DEGREES — Shape.Rotate, VXYZ.Rotate, VCoordinateSystem.Rotate, GeometryHelper.RotatePoint, and the VArc/VEllipse angle properties — while System.Math works in RADIANS. These exist so that crossing is written as what it is (30.0.ToRadians()) rather than an unexplained * Math.PI / 180.0. Use them only at that boundary: an angle you hand to a shape is already in the units it wants, and needs no conversion. They are plain arithmetic — no clamping or normalisation, so a value outside [0, 360) converts literally; fold it first with GeometryHelper.NormalizeAngle if that matters. Available wherever you have `using C2VGeometry;`. The one library API that takes radians, VTransform.CreateRotationRadians, reads well as CreateRotationRadians(axis, 90.0.ToRadians()) — though VTransform.CreateRotationDegrees(axis, 90) is the more direct answer there." },
                { "ShapeDefaults", "Static class holding the global style defaults applied to every shape as it is constructed. Each property is nullable and null means \"leave the shape's own default alone\": GlobalColor, GlobalFillColor, GlobalLineWeight, GlobalLineType, GlobalLineTypeScale. One exception to know about: VPoint assigns Color and FillColor to \"White\" outright, so GlobalColor and GlobalFillColor do NOT reach a VPoint; every other shape honours them. Dimension defaults: DimOffset, DimArrowSize, DimTextHeight, DimDecimalPlaces, DimExtendBeyondDimLines, DimOffsetFromOrigin, DimPrefix, DimSuffix, DimTextBgOpaque, DimExtensionLineColor, DimDimensionLineColor, DimTextColor, DimSuppressDimensionLine. Reset() sets them all back to null. Setting a default affects only shapes created afterwards. These values are also populated from Project Settings." },
                { "LineType", "Enum defining the stroke style (line pattern) for shape outlines. Options: Continuous (solid, default), Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden." },
                { "VColor", "Static class of colour STRINGS — every member returns the string that Color and FillColor expect, not a colour object, so shape.Color = VColor.Tomato is the same as shape.Color = \"Tomato\". It exposes 82 named colours as read-only properties: Red, Green, Blue, Yellow, Orange, Purple, Pink, Cyan, Magenta, White, Black, Gray, Brown, Coral, Crimson, DarkBlue, DarkGreen, DarkRed, DarkOrange, DarkViolet, DeepPink, DeepSkyBlue, DodgerBlue, ForestGreen, Fuchsia, Gold, GreenYellow, HotPink, IndianRed, Indigo, Khaki, Lavender, LawnGreen, LightBlue, LightCoral, LightGreen, LightPink, LightSalmon, LightSeaGreen, LightSkyBlue, LightYellow, Lime, LimeGreen, Maroon, MediumBlue, MediumOrchid, MediumPurple, MediumSeaGreen, MediumSlateBlue, MediumSpringGreen, MediumTurquoise, MediumVioletRed, MidnightBlue, Navy, Olive, OliveDrab, OrangeRed, Orchid, PaleGreen, PaleTurquoise, PaleVioletRed, Peru, Plum, RoyalBlue, Salmon, SandyBrown, SeaGreen, Sienna, Silver, SkyBlue, SlateBlue, SlateGray, SpringGreen, SteelBlue, Tan, Teal, Thistle, Tomato, Turquoise, Violet, Wheat, YellowGreen. Construction: FromRgb(r, g, b) and FromArgb(a, r, g, b) return hex strings, WithOpacity(r, g, b, opacity) takes opacity as 0.0-1.0 and returns #AARRGGBB, FromEnum(ColorName) converts the enum. Randomisation: GetRandomColor(returnPastelColor = true), GetRandomPastelColor(), GetRandomVibrantColor(), and the palettes behind them, GetPastelColors() and GetVibrantColors(), both string[] — handy as a ChartOptions.Palette. Any WPF colour name or #RRGGBB / #AARRGGBB string works too; VColor exists so the names are discoverable and typo-proof." },
                { "ColorName", "Enum of the same 82 colour names VColor exposes as properties, for when you want a colour as a value you can switch on or store rather than a string: Red, Green, Blue, Yellow, Orange, Purple, Pink, Cyan, Magenta, White, Black, Gray, Brown and the 69 extended names (Coral through YellowGreen). Convert with VColor.FromEnum(ColorName.Crimson), which returns the string that Color and FillColor take." },

                // Animation
                { "DoodleSharp.Animation", "Contains classes for animating shapes over time. Two models: Frame for per-frame callbacks that reschedule themselves (the requestAnimationFrame pattern), and Animator for a finite timeline that can be scrubbed and exported to GIF or video." },
                { "Frame", "Per-frame callbacks, in the shape JavaScript uses: a function that asks for the next frame. Frame.Request(callback) queues it and returns a handle; call Frame.Request again from inside the callback to keep going, and simply stop asking to end. The callback receives elapsed seconds since the loop started - write motion as a function of that rather than accumulating state and it stays frame-rate independent. Frame.Cancel(handle) removes a queued callback. Requesting during a callback runs on the NEXT frame, never the current one. Use this for open-ended, interactive or procedural motion; use Animator when you need a finite sequence you can scrub or export, which a self-rescheduling callback cannot provide." },
                { "Animator", "Main class for creating animations. Manages sequencing automatically - animations added with AddToAnimations() play sequentially; pass a List<Animation> for parallel playback. Use Pause(seconds) to insert a time gap between animations. Call Animate() to start, Stop() to end. Properties: Duration (read-only total in seconds), Repeat (default false; when true each animation loops independently on its own duration), Speed (playback multiplier, default 1.0, shared with the toolbar speed slider), Fps (target frame rate, default 60, clamped to 1-120). Adding an animation also places its target on the canvas if it is not already there. Only one Animator plays at a time - Animate() replaces the active timeline, so put every animation into a single Animator." },
                { "Animation", "Abstract base class for all animations. An animation attaches to one Shape and runs for a fixed Duration in seconds; the timeline feeds it a normalized time t (0 at its start, 1 at its end) which is passed through EasingFunction before being written into the target. Members: Target (the shape, null for ObjectPropertyAnimation), Duration, StartTime (assigned by the Animator when you add it), EasingFunction (defaults to EasingFunctions.Linear; any Func<double,double> works), Name (optional label for the timeline panel track), Apply(t) (called by the timeline, not by you)." },
                { "DrawAnimation", "Animates the DrawFactor property to progressively draw a shape from 0% to 100%. Constructor: new DrawAnimation(shape, duration). Sets DrawFactor to 0 at construction so the shape stays invisible until its turn; a VGroup target is set recursively so children draw along with it." },
                { "MoveAnimation", "Animates moving a shape by a displacement vector, writing OffsetX/OffsetY. Constructor: new MoveAnimation(shape, displacement, duration). The displacement is relative to wherever the shape sits when this animation starts (the starting offset is captured then, not at construction), so chained moves accumulate. The Z component is ignored." },
                { "PathAnimation", "Animates a shape along any ICurve path (arc, bezier, spline, polyline, etc.). The centre of the target's bounding box is placed on path.PointAtParameter(t) each frame, so it follows the exact curve from start to end. Constructor: new PathAnimation(shape, path, duration). The path is used purely as maths - call path.Hide() if you do not want the curve itself drawn." },
                { "RotateAnimation", "Animates rotating a shape around a pivot point, writing RotationAngle and RotationPivot. Constructor: new RotateAnimation(shape, pivot, angleDegrees, duration). The angle is in degrees counter-clockwise and is added to the shape's current rotation; pass a negative angle to rotate clockwise. It works on EVERY shape type — lines, circles, arcs, ellipses, polygons, rectangles, polylines, beziers, splines, text, arrows, groups, hatches and regions alike — because the renderer applies RotationAngle uniformly rather than each shape opting in. Note that rotation is a render-time transform: Contains, DistanceTo and click-to-select still work against the shape's unrotated geometry, so a hit test on a rotated shape answers for where it was drawn before the turn. (VRectangle is the exception, rebuilding its corners, so its point queries do follow the rotation.)" },
                { "FlipAnimation", "Animates flipping (mirroring) a shape across an axis line, writing FlipProgress and FlipAxis. Constructor: new FlipAnimation(shape, mirrorAxis, duration). It always drives progress to a complete mirror (1.0) from wherever the shape currently is; the VLine is read for its geometry only." },
                { "TransformAnimation", "Animates one shape morphing into another (e.g. a VLine unfurling into a VCircle). Both outlines are sampled into matched point sets (64-360 points, scaled to the more detailed of the two) and interpolated point-by-point through an internally-managed VPolyline proxy - the only thing on screen during the transition. The source shows before it starts, the real destination (with its own fill and styling) is revealed when it completes, and the proxy's colours switch from the source's to the destination's at the halfway point. Curve shapes are sampled along their real geometry; non-curve shapes (VText, VArrow, ...) fall back to their bounding-box outline; a VGroup morphs by its longest child contour. Constructors: new TransformAnimation(fromShape, toShape, duration) - throws ArgumentNullException on a null shape; and new TransformAnimation(vtext, charIndex, toShape, duration) to morph a single character of a VText into a shape - the word stays visible and the character is replaced with a space exactly when its morph starts, so it reads as the letter itself transforming (throws ArgumentException when the character has no outline: whitespace, out of range, or no glyph provider)." },
                { "FadeInAnimation", "Animates fading in a shape from transparent to opaque, writing Opacity. Constructor: new FadeInAnimation(shape, duration). Sets Opacity to 0 at construction, recursing into VGroup children." },
                { "FadeOutAnimation", "Animates fading out a shape from opaque to transparent, writing Opacity. Constructor: new FadeOutAnimation(shape, duration, targetOpacity) where targetOpacity defaults to 0 (fully transparent) - pass 0.3 to fade to 30%. Sets Opacity to 1 at construction, recursing into VGroup children." },
                { "ValueAnimation", "Animates any numeric (double) property on a shape (T must be a Shape). Supports two constructors: new ValueAnimation<T>(shape, c => c.Property, startValue, endValue, duration) for start/end interpolation, or new ValueAnimation<T>(shape, c => c.Property, new List<double> { v1, v2, v3, ... }, duration) to animate through a sequence of values evenly spaced over the duration. The selector must be a plain property access - anything else throws ArgumentException - and the list form needs at least two values. The first value is applied immediately at construction." },
                { "ObjectPropertyAnimation", "Animates any numeric (double) property on an arbitrary object (T : class, not limited to shapes). Constructor: new ObjectPropertyAnimation<T>(obj, o => o.Property, startValue, endValue, duration). Target is null because there is no shape to drive, so nothing is auto-drawn - your property setter is what moves the geometry. Same selector rules as ValueAnimation." },
                { "EasingFunctions", "Static class providing common easing functions for smooth animations: Linear, EaseInQuad, EaseOutQuad, EaseInOutQuad, EaseInCubic, EaseOutCubic, EaseInOutCubic. Each takes normalized time t and returns eased t. Assign one to an animation's EasingFunction property; because it is a plain Func<double,double>, a custom lambda works just as well." },

                // Boolean Operations
                { "BooleanOps", "Static class providing polygon boolean operations using the robust Clipper2 library. Supports Union (combine polygons — returns a single VPolygon, or null when it cannot form one; a null result EXPLAINS ITSELF in the console via GeometryDiagnostics, naming the case: no polygons passed, an empty result, or N disjoint regions because the inputs never touched), UnionAll (the answer when a single polygon is not required — takes any number of polygons and returns List<VPolygon> of every resulting piece, never null), Intersect (overlapping area), Difference (subtract), Xor (symmetric difference), OffsetPolygon (grow/shrink), OffsetPolygonSafe (safe inward offset), MaxSafeInwardOffset, MakeSimple (resolve self-intersections), HasSelfIntersections, Simplify (Douglas-Peucker algorithm), Area calculation, and PointInPolygon (ray casting). Also provides WithHoles variants (DifferenceWithHoles, IntersectWithHoles, UnionWithHoles) that return PolygonWithHoles objects. It additionally forwards Region work to RegionBooleanOps, but only through the two-argument and IEnumerable<Region> overloads — there is deliberately NO params Region[] form here, because it would make the argument-less BooleanOps.Union() ambiguous with params VPolygon[]. Call RegionBooleanOps.Union(a, b, c) when you want the params form for regions." },
                { "PolygonWithHoles", "Represents a polygon with an outer boundary and optional inner holes. Created via BooleanOps WithHoles methods or directly. Constructor: new PolygonWithHoles(outer) or new PolygonWithHoles(outer, holes). Properties: Outer (VPolygon), Holes (List<VPolygon>), Area (outer minus holes). Methods: AddHole(hole), Contains(point), Clone()." },
                { "Region", "Represents an enclosed 2D region bounded by curves (lines, arcs, splines, beziers). Unlike VPolygon which only supports straight edges, Region preserves original curve geometry in its boundary loops. A Region has an OuterLoop (ordered list of ICurve forming a closed boundary) and optional Holes. Constructors: new Region(curves), new Region(outerCurves, holes), new Region(closedCurve) — build directly from a single closed curve (circle, ellipse, closed polygon/polyline/spline/bezier); the source curve is consumed (removed from the canvas) so its outline isn't drawn twice. Static factories: Region.FromPolygon(polygon), Region.FromPolygonWithHoles(pwh). Properties: OuterLoop, Holes, Area (outer minus holes), SignedArea, Perimeter. Methods: AddHole(curves), Contains(point) (inside the outer loop and outside every hole), DistanceTo(point) (to the nearest boundary — outer loop or hole edge — handling both VLine edges and curved segments), ToPolygon(), ToPolygonHighRes(segments), ToPolygonWithHoles(segments), Clone(), Move(), Rotate(), Flip(), Scale(), GetBounds(). Curves are automatically ordered to form a continuous closed loop; self-intersection validation is enforced." },
                { "RegionBooleanOps", "Static class providing boolean operations on Regions. Operations approximate region boundaries to high-resolution polygons, clip them with the Clipper2 library, then wrap the results back as Regions. Methods: Union(a, b), Intersect(a, b), Difference(a, b), Xor(a, b). All four also accept a whole collection — Union/Intersect/Difference/Xor(IEnumerable<Region>) and (params Region[]) — folding across every region: Union = merged area, Intersect = area common to all, Difference = first minus the rest, Xor = running symmetric difference. WithHoles variants: UnionWithHoles, IntersectWithHoles, DifferenceWithHoles. Analysis: PointInRegion(region, point), Area(region). The BooleanOps class also exposes region overloads that forward here." },
                { "VPolygonBooleanExtensions", "Extension methods that put BooleanOps on the polygon itself: polygon.Union(other) (VPolygon? — null when the two stay disjoint), polygon.Difference(other), polygon.Xor(other) (each List<VPolygon>), polygon.OffsetPolygon(distance), polygon.OffsetPolygonSafe(distance), polygon.MaxSafeInwardOffset(), polygon.MakeSimple(), polygon.HasSelfIntersections(), polygon.Contains(point) and polygon.GetArea() (unsigned). ONE OF THEM IS UNREACHABLE: the Intersect extension is shadowed, because VPolygon already declares IntersectionResult Intersect(ICurve) and an instance method always beats an extension method — so polygon.Intersect(other) returns the points where the two OUTLINES cross, not the overlapping area. Call BooleanOps.Intersect(a, b) for the boolean; the other three are fine in dotted form. The extension overloads take no JoinType/EndType — call BooleanOps.OffsetPolygon for those. Results are unnamed shapes, so name them or call Place() to keep them visible." },
                { "RegionBooleanExtensions", "Extension methods for Region boolean operations, giving instance-method syntax: region.Union(other), region.Difference(other), region.Xor(other), region.ContainsPoint(point), region.GetArea(). ONE OF THEM IS UNREACHABLE: the Intersect extension is shadowed by the inherited Shape.Intersect(Shape), because an instance method always beats an extension method — and Region does not override it, so region.Intersect(other) compiles and ALWAYS RETURNS NULL. Always call RegionBooleanOps.Intersect(a, b) instead. The other five have no instance counterpart and work as written." },
                { "JoinType", "Enum for polygon offset join style. Values: Miter (sharp corners, default), Round (rounded corners), Square (squared-off corners). Used with BooleanOps.OffsetPolygon." },
                { "EndType", "Enum for polygon offset end style. Values: Polygon (closed polygon, default), OpenRound (rounded open ends), OpenSquare (squared open ends), OpenButt (flat cut open ends). Used with BooleanOps.OffsetPolygon." },

                // Hatch Patterns
                { "VHatch", "Fills a closed polygon boundary with a repeating line pattern. Supports 72 built-in AutoCAD-standard patterns (via BuiltInHatch enum or name string). Note that VHatch is NOT in the auto-naming rewriter's type list, so `var h = new VHatch(...)` still ends up unnamed and is hidden after Main() returns — set Name in the initializer or call Place(); and custom patterns defined using the .pat format. Constructors: new VHatch(polygon, BuiltInHatch.ANSI31, scale, angle), new VHatch(polygon, \"BRICK\", scale, angle), new VHatch(polygon, hatchType, scale, angle), new VHatch(boundaryPoints, pattern, scale, angle). Static factory: VHatch.FromDefinition(polygon, patString, scale, angle). Properties: Boundary (List<VXYZ>), Pattern (HatchType), PatternScale (double), PatternAngle (double), Color, LineWeight, Opacity. Methods: GenerateLines() returns clipped line segments, Clone(), Move(), Rotate(), Flip(), Scale(), GetBounds(), Contains(point) (an exact test against the boundary, not the bounding box), DistanceTo(point) (to the boundary treated as a closed path)." },
                { "HatchType", "Defines a hatch pattern composed of one or more line families following the AutoCAD .pat format. Properties: Name, Description, Lines (List<HatchPatternLine>) — all settable. Constructors: new HatchType() for an empty pattern, new HatchType(name, description, lines). Static methods: Parse(string patDefinition) parses from .pat format string, GetBuiltIn(string name) or GetBuiltIn(BuiltInHatch enum) retrieves a built-in pattern (forwarding to BuiltInHatches.Get, so it too hands back a fresh copy). Instance method: Clone() returns a deep copy, cloning every line family, so you can adapt a pattern without touching the one you copied it from." },
                { "HatchPatternLine", "A single line definition within a hatch pattern. Properties: Angle (degrees), OriginX, OriginY, DeltaX (shift along line between rows), DeltaY (spacing between parallel lines), Dashes (double[] - positive=dash, negative=gap, 0=dot, empty=continuous). All are settable. Constructors: new HatchPatternLine() and new HatchPatternLine(angle, originX, originY, deltaX, deltaY, params double[] dashes). Clone() returns a deep copy, including a copy of the Dashes array." },
                { "BuiltInHatch", "Enum of 72 built-in hatch patterns from the AutoCAD pattern library. Members use an underscore where the pattern name has a hyphen (BuiltInHatch.AR_BRSTD is the \"AR-BRSTD\" pattern). Values include: SOLID, ANGLE, ANSI31-ANSI38, AR_B816, AR_B816C, AR_B88, AR_BRELM, AR_BRSTD, AR_CONC, AR_HBONE, AR_PARQ1, AR_RROOF, AR_RSHKE, AR_SAND, BOX, BRASS, BRICK, BRSTONE, CLAY, CORK, CROSS, DASH, DOLMIT, DOTS, EARTH, ESCHER, FLEX, GOST_GLASS, GOST_WOOD, GOST_GROUND, GRASS, GRATE, GRAVEL, HEX, HONEY, HOUND, INSUL, LINE, MUDST, NET, NET3, PLAST, PLASTI, SACNCR, SQUARE, STARS, STEEL, SWAMP, TRANS, TRIANG, ZIGZAG, and ACAD_ISO02W100 through ACAD_ISO15W100." },
                { "BuiltInHatches", "Static registry of the 72 built-in hatch patterns. Methods: Get(string name) or Get(BuiltInHatch enum) retrieves a pattern (case-insensitive; an unknown name throws ArgumentException), GetAllNames() returns every available pattern name. BOTH Get overloads return a FRESH COPY on every call, so the pattern you get back is yours to modify — adjusting its angle, spacing or dashes cannot affect a later lookup of the same name. The cache holds the parsed template behind the copy, so repeated lookups stay cheap." },
                { "HatchGenerator", "Static class that generates hatch line segments from a HatchType pattern clipped to a polygon boundary. Generate(List<VXYZ> boundary, HatchType pattern, double scale, double patternAngle) returns List<(VXYZ Start, VXYZ End)> — pure geometry, nothing is created or registered on the canvas, which makes it the way to hatch something without a VHatch shape. scale multiplies the pattern spacing, dash lengths and origin; patternAngle (degrees) is added to every line family's own angle. Returns an empty list when the boundary has fewer than 3 points or the pattern has no line families, and skips any family that would need more than 10,000 parallel lines. A dot (0 in Dashes) comes back as a zero-length segment. This is what VHatch.GenerateLines() calls." },

                // Array Operations
                { "ArrayOps", "Static class providing array and pattern generation for shapes. Every method clones the source shape and returns a List<Shape>: LinearArray (count shapes total along a direction vector, which is normalised so spacing is in world units), RectangularArray (rows × cols grid, +X and +Y from the original), CircularArray (count shapes total around a centre; a full 360° sweep divides by count, a partial sweep by count-1, and rotateItems: false moves copies without turning them), PathArray (count clones spread by arc length along any ICurve — the original is not in the list), SpiralArray (count clones from startRadius to endRadius over totalRevolutions — also excludes the original), and Mirror (returns [original, mirrored copy]). A count, rows or cols of zero or less returns an empty list. The clones have no Name, so call .DrawAll() on the result (or set names) — otherwise the post-run pass that hides unnamed shapes removes them." },
                { "ShapeArrayExtensions", "Extension methods that put ArrayOps on the shape itself: shape.LinearArray(direction, count, spacing), shape.LinearArrayX(count, spacing), shape.LinearArrayY(count, spacing), shape.RectangularArray(rows, cols, rowSpacing, colSpacing), shape.CircularArray(center, count, totalAngleDegrees, rotateItems), shape.PathArray(curve, count, alignToPath), shape.SpiralArray(center, count, startRadius, endRadius, totalRevolutions, rotateItems) and shape.Mirror(mirrorLine). Also adds shapes.DrawAll() on any IEnumerable<Shape>, which marks every shape in the list as explicitly drawn so the unnamed-shape sweep leaves the clones alone — end an array chain with it." },

                // Charts
                { "Chart", "Static helper class for building Chart.js-style charts out of standard C2VGeometry primitives. Each method returns a VGroup containing axes, gridlines, ticks, labels and the data shapes. Methods: Bar(labels, values, options), Line(xs, ys, options), Scatter(points, options), Pie(values, labels, options), Area(xs, ys, options) — the options argument is optional everywhere and defaults to a fresh ChartOptions. Child shapes do not register individually with the canvas; only the returned VGroup is registered, so the whole chart can be moved/rotated/scaled as one unit. Data values are in data units, not canvas units — the chart maps them into the plot rectangle given by ChartOptions.Origin/Width/Height (world coordinates, Y up, origin at canvas centre). Because the group comes back from a method rather than a `new`, it carries no Name: set one (chart.Name = \"revenue\") so the post-run unnamed-shape pass does not hide it." },
                { "GlobalParameters", "Static, project-wide registry of named values that survives across code runs and can be tuned live from the Global Parameters panel (Windows > Global Parameters, or F6). Declare with Set<T>(name, value, min, max, step, group, description) — idempotent, so re-running your code will not discard a value dialled in from the panel unless the declared default itself changed. Read with Get(name) (returns a self-converting ParamValue), Get<T>(name), or Get<T>(name, fallback). Other members: Has, Find, Assign<T> (imperative write), Reset, ResetAll, SetRange, ClearAll, All, Count, and the Changed/Reloaded events. Changing a value re-executes your code, so every derived value updates at once — no dependency graph needed. Supported types are the numeric family (stored as double), bool, string and DateTime; user-defined types are rejected because holding one would keep the user assembly loaded forever. Names are case-insensitive." },
                { "Parameter", "One entry in the GlobalParameters registry. Properties: Name, Kind (ParamKind.Number/Boolean/Text/Date), Value, DefaultValue (the literal declared in code), IsOverridden, Min/Max/Step, RangePinned, Group, Description, SourceFile/SourceLine (the declaring Set(...) call, captured via CallerFilePath/CallerLineNumber so panel edits can be written back into your code). Convenience readers: AsDouble, AsBool, AsText, AsDate, EffectiveMin, EffectiveMax, ToLiteral()." },
                { "ParamKind", "The storage family of a global parameter: Number (every numeric type collapses to double), Boolean, Text, or Date." },
                { "ParamValue", "The self-converting value returned by GlobalParameters.Get(name). Converts implicitly to double, bool, string and DateTime so a parameter reads naturally without a type argument. Named accessors Num, Flag, Text, Date plus Raw, Exists, Name and As<T>() are always unambiguous. Caveat: because it converts to both double and string, the + operator cannot pick an overload — Get(\"n\") + 1 is a compile error; use Get(\"n\").Num + 1 or GlobalParameters.Get<double>(\"n\") + 1. int and float are explicit conversions for the same reason." },
                { "ChartOptions", "Configuration object for Chart.* methods. Properties: Origin (bottom-left of plot in world coords), Width, Height, Title, XAxisTitle, YAxisTitle, XMin/XMax/YMin/YMax (null = auto-fit), XTickCount, YTickCount, ShowGrid, ShowLegend (draws a swatch + label per entry down the right of the plot; honoured by Chart.Bar and Chart.Pie only), XLabelRotation, LabelFontSize (also sets the legend swatch size and row spacing), TitleFontSize, AxisColor, GridColor, TextColor, Palette (string[] of color names cycled across series/bars/slices, and the legend swatch colours), TickDecimalPlaces (null = auto-format)." },

                // Export
                { "DoodleSharp.Export", "Contains classes for exporting shapes and animations to various file formats." },
                { "DxfExporter", "Exports shapes to AutoCAD DXF format (R12 ASCII). Supports all shape types including lines, circles, arcs, ellipses, polygons, polylines, text, and arrows." },
                { "PdfExporter", "Exports shapes to vector PDF format using PdfSharp library. Preserves colors, stroke styles, and produces high-quality vector output suitable for printing." },
                { "SvgExporter", "Exports shapes to SVG (Scalable Vector Graphics) format. Web-compatible vector format that opens in browsers and vector editors. Supports all shape types with full color and styling." },
                { "VideoExporter", "Exports animations to MP4 video using Windows Media Foundation H.264 encoder. Renders vector graphics at target resolution using high DPI for sharp output. Supports resolution presets (Canvas Size, 720p, 1080p, 4K, Custom), configurable frame rate (15-60 FPS), and bitrate (1-20 Mbps). No external dependencies required." },
                { "GifEncoder", "Exports animations to animated GIF format. Supports configurable frame rate and duration. Good for short animations and web sharing." },

                // Canvas and Snap System
                { "DoodleSharp.Canvas", "Contains classes for the interactive canvas, drawing tools, and snap detection system." },
                { "SnapType", "Enumeration of snap point types: Endpoint (line/arc ends), Midpoint (center of segments), Center (circle/ellipse/arc centers), Intersection (where curves cross), Nearest (closest point on curve), Perpendicular (90° from reference point), Extension (line extended beyond endpoint), Tangent (tangent point on circles/arcs)." },
                { "SnapResult", "Represents a detected snap point with its type, position, and distance from cursor. For Extension snaps, includes ExtensionSource (the endpoint the extension originates from) and ExtensionAngle (direction in degrees). For Perpendicular/Tangent snaps, includes ReferenceSource (your first click) and ConstraintPoint (the perpendicular/tangent point on the shape)." },
                { "SnapEngine", "Engine for detecting snap points on shapes. Supports 8 snap types (Endpoint, Midpoint, Center, Intersection, Nearest, Perpendicular, Extension, Tangent). Each snap type can be individually enabled/disabled via Settings. Uses spatial indexing for efficient detection even with many shapes." },
                { "DrawingInputMode", "Enumeration for precise input modes while drawing: None (mouse-controlled), Distance (typing distance value), Angle (typing angle value). Press Tab to cycle between modes when drawing. Type numbers to enter precise values, Enter to confirm." },
                { "DrawingTool", "Manages interactive drawing state and shape creation. Supports all shape types with visual preview. Features: snap detection with 8 snap types, orthogonal constraint (Shift key), precise distance/angle input (Tab to cycle, type value, Enter to confirm). The InputMode property indicates current input state; InputBuffer holds the typed value." },

                // Console
                { "DoodleSharp.Console", "Console output for project code. VizConsole.Log(...) writes to the console panel below the canvas." },
                { "VizConsole", "Static class providing console output. Log(value, itemize = true) is the only method - there is no Write() or WriteLine(). It prints value.ToString() (an empty line for null) to the console panel, prefixed with the calling file name and line number, both captured automatically. When itemize is true (the default) and value is a collection - any IEnumerable other than a string - each item is printed on its own line and an empty collection prints \"(empty)\"; pass false to print the collection's own ToString() instead." },
            };
        }

        public string GetSummary(string name)
        {
            if (_summaries.TryGetValue(name, out var summary))
                return summary;
            return "No description available.";
        }

        public List<Type> GetDocumentableTypes()
        {
            var types = new List<Type>();
            foreach (var assembly in _assemblies)
            {
                try
                {
                    types.AddRange(assembly.GetTypes());
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types.AddRange(ex.Types.Where(t => t != null)!);
                }
            }

            return types
                .Where(t => t.IsPublic && (t.IsClass || t.IsAbstract) && t.Namespace != null &&
                    _namespacePrefixes.Any(p => t.Namespace == p || t.Namespace.StartsWith(p + ".") || t.Namespace.StartsWith(p)))
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Name)
                .ToList();
        }

        public FlowDocument GenerateDocForType(Type type)
        {
            var doc = new FlowDocument();
            doc.FontFamily = new FontFamily("Segoe UI");
            doc.PagePadding = new Thickness(20);
            doc.ColumnWidth = double.NaN; // Force single column mode

            // Title
            var displayName = GetDisplayTypeName(type);
            var cleanName = GetCleanTypeName(type);

            var title = new Paragraph(new Run(displayName + " Class"))
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateGray,
                Margin = new Thickness(0, 0, 0, 10)
            };
            doc.Blocks.Add(title);

            // Summary
            var summaryText = GetSummary(cleanName);
            doc.Blocks.Add(new Paragraph(new Run(summaryText)) { FontSize = 14, Margin = new Thickness(0, 0, 0, 20) });

            // Inheritance
            AddSectionHeader(doc, "Inheritance Hierarchy");
            doc.Blocks.Add(GenerateInheritance(type));

             // C# Samples
            AddSectionHeader(doc, "C# Sample Code");
            if (_csharpSamples == null) InitializeCSharpSamples();
            if (_csharpSamples.TryGetValue(cleanName, out var sample))
            {
                 var p = new Paragraph(new Run(sample));
                 p.FontFamily = new FontFamily("Consolas");
                 p.Background = Brushes.WhiteSmoke;
                 p.Padding = new Thickness(10);
                 doc.Blocks.Add(p);
            }
            else
            {
                 doc.Blocks.Add(new Paragraph(new Run("// No specific usage example available.")) { FontFamily = new FontFamily("Consolas"), Padding = new Thickness(5) });
            }

            // Syntax

            // Syntax
            AddSectionHeader(doc, "Syntax");
            doc.Blocks.Add(GenerateSyntax(type));

            // Constructors
            var dtors = type.GetConstructors();
            if (dtors.Length > 0)
            {
                AddSectionHeader(doc, "Constructors");
                doc.Blocks.Add(GenerateMemberTable(dtors, cleanName));
            }

            // Properties
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (props.Length > 0)
            {
                AddSectionHeader(doc, "Properties");
                doc.Blocks.Add(GenerateMemberTable(props, cleanName));
            }

            // Methods
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)) // Exclude getter/setter internal methods and Object methods
                .ToArray();

            if (methods.Length > 0)
            {
                AddSectionHeader(doc, "Methods");
                doc.Blocks.Add(GenerateMemberTable(methods, cleanName));
            }

            return doc;
        }

        private void AddSectionHeader(FlowDocument doc, string text)
        {
            doc.Blocks.Add(new Paragraph(new Run(text))
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Teal,
                Margin = new Thickness(0, 10, 0, 5),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 5)
            });
        }

        private Paragraph GenerateInheritance(Type type)
        {
            var p = new Paragraph();
            var hierarchy = new List<Type>();
            var current = type;
            while (current != null)
            {
                hierarchy.Insert(0, current);
                current = current.BaseType;
            }

            for (int i = 0; i < hierarchy.Count; i++)
            {
                var run = new Run(GetDisplayTypeName(hierarchy[i]));
                if (i == hierarchy.Count - 1) run.FontWeight = FontWeights.Bold;
                p.Inlines.Add(run);
                if (i < hierarchy.Count - 1) p.Inlines.Add(" → ");
            }
            return p;
        }

        private Paragraph GenerateSyntax(Type type)
        {
            var syntax = $"public class {GetDisplayTypeName(type)}";
            if (type.BaseType != null && type.BaseType != typeof(object))
                syntax += $" : {GetDisplayTypeName(type.BaseType)}";

            var interfaces = type.GetInterfaces();
            if (interfaces.Length > 0)
            {
                syntax += (type.BaseType != null && type.BaseType != typeof(object) ? ", " : " : ");
                syntax += string.Join(", ", interfaces.Select(i => GetDisplayTypeName(i)));
            }

            var p = new Paragraph(new Run(syntax))
            {
                FontFamily = new FontFamily("Consolas"),
                Background = Brushes.WhiteSmoke,
                Padding = new Thickness(10)
            };
            return p;
        }

        private Table GenerateMemberTable(MemberInfo[] members, string className = "")
        {
            var table = new Table();
            table.CellSpacing = 0;
            table.BorderBrush = Brushes.LightGray;
            table.BorderThickness = new Thickness(1);

            // Use fixed widths: Name, Type/Signature, Description
            table.Columns.Add(new TableColumn { Width = new GridLength(150) }); // Name
            table.Columns.Add(new TableColumn { Width = new GridLength(220) }); // Type/Signature
            table.Columns.Add(new TableColumn { Width = new GridLength(400) }); // Description

            var rowGroup = new TableRowGroup();

            // Header
            var headerRow = new TableRow();
            headerRow.Background = Brushes.AliceBlue;
            headerRow.Cells.Add(CreateHeaderCell("Name"));
            headerRow.Cells.Add(CreateHeaderCell("Type / Signature"));
            headerRow.Cells.Add(CreateHeaderCell("Description"));
            rowGroup.Rows.Add(headerRow);

            bool isAlt = false;
            foreach (var member in members)
            {
                var row = new TableRow();
                if (isAlt) row.Background = Brushes.WhiteSmoke;
                isAlt = !isAlt;

                // Name column
                var nameText = new Run(member.Name) { FontWeight = FontWeights.Bold, Foreground = Brushes.DarkBlue };
                var nameCell = new TableCell(new Paragraph(nameText)) { Padding = new Thickness(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0,0,0,1) };
                row.Cells.Add(nameCell);

                // Type/Signature column
                string sig = "";
                string returnType = "";
                if (member is MethodInfo mi)
                {
                    var paramStr = string.Join(", ", mi.GetParameters().Select(p => $"{GetFriendlyTypeName(p.ParameterType)} {p.Name}"));
                    returnType = GetFriendlyTypeName(mi.ReturnType);
                    sig = $"{returnType} ({paramStr})";
                }
                else if (member is PropertyInfo pi)
                {
                    sig = GetFriendlyTypeName(pi.PropertyType);
                    var accessors = new List<string>();
                    if (pi.CanRead) accessors.Add("get");
                    if (pi.CanWrite) accessors.Add("set");
                    if (accessors.Count > 0)
                        sig += $" {{ {string.Join("; ", accessors)} }}";
                }
                else if (member is ConstructorInfo ci)
                {
                    var paramStr = string.Join(", ", ci.GetParameters().Select(p => $"{GetFriendlyTypeName(p.ParameterType)} {p.Name}"));
                    sig = string.IsNullOrEmpty(paramStr) ? "()" : $"({paramStr})";
                }

                var sigPara = new Paragraph(new Run(sig));
                sigPara.FontFamily = new FontFamily("Consolas");
                sigPara.FontSize = 11;
                sigPara.Foreground = Brushes.DarkSlateGray;
                sigPara.TextAlignment = TextAlignment.Left;
                var sigCell = new TableCell(sigPara) { Padding = new Thickness(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0,0,0,1), TextAlignment = TextAlignment.Left };
                row.Cells.Add(sigCell);

                // Description column
                var description = GetMemberDescription(className, member.Name);
                if (string.IsNullOrEmpty(description))
                {
                    // Try base class descriptions for inherited members
                    description = GetMemberDescription("Shape", member.Name);
                }
                if (string.IsNullOrEmpty(description))
                {
                    description = GetMemberDescription("ICurve", member.Name);
                }
                var descPara = new Paragraph(new Run(description));
                descPara.FontSize = 11;
                descPara.Foreground = string.IsNullOrEmpty(description) ? Brushes.Gray : Brushes.Black;
                if (string.IsNullOrEmpty(description))
                    descPara.Inlines.Clear();
                var descCell = new TableCell(descPara) { Padding = new Thickness(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0,0,0,1) };
                row.Cells.Add(descCell);

                rowGroup.Rows.Add(row);
            }

            table.RowGroups.Add(rowGroup);
            return table;
        }

        private string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(int)) return "int";
            if (type == typeof(double)) return "double";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";

            if (type.IsGenericType)
            {
                var baseName = type.Name;
                var tickIndex = baseName.IndexOf('`');
                if (tickIndex > 0)
                    baseName = baseName.Substring(0, tickIndex);
                var args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
                return $"{baseName}<{args}>";
            }

            return type.Name;
        }

        /// <summary>
        /// Returns the type name without the generic arity suffix (e.g., "ValueAnimation" instead of "ValueAnimation`1").
        /// Used for dictionary lookups and display where generic parameters aren't needed.
        /// </summary>
        internal static string GetCleanTypeName(Type type)
        {
            var name = type.Name;
            var tickIndex = name.IndexOf('`');
            return tickIndex > 0 ? name.Substring(0, tickIndex) : name;
        }

        /// <summary>
        /// Returns a display-friendly type name with generic parameters (e.g., "ValueAnimation&lt;T&gt;").
        /// </summary>
        internal static string GetDisplayTypeName(Type type)
        {
            var cleanName = GetCleanTypeName(type);
            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                var argNames = string.Join(", ", args.Select(a => a.IsGenericParameter ? a.Name : GetCleanTypeName(a)));
                return $"{cleanName}<{argNames}>";
            }
            return cleanName;
        }

        private TableCell CreateHeaderCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold })
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(5)
            };
        }

        private void InitializeCSharpSamples()
        {
            _csharpSamples = new Dictionary<string, string>
            {
                // Basic shapes
                { "VPoint", @"// A VPoint is a DRAWN dot. For coordinates and vectors use VXYZ —
// constructing a VPoint puts a marker on the canvas.
var p = new VPoint(100, 200);   // origin is the canvas centre, Y points up
p.Color = ""Red"";                // outline
p.FillColor = ""Red"";            // both default to White

// From a VXYZ, and back again
var pos = new VXYZ(-50, 25);
var marker = new VPoint(pos);
VXYZ back = marker.AsVXYZ();     // also converts implicitly: VXYZ v = marker;

// X and Y are settable; arithmetic always yields a plain VXYZ,
// so intermediates never leave stray dots behind.
marker.X += 10;
VXYZ sum = marker + new VXYZ(5, 5);

// Polar placement: rotate a radius vector, then offset from the centre
var centre = new VXYZ(0, 0);
var atFortyFive = new VPoint(centre + new VXYZ(100, 0).Rotate(45));" },

                { "VLine", @"// Create a line from two points (endpoints are Start and End, both VXYZ)
var line = new VLine(new VXYZ(0, 0), new VXYZ(100, 50));
line.Color = ""Cyan"";
line.LineWeight = 2;

// Or using coordinates directly
var line2 = new VLine(0, 100, 150, 100);

// Or from a start point, angle (degrees CCW from +X), and length
var line3 = new VLine(new VXYZ(0, 0), 45, 100);

// Read and reshape
VXYZ mid = line.MidPoint;
VXYZ dir = line.Direction;        // unit vector
double len = line.GetLength();
line.End = new VXYZ(200, 50);     // Start/End are settable

// Curve operations (VLine implements ICurve)
VXYZ onLine = line.Project(new VXYZ(60, 90));   // closest point, clamped to the segment
var quarters = line.Divide(4);                   // 5 points, ends included
line3.SetBounds(0.25, 0.75);                     // trim in place to the middle half" },

                { "VXLine", @"// Create an infinite construction line through a point with direction
var xline = new VXLine(new VXYZ(0, 0), new VXYZ(1, 1, 0));
xline.Color = ""Gray"";
xline.LineType = LineType.DashDot;

// THROUGH TWO POINTS is the FOUR-COORDINATE overload, not two VXYZ.
// There is no VXLine(VXYZ point1, VXYZ point2): the two-VXYZ form is
// (basePoint, DIRECTION), so passing a second point there silently gives a line
// through the origin-ward direction instead of one through both points.
var xline2 = new VXLine(0, 50, 100, 90);            // through (0,50) and (100,90)
var wrong  = new VXLine(new VXYZ(0, 50), new VXYZ(100, 90));   // direction (100,90) FROM (0,50)

// The two-VXYZ form spelled correctly for two points: subtract to get the direction
var p1 = new VXYZ(0, 50);
var p2 = new VXYZ(100, 90);
var xline3 = new VXLine(p1, p2 - p1);

// Static helpers for horizontal and vertical lines
var hLine = VXLine.Horizontal(100);  // Horizontal at Y=100
var vLine = VXLine.Vertical(50);     // Vertical at X=50

// RenderExtent controls how far it is drawn (default 10000); the geometry is
// still infinite, so GetLength() is +Infinity and GetBounds() is non-finite.
xline.RenderExtent = 500;
VLine finite = xline.ToFiniteLine();      // clip to RenderExtent as a real segment
                                          // NOT drawn — call finite.Place() if you want it
var (a, b) = xline.GetTwoPoints();        // two points defining the line

// Use for slicing polygons — Slice takes two points, so feed it GetTwoPoints().
// There is no Slice(VXLine) overload; this is how you cut with a construction line.
var polygon = new VPolygon(new VXYZ(0,0), new VXYZ(100,0), new VXYZ(100,100), new VXYZ(0,100));
List<VPolygon> sliced = polygon.Slice(a, b);
foreach (var piece in sliced) piece.Place();" },

                { "VRay", @"// Create a ray from an origin in a direction (the direction is normalised for you)
var ray = new VRay(new VXYZ(0, 0), new VXYZ(1, 0.5, 0));
ray.Color = ""Orange"";

// ORIGIN-THROUGH-A-POINT is the FOUR-COORDINATE overload, not two VXYZ.
// There is no VRay(VXYZ origin, VXYZ throughPoint): the two-VXYZ form is
// (origin, DIRECTION), so a second point passed there is read as a direction
// measured from (0,0) — which aims the ray somewhere else entirely.
var ray2 = new VRay(50, 50, 100, 75);                          // from (50,50) toward (100,75)
var wrong = new VRay(new VXYZ(50, 50), new VXYZ(100, 75));     // direction (100,75) FROM (50,50)

// The two-VXYZ form spelled correctly for a through-point: subtract
var from = new VXYZ(50, 50);
var toward = new VXYZ(100, 75);
var ray3 = new VRay(from, toward - from);

// Static helpers for common rays
var rightRay  = VRay.HorizontalRight(new VXYZ(0, 0));
var leftRay   = VRay.HorizontalLeft(new VXYZ(0, 0));
var upRay     = VRay.VerticalUp(new VXYZ(100, 0));
var downRay   = VRay.VerticalDown(new VXYZ(100, 0));
var angledRay = VRay.AtAngle(new VXYZ(0, 0), 45);  // 45 degrees CCW from +X

// Semi-infinite: GetLength() is +Infinity and RenderExtent (default 10000)
// only controls how far it is painted.
VXYZ at50 = ray.GetPointAtDistance(50);
bool onIt  = ray.ContainsPoint(at50);      // false behind the origin
// Both conversions return a real shape that is NOT drawn — converting a ray for
// a calculation should not add a second line. Call .Place() if you want it shown.
VLine seg  = ray.ToFiniteLine();           // clip to RenderExtent
VXLine full = ray.ToXLine();               // extend backwards too" },

                { "VCircle", @"// Create a circle with center and radius
var circle = new VCircle(new VXYZ(50, 50), 30);
circle.Color = ""Yellow"";
circle.FillColor = ""#4000FFFF""; // Semi-transparent cyan

// Or using coordinates
var circle2 = new VCircle(100, 100, 25);

// Circumcircle through 3 points (throws ArgumentException if they are collinear)
var circumcircle = new VCircle(new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(50, 80));

// Static factories
var byDiameter = VCircle.FromCenterDiameter(new VXYZ(0, 0), 120);
var byEndpoints = VCircle.FromTwoPoints(new VXYZ(-40, 0), new VXYZ(40, 0));

// Computed properties and curve operations
double area = circle.Area;              // pi * r^2
double perimeter = circle.Circumference; // 2 * pi * r
var twelve = circle.Divide(12);          // 13 points CCW from angle 0
VXYZ quarter = circle.PointAtParameter(0.25);
bool inside = circle.Contains(new VXYZ(50, 60));   // true, disc test
// circle.SetBounds(...) throws — a trimmed circle is an arc; use SplitAtPoint" },

                { "VRectangle", @"// Create a rectangle (bottom-left corner, width, height)
var rect = new VRectangle(new VXYZ(10, 10), 80, 50);
rect.Color = ""LimeGreen"";
rect.FillColor = ""#2000FF00"";

// Or using coordinates
var rect2 = new VRectangle(100, 0, 60, 40);

// Create from two corner points (bottom-left and top-right)
var rect3 = new VRectangle(new VXYZ(0, 0), new VXYZ(100, 75));

// Corner / Width / Height / RotationAngle rebuild the corner points when set.
// RotationAngle OVERRIDES Shape.RotationAngle, so there is one property:
// degrees CCW about the rectangle's own centre, whichever type you hold it as.
rect.Width = 120;
rect.RotationAngle = 30;

Shape asShape = rect;
asShape.RotationAngle = 45;            // the same property — the corners rebuild
// ...which is why a rectangle's rotation is real geometry rather than a render
// transform. RotateAnimation works on every shape type either way.
var spin = new Animator();
spin.AddToAnimations(new RotateAnimation(rect, new VXYZ(60, 20), 90.0, 2.0));
spin.Animate();

// Point queries are real geometry too
bool inside = rect.Contains(new VXYZ(110, 20));     // interior test, honours rotation
double toEdge = rect.DistanceTo(new VXYZ(110, 20)); // to the boundary (from VPolygon)

// VRectangle inherits from VPolygon, so all polygon members work
double area = rect.Area;              // always positive
double signed = rect.SignedArea;      // >0 for CCW winding
// A rectangle is convex, so a cut that crosses it always gives exactly two pieces
List<VPolygon> halves = rect.Slice(new VXYZ(0, 35), new VXYZ(200, 35));
foreach (var half in halves) half.Place();   // results are unnamed — Place() keeps them" },

                { "VEllipse", @"// Create an ellipse with center and radii
var ellipse = new VEllipse(new VXYZ(100, 100), 60, 30);
ellipse.Color = ""Magenta"";
ellipse.LineWeight = 2;

// Or from coordinates
var e2 = new VEllipse(0, 0, 80, 40);

// A partial ellipse (elliptical arc) — angles in degrees, CCW
var wedge = new VEllipse(new VXYZ(-150, 0), 60, 40, 30, 210);

// Computed properties
double area = ellipse.Area;                // pi * rx * ry
double perimeter = ellipse.Circumference;  // Ramanujan approximation

// The curve parameter is ARC LENGTH, like every other ICurve, so Divide()
// spaces its points evenly along the edge — no bunching at the flat ends.
var pts = ellipse.Divide(16);
foreach (var p in pts) new VPoint(p) { Color = ""Gold"", Name = ""bead"" };

// Evaluate(t) walks the length of the curve...
VXYZ halfway = ellipse.Evaluate(0.5);        // halfway ALONG the curve
VXYZ same = ellipse.PointAtParameter(0.5);   // PointAtParameter uses Evaluate

// ...EvaluateByAngle(t) interpolates StartAngle -> EndAngle instead.
// Reach for it when you want equal ANGLES: spokes, sector edges, dials.
for (int i = 0; i < 12; i++)
{
    var spoke = new VLine(ellipse.Center, ellipse.EvaluateByAngle(i / 12.0));
    spoke.Color = ""DimGray"";
    spoke.Name = ""spoke"";
}

// SetBounds trims by arc length too: this keeps the middle half of the CURVE.
var half = new VEllipse(new VXYZ(0, -150), 80, 25);
half.SetBounds(0.25, 0.75);

// On a circle the two readings agree; they diverge as eccentricity grows." },

                { "VArc", @"// Create an arc (center, radius, startAngle, endAngle) — degrees, CCW from +X
var arc = new VArc(new VXYZ(50, 50), 40, 0, 270);
arc.Color = ""Orange"";
arc.LineWeight = 3;

// Or from coordinates
var arc2 = new VArc(0, 0, 60, 45, 135);

// Through three points
var threePoint = new VArc(new VXYZ(-50, 0), new VXYZ(0, 50), new VXYZ(50, 0));

// Static factories cover the usual CAD constructions
var a1 = VArc.FromStartCenterEnd(new VXYZ(50, 0), new VXYZ(0, 0), new VXYZ(0, 50));
var a2 = VArc.FromCenterStartAngle(new VXYZ(0, 0), new VXYZ(50, 0), 90);
var a3 = VArc.FromStartEndRadius(new VXYZ(0, 0), new VXYZ(60, 0), 40, largeArc: false);
var a4 = VArc.FromCenterStartLength(new VXYZ(0, 0), new VXYZ(50, 0), 100);

// Tangent continuation from any curve
var lead = new VLine(new VXYZ(-100, 0), new VXYZ(-50, 0));
var a5 = VArc.Continue(lead, 80);

// Curve operations
VXYZ mid = arc.MidPoint;
double len = arc.GetLength();
arc2.SetBounds(0.0, 0.5);   // keep the first half, in place" },

                { "VPolygon", @"// Create a triangle — the closing edge is implicit, do not repeat the first point
var triangle = new VPolygon(
    new VXYZ(0, 0),
    new VXYZ(100, 0),
    new VXYZ(50, 80)
);
triangle.Color = ""LimeGreen"";
triangle.FillColor = ""#4000FF00"";

// Create from any sequence of VXYZ
var points = new[] { new VXYZ(0,0), new VXYZ(50,0), new VXYZ(50,50), new VXYZ(0,50) };
var square = new VPolygon(points);
square.AddPoint(-20, 25);        // grows the outline

// Build from curves — they are auto-ordered into one closed loop and validated
// (throws ArgumentException on a gap, a branch, a closed input, or a crossing)
var fromCurves = new VPolygon(new List<ICurve>
{
    new VLine(new VXYZ(0, 0), new VXYZ(60, 0)),
    VArc.FromStartEndRadius(new VXYZ(60, 0), new VXYZ(60, 60), 40),
    new VLine(new VXYZ(60, 60), new VXYZ(0, 0))
});

// Measurements and slicing
double area = square.Area;             // shoelace, always positive
double signed = square.SignedArea;     // sign tells you the winding order
bool tangled = square.SelfIntersecting;
// Slice cuts along the INFINITE line through the two points; the pieces always sum back to Area.
// A line that misses (or merely grazes an edge or a single vertex) returns one piece: a copy of
// the original. Pieces inherit the source's styling but carry no Name, so Place() the keepers.
List<VPolygon> pieces = square.Slice(new VXYZ(-50, 25), new VXYZ(150, 25));
foreach (var piece in pieces) piece.Place();

// NEVER assume two pieces. A concave polygon whose notch straddles the cut is crossed four
// times, and the honest answer is three: the two towers above the line and everything below it.
var notched = new VPolygon(
    new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(100, 100), new VXYZ(60, 100),
    new VXYZ(60, 40), new VXYZ(40, 40), new VXYZ(40, 100), new VXYZ(0, 100));
List<VPolygon> parts = notched.Slice(new VXYZ(-50, 70), new VXYZ(150, 70));
VizConsole.Log($""{parts.Count} pieces"");                       // 3
VizConsole.Log($""{parts.Sum(p => p.Area)} of {notched.Area}""); // 10000 of 10000
foreach (var part in parts) part.Place();" },

                { "VPolyline", @"// Create an open polyline (the ends are NOT joined)
var polyline = new VPolyline(
    new VXYZ(0, 0),
    new VXYZ(30, 50),
    new VXYZ(60, 20),
    new VXYZ(100, 60)
);
polyline.Color = ""Cyan"";
polyline.AddPoint(140, 10);

// Close it by hand if you want a loop
var closed = new VPolyline(new VXYZ(0,0), new VXYZ(50,0), new VXYZ(25,40), new VXYZ(0,0));

// Curve operations run over the whole chain by arc length
double len = polyline.GetLength();
VXYZ halfway = polyline.PointAtParameter(0.5);
var evenly = polyline.Measure(10);       // a point every 10 units
polyline.SetBounds(0.2, 0.8);            // trim in place to the middle 60%" },

                { "VBezier", @"// Create a cubic Bezier curve (4 control points)
var bezier = new VBezier(
    new VXYZ(0, 0),      // P0 - start point
    new VXYZ(30, 80),    // P1 - control handle out of the start
    new VXYZ(70, 80),    // P2 - control handle into the end
    new VXYZ(100, 0)     // P3 - end point
);
bezier.Color = ""Magenta"";
bezier.LineWeight = 2;

// Or from eight coordinates
var b2 = new VBezier(0, 0, 20, 60, 80, 60, 100, 0);

// Segments controls tessellation density for rendering and length queries
b2.Segments = 64;

// Exact evaluation, and an exact De Casteljau trim
VXYZ atT = bezier.Evaluate(0.35);
bezier.SetBounds(0.25, 0.75);   // keep the middle half, control points updated in place" },

                { "VSpline", @"// Catmull-Rom spline — the curve passes through every control point
var spline = new VSpline(
    new VXYZ(0, 0),
    new VXYZ(30, 40),
    new VXYZ(60, 20),
    new VXYZ(100, 50)
);
spline.Color = ""Cyan"";

// Tessellation and shape
spline.SegmentsPerSpan = 32;   // default 16 - points generated between control points
spline.Tension = 0.2;          // default 0.5; lower is more angular, higher is looser

// Curve operations
double len = spline.GetLength();
VXYZ p = spline.PointAtParameter(0.5);
spline.SetBounds(0.1, 0.9);    // resamples the trimmed range (tangents depend on neighbours)" },

                { "VText", @"// Create text at a position (Location is the anchor point, Content is the string)
var text = new VText(new VXYZ(50, 50), ""Hello World"");
text.Height = 24;         // font height in world units, default 12
text.Color = ""White"";

// Create text with height in constructor
var text2 = new VText(0, -50, ""Compact syntax"", 18);
text2.Color = ""Cyan"";

// Font family and weight
text2.Font = VFont.Consolas;
text2.FontWeight = VFontWeight.Bold;

// Use Anchor to control which point of the text box sits at Location
var text3 = new VText(0, 0, ""Centered"", 20);
text3.Anchor = VTextAnchor.MiddleCenter;

// Rotate the entire text block (CCW degrees around Location)
var tilted = new VText(0, -100, ""45 degrees"", 18);
tilted.Angle = 45;

var vertical = new VText(80, 0, ""Vertical"", 16);
vertical.Angle = 90; // reads bottom-to-top

// Width is 0 by default, meaning ""measure the rendered string""; set it to
// override the box width used by GetBounds and anchoring.

// Morph a character's font outline into another shape.
// The (VText, index, to, duration) overload keeps the whole word visible and replaces
// the character with a space exactly when its morph starts.
var word = new VText(-100, 0, ""Go"", 120);
var ball = new VCircle(60, 50, 60);
var anim = new Animator();
anim.AddToAnimations(new TransformAnimation(word, 0, ball, 2.0)); // 'G' unfolds into a circle
anim.Animate();

// Lower-level extraction:
var glyphNow = word[1];                // eager: lift 'o' AND blank it immediately
var letterShape = word.ToCharShape(0); // non-mutating: outline of 'G', text intact" },

                { "VTextAnchor", @"// VTextAnchor controls which point of the text is placed at its position
// Default is BottomLeft (text extends right and up from the position)

var label = new VText(0, 0, ""Bottom-Left (default)"", 16);
label.Anchor = VTextAnchor.BottomLeft;

var centered = new VText(0, -40, ""Middle-Center"", 16);
centered.Anchor = VTextAnchor.MiddleCenter;

var topRight = new VText(0, -80, ""Top-Right"", 16);
topRight.Anchor = VTextAnchor.TopRight;

// All 9 anchor values:
// TopLeft,    TopCenter,    TopRight
// MiddleLeft, MiddleCenter, MiddleRight
// BottomLeft, BottomCenter, BottomRight" },

                { "VArrow", @"// Create an arrow from two points (Start and End are settable VXYZ)
var arrow = new VArrow(new VXYZ(0, 0), new VXYZ(100, 0));
arrow.Color = ""Orange"";
arrow.HeadLength = 15;   // world units, default 15
arrow.HeadAngle = 30;    // degrees off the shaft, default 30

// Or from four coordinates
var arrow1b = new VArrow(0, -30, 100, -30);

// Create from point, direction, and length
var arrow2 = new VArrow(new VXYZ(0, 50), VXYZ.BasisX, 80);
arrow2.DoubleEnded = true; // Arrowhead at Start as well

// Query the head geometry (the two wing tips)
var (w1, w2) = arrow.GetEndArrowhead();
VXYZ mid = arrow.MidPoint;" },

                { "VDimension", @"// Create a dimension line between two points
var dim = new VDimension(new VXYZ(0, 0), new VXYZ(100, 0));
dim.Offset = 20;          // Distance above the line
dim.DecimalPlaces = 1;    // Show 1 decimal place
dim.TextHeight = 14;

// AutoCAD-style extension lines
var dim2 = new VDimension(0, 50, 80, 50);
dim2.ExtendBeyondDimLines = 2.0; // Extension past dimension line
dim2.OffsetFromOrigin = 1.0;     // Gap from origin point
dim2.Prefix = ""L="";
dim2.Suffix = ""mm"";
dim2.SuppressExtLine2 = true;    // Hide second extension line
dim2.TextBackgroundOpaque = true; // Opaque background behind text

// Per-element colors (each defaults to Color when null)
var dim3 = new VDimension(0, 100, 100, 100);
dim3.Offset = 20;
dim3.ExtensionLineColor = ""Green"";   // Extension lines in green
dim3.DimensionLineColor = ""Red"";     // Dim line + arrowheads in red
dim3.TextColor = ""Cyan"";             // Text in cyan
dim3.SuppressDimensionLine = true;     // Hide dim line + arrowheads" },

                { "VRadialDimension", @"// Radius dimension for a circle
var circle = new VCircle(0, 0, 50);
var dim = new VRadialDimension(circle);
dim.LeaderAngle = 45;    // Direction of leader line

// Radius dimension for an arc
var arc = new VArc(0, 0, 80, 30, 150);
var dimArc = new VRadialDimension(arc);

// Diameter mode
var dim2 = new VRadialDimension(circle);
dim2.ShowDiameter = true;
dim2.LeaderAngle = 30;
dim2.Suffix = ""mm"";
// Displays: ""⌀100.00mm""

// Custom text and colors
var dim3 = new VRadialDimension(circle);
dim3.CustomText = ""TYP."";
dim3.DimensionLineColor = ""Red"";
dim3.TextColor = ""Cyan"";
dim3.TextBackgroundOpaque = true;" },

                { "VGroup", @"// Create a group from shapes
var group = new VGroup(
    new VCircle(0, 0, 20),
    new VLine(-30, 0, 30, 0),
    new VLine(0, -30, 0, 30)
);

// Or create empty and add shapes
var group2 = new VGroup();
group2.Add(new VCircle(50, 50, 15));
group2.AddRange(new[] { new VLine(40, 50, 60, 50) });

// Transform the entire group
group.Move(new VXYZ(100, 100, 0));
group.Rotate(new VXYZ(100, 100), 45);
group.Scale(group.GetCenter(), 1.5);

// Apply styling to all shapes
group.Color = ""Cyan"";
group.ApplyStyle();

// Utility methods
var circles = group.GetShapesOfType<VCircle>();
var allShapes = group.Flatten();  // Includes nested groups
group.ForEach(s => s.LineWeight = 2);
var big = group.Where(s => s is VCircle);   // a NEW VGroup of the matching shapes

// Membership and fade
bool has = group.ContainsShape(group[0]);
group.SetOpacity(0.5);

// The group is registered on the canvas as one selectable entity —
// no placement call needed." },

                { "VGrid", @"// Centered grid at origin: 5 columns x 3 rows, spacing 10 on BOTH axes.
// ySpacing is double? and defaults to null, meaning ""same as xSpacing"".
var grid = new VGrid(new VXYZ(0, 0), 5, 3, 10);
grid.FillColor = ""Cyan"";
grid.ApplyStyle();     // push the grid's Color/FillColor/LineWeight onto every point

// Different X/Y spacing, bottom-left at (-100, -50)
var grid2 = new VGrid(new VXYZ(-100, -50), 4, 4, 20, 15, false);

// Uniform spacing with an explicit `centered` (that parameter has no default
// on this overload, which is what keeps the four-argument call unambiguous)
var grid3 = new VGrid(new VXYZ(200, 0), 6, 6, 25, false);

// Spacing 1.0, anchored at the bottom-left corner
var grid4 = new VGrid(new VXYZ(-200, 100), 3, 3, false);

// Access individual points
VPoint firstPoint = grid[0];           // By index
VPoint cell = grid[2, 1];              // By column, row

// Get rows and columns
var bottomRow = grid.GetRow(0);
var thirdColumn = grid.GetColumn(2);

// Transform entire grid
grid.Move(new VXYZ(50, 25, 0));
grid.Rotate(new VXYZ(0, 0), 45);
grid.Scale(grid.GetCenter(), 2.0);" },

                { "VCell", @"// VCell is typically created by VSpatialGrid
var grid = new VSpatialGrid(new VXYZ(0, 0), 5, 5, 10);
VCell cell = grid[2, 2];
VizConsole.Log($""Cell {cell.UniqueId} at ({cell.Column}, {cell.Row})"");
VizConsole.Log($""Neighbours: {cell.Neighbours.Count}"");  // 4 (interior)
VizConsole.Log($""Center: {cell.Center}"");
VizConsole.Log($""CellSize: {cell.CellSize}"");

// Mark cell as blocked
cell.Blocked = true;
cell.FillColor = ""Red"";" },

                { "VSpatialGrid", @"// Create a 10x10 grid of cells, each 5 units wide
var grid = new VSpatialGrid(new VXYZ(0, 0), 10, 10, 5);

// Access cells by index or (col, row)
VCell corner = grid[0, 0];          // Bottom-left
VCell center = grid[5, 5];          // Near center
List<VCell> row = grid.GetRow(0);   // Bottom row
List<VCell> col = grid.GetColumn(0); // Left column

// Block cells to create obstacles
grid[3, 3].Blocked = true;
grid[3, 4].Blocked = true;
grid[3, 5].Blocked = true;

// A* pathfinding around obstacles
List<VCell> path = grid.FindPath(corner, center);
foreach (var cell in path)
    cell.FillColor = ""LimeGreen"";

// O(log n) nearest-cell lookup via KD-tree
VCell closest = grid.GetClosestCell(new VXYZ(12, 8));   // VXYZ queries; VPoint would draw a marker

// Style and transform
grid.Color = ""DarkGray"";
grid.ApplyStyle();
grid.Move(new VXYZ(50, 0, 0));
grid.Scale(grid.GetCenter(), 2.0);" },

                { "RayCaster", @"// Build a BVH (Surface Area Heuristic split) once over an explicit
// collection of shapes. Each query then runs in O(log N) — scales to
// millions of shapes. There is no no-arg / canvas constructor: you decide
// what gets indexed.
var walls = new List<Shape>();
for (int i = 0; i < 20; i++)
    walls.Add(new VLine(i * 10, -50, i * 10, 50) { Name = $""wall{i}"" });

var caster  = new RayCaster(walls);                        // default leafSize = 8
var caster2 = new RayCaster(walls, leafSize: 16);          // shallower tree

// To cast against everything currently drawn on the canvas
// (needs `using DoodleSharp.Canvas;` and `using System.Linq;`):
// var all = new RayCaster(CanvasRenderer.Instance.GetShapes().OfType<Shape>());

// Closest hit (XY plane; Z is ignored, direction need not be normalised)
RayHit? hit = caster.FindIntersection(new VXYZ(0, 0, 0), new VXYZ(1, 0, 0));
if (hit is { } h)
{
    VizConsole.Log($""hit {h.Shape} at {h.Point}, distance {h.Distance}"");
}

// Closest hit with a distance cap (prunes BVH sub-trees beyond the cap)
RayHit? near = caster.FindIntersection(new VXYZ(0, 0, 0), new VXYZ(1, 0, 0), maxDistance: 50);

// Exclude specific shapes (e.g. the source shape) from the candidate set —
// useful for casting off a known shape or finding the next hit past a set.
// Reference equality; excluded shapes stay in the BVH, they are just skipped.
RayHit? past = caster.FindIntersection(
    new VXYZ(0, 0, 0), new VXYZ(1, 0, 0),
    exclusionList: new List<Shape> { walls[0] });

// Any-hit early-out — faster than closest-hit for shadow-ray queries
bool blocked = caster.HasIntersection(new VXYZ(0, 0, 0), new VXYZ(1, 0, 0));
bool nearby  = caster.HasIntersection(new VXYZ(0, 0, 0), new VXYZ(1, 0, 0), maxDistance: 100);

// Parallel batch — BVH is read-only after construction, so this is thread-safe.
var queries = new[]
{
    new RayQuery(new VXYZ(0, 0, 0), new VXYZ(1, 0, 0)),
    new RayQuery(new VXYZ(0, 0, 0), new VXYZ(0, 1, 0))
};
RayHit?[] results = caster.FindIntersections(queries);              // parallel
RayHit?[] seq     = caster.FindIntersections(queries, parallel: false);

int indexed = caster.Count;   // shapes actually in the index

// After shapes move, refresh AABBs in O(N) without rebuilding the tree.
// (VXYZ is immutable — assign a new one rather than mutating X/Y.)
walls[3].Move(new VXYZ(0, 5));
caster.Refit();" },

                // Support classes
                { "VXYZ", @"// Create a 3D vector
var v = new VXYZ(10, 20, 30);
double len = v.GetLength();
var normalized = v.Normalize();

// Vector operations
var v1 = new VXYZ(1, 0, 0);
var v2 = new VXYZ(0, 1, 0);
var cross = v1.CrossProduct(v2);  // (0, 0, 1)
var dot = v1.DotProduct(v2);      // 0

// Static basis vectors
var x = VXYZ.BasisX;  // (1, 0, 0)
var y = VXYZ.BasisY;  // (0, 1, 0)
var z = VXYZ.BasisZ;  // (0, 0, 1)

// Rotate a vector around the Z-axis
var rotated = v1.Rotate(90);  // Rotates 90 degrees

// Operators: +, -, * and / work with scalars and with VPoint.
// Mixing VXYZ and VPoint always returns a plain VXYZ (never a drawable point).
var sum = new VXYZ(1, 2) + new VPoint(3, 4);       // (4, 6, 0)
var scaled = new VPoint(2, 3) * 2.0;               // (4, 6, 0)
var hadamard = new VXYZ(2, 3) * new VPoint(4, 5);  // component-wise (8, 15, 0)" },

                { "VPlane", @"// No public constructor — three factories. Vectors are normalised for you.
var byNormal = VPlane.CreateByNormalAndOrigin(VXYZ.BasisZ, VXYZ.Zero);   // the drawing plane
var byBasis  = VPlane.CreateByOriginAndBasis(VXYZ.Zero, VXYZ.BasisX, VXYZ.BasisY);
var byPoints = VPlane.CreateByThreePoints(
    new VXYZ(0, 0, 0), new VXYZ(100, 0, 0), new VXYZ(0, 100, 0));

// Read-only: Origin, Normal, XVec, YVec
VizConsole.Log($""normal = {byPoints.Normal}"");    // (0, 0, 1)

// There is no ProjectPoint / DistanceTo on VPlane. Go through a coordinate
// system instead — the local Z is the signed distance to the plane.
var cs = VCoordinateSystem.ByPlane(byNormal);
VXYZ local = cs.ToLocal(new VXYZ(10, 20, 7));
VizConsole.Log($""signed distance = {local.Z}"");   // 7" },

                { "VCoordinateSystem", @"// Built through factories; Origin, XAxis, YAxis, ZAxis are read-only.
var world = VCoordinateSystem.Identity;
var local = VCoordinateSystem.ByOrigin(new VXYZ(100, 50));

// Round-trip a point between the two frames
VXYZ inLocal = local.ToLocal(new VXYZ(150, 50));   // (50, 0, 0)
VXYZ back    = local.ToWorld(inLocal);             // (150, 50, 0)
VXYZ direct  = local.ToWorld(50, 0, 0);            // same, without building a VXYZ

// Other factories
var fromXY   = VCoordinateSystem.ByOriginXY(VXYZ.Zero, VXYZ.BasisX, VXYZ.BasisY);
var fromZ    = VCoordinateSystem.ByOriginZAxis(VXYZ.Zero, VXYZ.BasisZ);
var fromAll  = VCoordinateSystem.ByOriginVectors(
    VXYZ.Zero, VXYZ.BasisX, VXYZ.BasisY, VXYZ.BasisZ);

// Translate and Rotate return a NEW system — the original is unchanged.
var shifted = local.Translate(new VXYZ(0, 25));
var turned  = local.Rotate(VXYZ.BasisZ, 90);            // DEGREES — a quarter turn

// Use a local frame to lay out shapes relative to a moving origin
foreach (var t in new[] { 0.0, 40.0, 80.0 })
    new VCircle(shifted.ToWorld(t, 0, 0), 8) { Name = $""dot{t}"" };" },

                { "VTransform", @"// Basis vectors plus an origin — not a matrix. Identity is the default.
var t = VTransform.Identity;

// CreateRotationDegrees matches the rest of the library — prefer it.
var rotation = VTransform.CreateRotationDegrees(VXYZ.BasisZ, 90);     // quarter turn
// CreateRotationRadians is the same transform when you already hold radians.
var byRadians = VTransform.CreateRotationRadians(VXYZ.BasisZ, Math.PI / 2);   // identical
// The old name CreateRotation is the radians form, now [Obsolete].
VXYZ spun = rotation.OfVector(new VXYZ(100, 0));                      // ~(0, 100, 0)

// Reflection across a plane
var plane = VPlane.CreateByNormalAndOrigin(VXYZ.BasisX, VXYZ.Zero);   // the YZ plane
var mirror = VTransform.CreateReflection(plane);
VXYZ mirrored = mirror.OfPoint(new VXYZ(30, 10));                     // (-30, 10, 0)

// OfPoint applies the translation in Origin; OfVector ignores it.
var moved = new VTransform { Origin = new VXYZ(0, 100) };
VizConsole.Log(moved.OfPoint(new VXYZ(10, 0)));    // (10, 100, 0)
VizConsole.Log(moved.OfVector(new VXYZ(10, 0)));   // (10, 0, 0)" },

                { "DoubleExtensions", @"// Angle conversions for the boundary with System.Math.
// C2VGeometry is degrees throughout; System.Math is radians.
double rad = 45.0.ToRadians();                  // 0.7853981633974483
double deg = rad.ToDegrees();                   // 45.0

// The usual reason you need them: trigonometry
double y = 100 * Math.Sin(30.0.ToRadians());    // 50
double heading = Math.Atan2(dy, dx).ToDegrees();

// Library angles need NO conversion — they are already degrees
var arc = new VArc(VXYZ.Zero, 50, 0, 90);       // a quarter arc
var spun = new VXYZ(100, 0).Rotate(90);         // (0, 100)

// Place points around a circle
for (int i = 0; i < 12; i++)
{
    double a = (i * 30.0).ToRadians();
    new VPoint(80 * Math.Cos(a), 80 * Math.Sin(a)).Place();
}

// The one library API that wants radians:
var rot = VTransform.CreateRotationRadians(VXYZ.BasisZ, 90.0.ToRadians());
var same = VTransform.CreateRotationDegrees(VXYZ.BasisZ, 90);   // more direct" },

                { "ShapeDefaults", @"// Set global defaults. Every property is nullable — null means
// ""leave each shape's own default alone"".
ShapeDefaults.GlobalColor = ""Cyan"";
ShapeDefaults.GlobalFillColor = ""#20FFFFFF"";
ShapeDefaults.GlobalLineWeight = 2.0;
ShapeDefaults.GlobalLineType = LineType.Continuous;
ShapeDefaults.GlobalLineTypeScale = 1.5;

// Only shapes created AFTER the assignment pick these up
var circle = new VCircle(0, 0, 50);   // Cyan stroke

// Dimension-specific defaults
ShapeDefaults.DimOffset = 15.0;
ShapeDefaults.DimArrowSize = 6.0;
ShapeDefaults.DimTextHeight = 10.0;
ShapeDefaults.DimDecimalPlaces = 1;
ShapeDefaults.DimPrefix = ""L="";
ShapeDefaults.DimSuffix = ""mm"";

// Back to null (each shape uses its own default again)
ShapeDefaults.Reset();" },

                { "LineType", @"// LineType controls the line pattern for shape outlines

// Solid line (default)
var line1 = new VLine(0, 0, 100, 0);
line1.LineType = LineType.Continuous;

// Dashed line
var line2 = new VLine(0, 20, 100, 20);
line2.LineType = LineType.Dashed;

// Dotted line
var line3 = new VLine(0, 40, 100, 40);
line3.LineType = LineType.Dotted;

// Dash-dot pattern (commonly used for centerlines)
var line4 = new VLine(0, 60, 100, 60);
line4.LineType = LineType.DashDot;

// Hidden line (short dashes for hidden edges)
var rect = new VRectangle(0, 100, 80, 50);
rect.LineType = LineType.Hidden;

// LineTypeScale stretches or compresses the pattern (default 1.0)
line2.LineTypeScale = 3.0;   // longer dashes and gaps
line3.LineTypeScale = 0.5;   // finer dots

// All eight values: Continuous, Dashed, Dotted, DashDot,
// DashDotDot, Center, Phantom, Hidden" },

                { "Shape", @"// Shape is the base class for every drawable. A shape appears the moment you
// construct it, so no Place() call is needed here. Place() is for shapes that
// did not come from a plain `new` — see Shape.Place.
Shape shape = new VCircle(0, 0, 50);
var otherShape = new VRectangle(new VXYZ(-20, -20), 40, 40);
var pivot = new VXYZ(0, 0);
var point = new VXYZ(10, 10);

// Styling (defaults shown)
shape.Color = ""Cyan"";                  // stroke; named color or #RRGGBB / #AARRGGBB
shape.FillColor = ""Transparent"";       // fill
shape.LineWeight = 2.0;                 // stroke thickness
shape.LineType = LineType.Continuous;   // Continuous, Dashed, Dotted, DashDot, ...
shape.LineTypeScale = 1.0;              // dash/gap length multiplier
shape.Opacity = 1.0;                    // 0 = invisible, 1 = opaque

// Identity and state
long id = shape.Id;         // unique, assigned automatically, restarts at 1 each run
shape.Name = ""outline"";     // naming a shape also keeps it from being auto-hidden
shape.Hide();               // IsVisible = false; stays in the collection
shape.Show();
shape.Place();              // put it on the canvas and keep it there (idempotent)
shape.Remove();             // the inverse: take it off entirely

// Place() is what you need for a shape that did NOT come from a plain `new`:
var a = new VPolygon(new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(100, 100));
var b = new VPolygon(new VXYZ(50, 50), new VXYZ(150, 50), new VXYZ(150, 150));
a.Color = ""Tomato""; a.FillColor = ""#40FF6347"";

VPolygon? merged = a.Union(b);   // a method result: unnamed, default styling
a.CopyStyleTo(merged);           // copy Color/FillColor/LineWeight/LineType/LineTypeScale
merged?.Place();                 // ...and keep it past the post-run cleanup
// Draw() is the historical name for Place() and does exactly the same thing.

// Animation properties (usually driven by the Animation classes)
shape.DrawFactor = 1.0;     // 0-1, progressive drawing
shape.OffsetX = 0;          // translation offset
shape.OffsetY = 0;
shape.RotationAngle = 0;    // degrees
shape.RotationPivot = null; // null means the shape's own center

// Geometry
var copy = shape.Clone();   // same type back, no cast needed
copy.Move(new VXYZ(10, 20, 0));
copy.Rotate(pivot, 45);
copy.Scale(pivot, 2.0);
copy.Flip(new VLine(0, 0, 0, 100));   // mirror across the Y axis
BoundingBox bounds = shape.GetBounds();
// bounds.Min, bounds.Max, bounds.Width, bounds.Height, bounds.Center, bounds.Area

// Queries. The base class falls back to the bounding box, but every shape with
// a real outline overrides these with true geometry:
//   open curves (VLine, VPolyline, VArc, VBezier, VSpline, VXLine, VRay)
//       Contains   = lies ON the stroke;  DistanceTo = shortest distance to it
//       (VRay is false behind its Origin; VXLine never clamps — it is infinite)
//   areas (VPolygon, VRectangle, VCircle, VEllipse, VGroup, VHatch, Region)
//       Contains   = genuinely inside (a PARTIAL VEllipse encloses no area, so
//                    there it means 'on the curve')
//       DistanceTo = to the OUTLINE — zero on it, positive both inside and
//                    outside. Not a signed depth: pair it with Contains.
// Only VPoint, VText, VGrid, VSpatialGrid, VArrow and the dimension shapes keep
// the bounding-box answer, because for those the box IS the shape.
bool inside = shape.Contains(point);
double dist = shape.DistanceTo(point);
bool touching = shape.DoesIntersect(otherShape);

var diagonal = new VLine(0, 0, 100, 100);
diagonal.Contains(new VXYZ(50, 50));      // true  — on the segment
diagonal.Contains(new VXYZ(100, 0));      // false — inside the box, off the line
diagonal.DistanceTo(new VXYZ(0, 100));    // ~70.71, perpendicular distance

var ring = new VCircle(0, 0, 50);
ring.DistanceTo(new VXYZ(50, 0));         // 0  — exactly on the circumference
ring.DistanceTo(new VXYZ(20, 0));         // 30 — inside, measured out to the rim
ring.DistanceTo(new VXYZ(80, 0));         // 30 — outside, measured in to the rim
ring.Contains(new VXYZ(20, 0));           // true — Contains is the disc test

// Z-ordering
shape.BringAbove(otherShape);  // render on top of otherShape
shape.SendBehind(otherShape);  // render behind otherShape

// Static defaults applied to shapes created afterwards
Shape.DefaultColor = ""White"";
Shape.DefaultLineWeight = 1.5;
Shape.ResetDefaults();" },

                { "BoundingBox", @"// BoundingBox is what GetBounds() returns on every shape
var circle = new VCircle(0, 0, 50);
var square = new VRectangle(new VXYZ(20, 20), 60, 60);
BoundingBox bounds = circle.GetBounds();
BoundingBox otherBounds = square.GetBounds();

// Access min/max corners (VXYZ)
VXYZ min = bounds.Min;  // (-50, -50) - lower-left
VXYZ max = bounds.Max;  // (50, 50)   - upper-right

// Computed properties
double w = bounds.Width;   // 100
double h = bounds.Height;  // 100
VXYZ c = bounds.Center;    // (0, 0)
double a = bounds.Area;    // 10000

// Methods (all ignore Z; Contains and Intersects include the boundary)
bool hit = bounds.Contains(new VXYZ(10, 10));
bool overlaps = bounds.Intersects(otherBounds);
BoundingBox combined = bounds.Union(otherBounds);
BoundingBox expanded = bounds.Expand(10);   // grow 10 on all sides; negative contracts

// Build one yourself, and deconstruct
var manual = new BoundingBox(new VXYZ(0, 0), new VXYZ(10, 5));
var (minPt, maxPt) = circle.GetBounds();

// VRay and VXLine are infinite, so their bounds are non-finite — guard before use
var ray = new VRay(new VXYZ(0, 0), VXYZ.BasisX);
bool finite = double.IsFinite(ray.GetBounds().Width);   // false" },

                { "ControlPoint", @"// Control points are the draggable handles a shape exposes on the canvas.
// You can also read and drive them from code.
var circle = new VCircle(0, 0, 50);

List<ControlPoint> handles = circle.GetControlPoints();
foreach (var h in handles)
    VizConsole.Log($""{h.Type} '{h.Label}' at ({h.X}, {h.Y})"");
// Move  'Center' at (0, 0)
// Radius 'Radius' at (50, 0)

// Index 0 is by convention the whole-shape Move handle
circle.MoveControlPoint(0, new VXYZ(100, 100));   // relocates the circle
circle.MoveControlPoint(1, new VXYZ(180, 100));   // sets Radius to 80

// A handle's position as a coordinate
VXYZ where = handles[0].ToVXYZ();

// Build one directly if you are writing a custom shape
var custom = new ControlPoint(ControlPointType.Vertex, 10, 20, ""Corner"");" },

                { "VColor", @"// VColor is a helper for the string-valued Color and FillColor properties
var circle = new VCircle(0, 0, 50);

// Named color properties (60+)
circle.Color = VColor.Red;
circle.FillColor = VColor.LightBlue;

// Custom colors
circle.FillColor = VColor.FromRgb(255, 128, 0);        // opaque orange
circle.Color = VColor.FromArgb(128, 255, 0, 0);        // half-transparent red
circle.FillColor = VColor.WithOpacity(0, 200, 255, 0.25);

// Random colors — pastel reads well as a fill, vibrant as a stroke
circle.FillColor = VColor.GetRandomColor();        // pastel by default
circle.Color = VColor.GetRandomColor(false);       // vibrant
circle.Color = VColor.GetRandomVibrantColor();
circle.FillColor = VColor.GetRandomPastelColor();

// The whole palettes, e.g. to cycle deterministically
string[] vibrant = VColor.GetVibrantColors();
string[] pastel = VColor.GetPastelColors();
for (int i = 0; i < 10; i++)
{
    var c = new VCircle(i * 30 - 135, -100, 12);
    c.FillColor = pastel[i % pastel.Length];
}

// From the ColorName enum
circle.Color = VColor.FromEnum(ColorName.Coral);" },

                { "GeometryTolerance", @"// Comparing doubles directly is unreliable — use the library's tolerances
var a = new VXYZ(1.0, 2.0);
var b = new VXYZ(1.0 + 1e-12, 2.0);

bool same = GeometryTolerance.AreEqual(a.X, b.X);      // true, within Epsilon (1e-9)
bool zero = GeometryTolerance.IsZero(1e-15);           // true
bool coincident = GeometryTolerance.PointsAreEqual(a, b);

// The three tolerance constants
double eps = GeometryTolerance.Epsilon;         // 1e-9  - general comparisons
double vis = GeometryTolerance.VisualEpsilon;   // 1e-6  - on-screen coincidence
double ang = GeometryTolerance.AngleEpsilon;    // 1e-5  - radians

// Angle helpers
double d = GeometryTolerance.NormalizeAngleDegrees(-90);   // 270
double r = GeometryTolerance.NormalizeAngle(-Math.PI);     // pi

// Point/segment predicates
var p = new VXYZ(50, 0);
bool onSeg = GeometryTolerance.PointOnSegment(p, new VXYZ(0, 0), new VXYZ(100, 0));  // true
double dist = GeometryTolerance.PointToLineDistance(new VXYZ(50, 7), new VXYZ(0, 0), new VXYZ(100, 0));
bool collinear = GeometryTolerance.AreCollinear(new VXYZ(0,0), new VXYZ(1,1), new VXYZ(2,2));
int sign = GeometryTolerance.Sign(-1e-12);   // 0, not -1" },

                { "IDrawable", @"// IDrawable is the smallest common denominator: something that can be drawn
// and carries the five styling properties. Shape implements it; ICurve extends it.
IDrawable d = new VCircle(0, 0, 40);

d.Color = ""Cyan"";
d.FillColor = ""#40FFFFFF"";
d.LineWeight = 3;
d.LineType = LineType.Dashed;
d.LineTypeScale = 2.0;

// Style a mixed collection without caring what the shapes are
var items = new List<IDrawable>
{
    new VLine(0, 0, 100, 0),
    new VRectangle(new VXYZ(0, 20), 100, 40),
    new VArc(new VXYZ(0, 0), 60, 0, 180)
};
foreach (var item in items)
{
    item.Color = ""Gold"";
    item.LineWeight = 1.5;
}

// Place() is only for shapes that did not come from a plain `new`." },

                { "IShapeRegistry", @"// IShapeRegistry is the seam between the geometry library and a canvas.
// Shape.DefaultRegistry holds the live implementation, which is why a shape
// appears the moment you construct it — no placement call involved.
// The desktop app sets this up for you; you normally only READ these switches.

// Temporarily stop auto-registration while building throwaway geometry
Shape.AutoRegister = false;
var scratch = new VPolygon(new VXYZ(0,0), new VXYZ(10,0), new VXYZ(5,8));
double area = scratch.Area;         // computed, but nothing was added to the canvas
Shape.AutoRegister = true;          // ALWAYS restore it (use try/finally in real code)

// Register/unregister explicitly
var keeper = new VCircle(0, 0, 30);
keeper.Remove();                    // calls DefaultRegistry.Unregister
keeper.Place();                     // calls DefaultRegistry.Register

// Draw order
var under = new VRectangle(new VXYZ(-40, -40), 80, 80);
keeper.BringAbove(under);           // calls DefaultRegistry.MoveAbove
under.SendBehind(keeper);           // calls DefaultRegistry.MoveBehind" },

                { "GeometryHelper", @"// Point transforms — all return a plain VXYZ, nothing is drawn
var p = new VXYZ(100, 0);
VXYZ spun    = GeometryHelper.RotatePoint(p, VXYZ.Zero, 90);      // (0, 100) — DEGREES
VXYZ moved   = GeometryHelper.MovePoint(p, new VXYZ(0, 25));      // (100, 25)
VXYZ shrunk  = GeometryHelper.ScalePoint(p, VXYZ.Zero, 0.5);      // (50, 0)
var yAxis = new VLine(0, -100, 0, 100) { Name = ""mirror"" };
VXYZ flipped = GeometryHelper.FlipPoint(p, yAxis);                // (-100, 0)

// Angles, in degrees
double norm = GeometryHelper.NormalizeAngle(-90);                 // 270
double turn = GeometryHelper.AngleDifference(10, 350);            // 20, not -340

// Two circles: 0, 1 (tangent) or 2 points
List<VXYZ> hits = GeometryHelper.IntersectCircleCircle(
    new VXYZ(0, 0), 50, new VXYZ(60, 0), 50);
VizConsole.Log($""circle-circle points: {hits.Count}"");           // 2

// Outward normal of the nearest segment of a path
VXYZ n = GeometryHelper.GetPolylineNormalAtPoint(
    new List<VXYZ> { new VXYZ(0, 0), new VXYZ(100, 0) }, new VXYZ(50, 20), false);

// These three answer with a Shape, because the answer carries its own type.
// The result is NOT drawn — read what you need off it and let it go.
var a = new VLine(-50, 0, 50, 0);
var b = new VLine(0, -50, 0, 50);

Shape? hit = GeometryHelper.IntersectLineLine(a, b);
if (hit is VPoint crossing)
    VizConsole.Log($""cross at ({crossing.X}, {crossing.Y})"");    // cross at (0, 0)
else if (hit is VLine shared)
    VizConsole.Log($""collinear over {shared.GetLength()} units"");

hit?.Place();  // only if you actually want the marker on the canvas
// IntersectLineRect(line, rect) and IntersectRectRect(r1, r2) behave the same way." },

                { "ICurve", @"// ICurve is implemented by VLine, VCircle, VArc, VEllipse, VPolyline,
// VPolygon, VBezier, VSpline, VRay and VXLine — so you can write code that
// works against any of them. ICurve extends IDrawable, so styling is available too.
ICurve curve = new VLine(0, 0, 100, 100);
curve.Color = ""Gold"";

// Endpoints and defining vertices (all VXYZ)
VXYZ s = curve.StartPoint;
VXYZ e = curve.EndPoint;          // equals StartPoint for closed curves
List<VXYZ> verts = curve.Vertices;

// Sampling and measurement
double len = curve.GetLength();
List<VXYZ> ten = curve.Divide(10);        // 11 points, ends included
List<VXYZ> every5 = curve.Measure(5);     // a point every 5 units
VXYZ half = curve.PointAtParameter(0.5);  // parameter runs 0 to 1
double t = curve.ParameterAtPoint(half);  // the inverse
VXYZ near = curve.Project(new VXYZ(20, 80));
VXYZ n = curve.NormalAtPoint(half);

// Derived curves
ICurve parallel = curve.Offset(10);
List<ICurve> band = curve.Offset(new List<double> { -10, 10 });
var (first, second) = curve.SplitAtPoint(half);

// Trim in place: [0.2, 0.8] becomes the new [0, 1].
// Throws NotSupportedException on VCircle, VPolygon, VRay and VXLine.
curve.SetBounds(0.2, 0.8);

// Check for self-intersection
bool selfIntersects = curve.SelfIntersecting;
VizConsole.Log($""Self-intersecting: {selfIntersects}"");

// Intersect with another curve
var line2 = new VLine(0, 100, 100, 0);
IntersectionResult result = curve.Intersect(line2);
if (result.HasIntersection)
{
    foreach (var pt in result.Points)
    {
        var marker = new VPoint(pt);   // a VPoint is a drawn dot; it appears on construction
        marker.Color = ""Red"";
    }
}" },

                { "IntersectionResult", @"// IntersectionResult holds intersection data
var line1 = new VLine(0, 0, 100, 100);
var line2 = new VLine(0, 100, 100, 0);
var circle = new VCircle(50, 50, 30);

// Line-Line intersection
var result = line1.Intersect(line2);
if (result.IsSinglePoint)
    VizConsole.Log($""Cross at: {result.Points[0]}"");

// Line-Circle may have multiple points
var circleResult = line1.Intersect(circle);
VizConsole.Log($""Found {circleResult.Points.Count} intersections"");

// Check for overlapping segments (collinear lines)
if (result.HasOverlap)
    foreach (var c in result.Curves) c.Place();" },

                { "CurveIntersection", @"// Static utility for curve intersections
var line = new VLine(0, 0, 100, 100);
var circle = new VCircle(50, 50, 40);

// Intersect(a, b) dispatches on the pair and picks the exact algorithm;
// anything it has no closed form for falls back to segment sampling.
IntersectionResult result = CurveIntersection.Intersect(line, circle);
foreach (var pt in result.Points)
    new VPoint(pt.X, pt.Y).Place();    // inline shapes need Place() to stay visible

// Call an exact pair directly when you already know the types
var arc = new VArc(0, 0, 60, 0, 180);
var r1 = CurveIntersection.IntersectLineLine(line, new VLine(0, 100, 100, 0));
var r2 = CurveIntersection.IntersectLineCircle(line, circle);
var r3 = CurveIntersection.IntersectLineArc(line, arc);
var r4 = CurveIntersection.IntersectLineEllipse(line, new VEllipse(0, 0, 80, 40));
var r5 = CurveIntersection.IntersectCircleCircle(circle, new VCircle(80, 50, 40));
var r6 = CurveIntersection.IntersectCircleArc(circle, arc);
var r7 = CurveIntersection.IntersectArcArc(arc, new VArc(50, 0, 60, 90, 270));

// Collinear overlapping lines report a shared segment, not a point
var overlap = CurveIntersection.IntersectLineLine(
    new VLine(0, 0, 100, 0), new VLine(50, 0, 150, 0));
VizConsole.Log($""overlap: {overlap.HasOverlap}, curves: {overlap.Curves.Count}"");

// Force the sampled path, or get the sampling itself
var generic = CurveIntersection.IntersectGeneric(circle, arc);
List<VLine> segs = CurveIntersection.GetSegments(circle, segmentsPerUnit: 10);
// (these VLines are built non-registering, so they never draw)

// Self-intersection
var polyline = new VPolyline(
    new VXYZ(0, 0), new VXYZ(100, 0),
    new VXYZ(50, 50), new VXYZ(50, -50));
bool selfX = CurveIntersection.IsSelfIntersecting(polyline);            // true
bool rawX  = CurveIntersection.IsPolylineSelfIntersecting(polyline.Points);" },

                { "CurveGeometry", @"// The point-to-curve maths the curve shapes use for Contains/DistanceTo.
// Public, so it works on your own vertex lists too.

var p = new VXYZ(5, 3);

// Distance to a single segment, clamped to its ends
double d1 = CurveGeometry.DistanceToSegment(p, new VXYZ(0, 0), new VXYZ(10, 0));  // 3

// Distance to a whole path. closed: true adds the edge back to the first point.
var verts = new List<VXYZ> { new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10) };
double open   = CurveGeometry.DistanceToPath(p, verts);
double closed = CurveGeometry.DistanceToPath(p, verts, closed: true);
// An empty list returns double.PositiveInfinity.

// Any ICurve, by sampling it into a polyline (96 samples by default)
var bez = new VBezier(0, 0, 30, 60, 70, -60, 100, 0);
double toCurve = CurveGeometry.DistanceToCurve(p, bez);
double finer   = CurveGeometry.DistanceToCurve(p, bez, samples: 400);

// ""Is this point on the stroke?"" — tolerance scales with the curve's own
// size (max(1e-9, |extent| * 1e-6)), so units do not change the answer.
bool on = CurveGeometry.IsOnStroke(d1, curveExtent: 10);   // false, 3 is far off" },

                { "GeometryDiagnostics", @"// Where the geometry library explains a non-exceptional failure — above all,
// why a BooleanOps.Union returned null. C2VGeometry has no UI, so the host
// plugs in a sink; DoodleSharp routes it to the console panel,
// where the messages appear tagged ""Geometry"". You usually need do nothing.

// Capture the messages as well as printing them
var notes = new List<string>();
var previous = GeometryDiagnostics.Sink;
GeometryDiagnostics.Sink = m => { notes.Add(m); previous?.Invoke(m); };

var a = new VPolygon(new VXYZ(0, 0), new VXYZ(10, 0), new VXYZ(10, 10), new VXYZ(0, 10));
var b = new VPolygon(new VXYZ(50, 0), new VXYZ(60, 0), new VXYZ(60, 10), new VXYZ(50, 10));

var merged = BooleanOps.Union(a, b);        // null — the squares never touch
if (merged == null)
    VizConsole.Log(notes[^1]);              // ...and here is the reason why

// Report is how the library sends one; you can use it too. It never throws —
// an exception from the sink is swallowed rather than breaking the operation.
GeometryDiagnostics.Report(""checkpoint reached"");

GeometryDiagnostics.Sink = previous;        // put the host's sink back
// Setting Sink to null discards messages entirely." },

                // Animation
                { "Frame", @"// A function that asks for the next frame - the JavaScript idiom.
var circle = new VCircle(new VXYZ(0, 0), 20) { FillColor = ""Cyan"" };

void Tick(double t)
{
    // Motion as a function of time, not accumulated state: frame-rate independent,
    // and it lands in the same place whatever the machine is doing.
    circle.Center = new VXYZ(200 * Math.Cos(t), 200 * Math.Sin(t));

    if (t < 5.0) Frame.Request(Tick);   // keep going
    // stop asking, and the loop ends
}

Frame.Request(Tick);" },

                { "Animator", @"// Create shapes
var line = new VLine(0, 0, 100, 50);
var circle = new VCircle(50, 50, 30);

// Create animator
var anim = new Animator();
anim.Repeat = true;  // Loop animation
anim.Fps = 30;       // Limit to 30 frames per second

// Add animations sequentially - they auto-sequence
anim.AddToAnimations(new DrawAnimation(line, 2.0));      // 0-2s
anim.Pause(3);                                            // 2-5s: pause
anim.AddToAnimations(new DrawAnimation(circle, 2.0));   // 5-7s
anim.AddToAnimations(new MoveAnimation(circle, new VXYZ(50, 0, 0), 2.0)); // 7-9s

// Start playback
anim.Animate();

// For parallel animations, pass a List:
anim.AddToAnimations(new List<Animation> {
    new FadeInAnimation(line, 1.0),
    new FadeInAnimation(circle, 1.0)
});  // Both run simultaneously" },

                { "DrawAnimation", @"// Animates shape drawing from 0% to 100%
var line = new VLine(0, 0, 100, 0);
var anim = new Animator();

// Draw the line over 2 seconds
anim.AddToAnimations(new DrawAnimation(line, 2.0));
anim.Animate();" },

                { "MoveAnimation", @"// Animates moving a shape by a vector
var circle = new VCircle(0, 0, 30);
var anim = new Animator();

// Move circle by (100, 50) over 3 seconds
anim.AddToAnimations(new MoveAnimation(circle, new VXYZ(100, 50, 0), 3.0));
anim.Animate();" },

                { "PathAnimation", @"// Animates a shape along a curved path
var dot = new VCircle(0, 0, 5) { Color = ""Yellow"" };
var path = new VBezier(0, 0, 50, 100, 150, 100, 200, 0);
var anim = new Animator();

// Move dot along the bezier curve over 3 seconds
anim.AddToAnimations(new PathAnimation(dot, path, 3.0));
anim.Animate();" },

                { "RotateAnimation", @"// Animates rotating a shape around a pivot. Works on EVERY shape type.
var rect = new VRectangle(0, 0, 50, 30);
var pivot = new VXYZ(25, 15);   // centre of the rectangle
var anim = new Animator();

// Rotate 360 degrees over 4 seconds
anim.AddToAnimations(new RotateAnimation(rect, pivot, 360.0, 4.0));

// Ellipses, arcs, polygons, polylines, beziers, splines, text, groups,
// hatches and regions all rotate too — nothing opts in.
var label = new VText(-40, 80, ""spin"", 20);
var blob = new VSpline(new VXYZ(-40,-40), new VXYZ(0,-70), new VXYZ(40,-40), new VXYZ(0,-10));
anim.AddToAnimations(new List<Animation>
{
    new RotateAnimation(label, new VXYZ(0, 80), 180.0, 4.0),
    new RotateAnimation(blob, new VXYZ(0, -40), -180.0, 4.0)   // negative = clockwise
});

anim.Animate();

// Note: rotation is a render-time transform, so Contains/DistanceTo and
// click-to-select still use the shape's unrotated geometry." },

                { "FlipAnimation", @"// Animates flipping a shape across a mirror axis
var triangle = new VPolygon(new VXYZ(0,0), new VXYZ(50,0), new VXYZ(25,50));
var mirrorAxis = new VLine(25, -10, 25, 60); // vertical line
var anim = new Animator();

// Flip across the axis over 2 seconds
anim.AddToAnimations(new FlipAnimation(triangle, mirrorAxis, 2.0));
anim.Animate();" },

                { "TransformAnimation", @"// Morphs one shape into another over time
var line = new VLine(-60, 0, 60, 0);
var circle = new VCircle(0, 0, 50);
var anim = new Animator();

// The line is shown first, then unfurls into the circle over 2 seconds.
// Both inputs are hidden during the morph; the real circle is revealed at the end.
anim.AddToAnimations(new TransformAnimation(line, circle, 2.0));
anim.Animate();

// --- Spell a word, then morph each letter into a shape ---
// new TransformAnimation(text, charIndex, target, duration) keeps the whole word
// visible and blanks the character to a space exactly when ITS morph starts, so the
// letter appears to transform from its own position. Easing is set per-animation.
var word = new VText(new VXYZ(-360, -60), ""HELLO"", 170);
word.Color = ""Cyan""; word.Place();
var anim2 = new Animator();

var c = new VCircle(new VXYZ(-290, 25), 75);
c.Color = ""Orange""; c.LineWeight = 3;
var m0 = new TransformAnimation(word, 0, c, 1.4);
m0.EasingFunction = EasingFunctions.EaseInOutCubic;
anim2.AddToAnimations(m0);
anim2.Pause(0.25);

var sq = new VRectangle(-205, -50, 140, 140);
sq.Color = ""Lime""; sq.LineWeight = 3;
anim2.AddToAnimations(new TransformAnimation(word, 1, sq, 1.4));
anim2.Pause(0.25);

var tri = new VPolygon(new VXYZ(-110, -50), new VXYZ(20, -50), new VXYZ(-45, 90));
tri.Color = ""HotPink""; tri.LineWeight = 3;
anim2.AddToAnimations(new TransformAnimation(word, 2, tri, 1.4));
anim2.Pause(0.25);

var ell = new VEllipse(new VXYZ(110, 25), 80, 45);
ell.Color = ""Gold""; ell.LineWeight = 3;
anim2.AddToAnimations(new TransformAnimation(word, 3, ell, 1.4));
anim2.Pause(0.25);

var ring = new VCircle(new VXYZ(230, 25), 70);
ring.Color = ""DeepSkyBlue""; ring.LineWeight = 3;
anim2.AddToAnimations(new TransformAnimation(word, 4, ring, 1.4));
anim2.Animate();" },

                { "FadeInAnimation", @"// Animates fading in a shape from transparent to opaque
var circle = new VCircle(0, 0, 50);
var anim = new Animator();

// Fade in over 2 seconds
anim.AddToAnimations(new FadeInAnimation(circle, 2.0));
anim.Animate();" },

                { "FadeOutAnimation", @"// Animates fading out a shape from opaque to transparent
var circle = new VCircle(0, 0, 50);
var anim = new Animator();

// Fade out over 2 seconds (to fully transparent)
anim.AddToAnimations(new FadeOutAnimation(circle, 2.0));

// Or fade to partial transparency
anim.AddToAnimations(new FadeOutAnimation(circle, 2.0, 0.3));  // Fade to 30% opacity
anim.Animate();" },

                { "ValueAnimation", @"// Animates any numeric (double) property on a shape
// Works with any property: Radius, Width, Height, X, Y, etc.
// Only one Animator plays at a time, so put everything into a single one.
var circle = new VCircle(0, 0, 10);
var rect   = new VRectangle(120, 0, 20, 50);
var pulse  = new VCircle(-120, 0, 5);

var anim = new Animator();
anim.Repeat = true;

// Example 1: Pulsing circle — animate radius
anim.AddToAnimations(new ValueAnimation<VCircle>(circle, c => c.Radius, 10, 80, 2.0));

// Example 2: Growing rectangle — animate width
anim.AddToAnimations(new ValueAnimation<VRectangle>(rect, r => r.Width, 20, 200, 3.0));

// Example 3: With easing for smooth motion
var valAnim = new ValueAnimation<VCircle>(pulse, c => c.Radius, 5, 60, 2.0);
valAnim.EasingFunction = EasingFunctions.EaseInOutCubic;
anim.AddToAnimations(valAnim);

// Example 4: Animate through multiple values — radius goes 10 → 50 → 20 → 80,
// each leg taking a third of the 3 second duration (at least 2 values required)
anim.AddToAnimations(new ValueAnimation<VCircle>(
    circle, c => c.Radius, new List<double> { 10, 50, 20, 80 }, 3.0));

anim.Animate();

// The selector must be a plain property access on T (T : Shape).
// c => c.Radius * 2, a method call, or a field all throw ArgumentException." },

                { "ObjectPropertyAnimation", @"// Animates any numeric property on an arbitrary object
// Useful for animating user-defined classes, not just shapes
var wheel = new Wheel();
var anim = new Animator();

// Animate rotation from 0 to 360 over 1 second
anim.AddToAnimations(new ObjectPropertyAnimation<Wheel>(wheel, w => w.Rotation, 0.0, 360.0, 1.0));
anim.Repeat = true;
anim.Animate();" },

                { "EasingFunctions", @"// Apply easing to any animation for smooth motion
var circle = new VCircle(0, 0, 30);
var anim = new Animator();

var moveAnim = new MoveAnimation(circle, new VXYZ(200, 0, 0), 3.0);

// Available Easing Functions:
// ┌─────────────────┬───────────┬──────────────────────────┐
// │ Function        │ Formula   │ Effect                   │
// ├─────────────────┼───────────┼──────────────────────────┤
// │ Linear          │ t         │ Constant speed           │
// │ EaseInQuad      │ t²        │ Slow start, accelerates  │
// │ EaseOutQuad     │ t(2-t)    │ Fast start, decelerates  │
// │ EaseInOutQuad   │ Piecewise │ Slow start & end         │
// │ EaseInCubic     │ t³        │ Slower start             │
// │ EaseOutCubic    │ (t-1)³+1  │ Slower end               │
// │ EaseInOutCubic  │ Piecewise │ Smooth start & end       │
// └─────────────────┴───────────┴──────────────────────────┘

// Set the easing function
moveAnim.EasingFunction = EasingFunctions.EaseInOutCubic;

// EasingFunction is a plain Func<double, double>, so a custom curve works too
// moveAnim.EasingFunction = t => t * t * (3 - 2 * t);   // smoothstep

anim.AddToAnimations(moveAnim);
anim.Animate();" },

                { "Animation", @"// Animation is the abstract base every animation type inherits from.
// It attaches to one shape and runs for a fixed Duration in seconds; the
// timeline feeds it a normalized time t (0 at its start, 1 at its end).
var circle = new VCircle(0, 0, 40) { Color = ""Cyan"" };
var anim = new Animator();

// Declare as Animation to work with any subclass
Animation move = new MoveAnimation(circle, new VXYZ(200, 0, 0), 2.0);
move.EasingFunction = EasingFunctions.EaseInOutQuad;  // default is Linear
move.Name = ""slide right"";                            // label on the timeline track
anim.AddToAnimations(move);

// Duration is fixed at construction; StartTime is assigned when you add it
VizConsole.Log($""{move.Name}: {move.Duration}s starting at {move.StartTime}s"");
VizConsole.Log(move.Target == circle);   // true — Target is the animated shape

// Build a mixed list and add them all in parallel
var together = new List<Animation>
{
    new FadeInAnimation(circle, 1.0),
    new RotateAnimation(circle, new VXYZ(0, 0), 180, 1.0)
};
anim.AddToAnimations(together);

anim.Animate();" },

                // Console
                { "VizConsole", @"// Log() is the only console method — no Write(), no WriteLine().
// The calling file and line are captured for you: [StartViz:12] message
var circle = new VCircle(0, 0, 50);

VizConsole.Log(""Starting visualization..."");
VizConsole.Log($""Circle radius: {circle.Radius}"");
VizConsole.Log(circle.GetBounds().Center);    // any object — ToString() is printed
VizConsole.Log(null);                          // prints an empty line

// Collections are itemized by default: one line per item
var nums = new List<int> { 1, 2, 3 };
VizConsole.Log(nums);          // three lines: 1, 2, 3
VizConsole.Log(nums, false);   // one line: System.Collections.Generic.List`1[System.Int32]

// An empty collection prints ""(empty)"" rather than nothing at all
VizConsole.Log(new List<int>());

// Strings are never itemized, even though they are IEnumerable
VizConsole.Log(""abc"");         // one line: abc" },

                // Boolean Operations
                { "BooleanOps", @"// Boolean operations on polygons (delegates to Clipper2)

var poly1 = new VPolygon(
    new VXYZ(0, 0), new VXYZ(100, 0),
    new VXYZ(100, 100), new VXYZ(0, 100));
var poly2 = new VPolygon(
    new VXYZ(50, 50), new VXYZ(150, 50),
    new VXYZ(150, 150), new VXYZ(50, 150));

// Union - ONE polygon or null (null when the inputs stay disjoint, or when
// the merge produces more than one piece). When it returns null it says WHY
// in the console, tagged ""Geometry"" — via GeometryDiagnostics. Results come
// back unnamed, so name them (or call Place()) or the post-run sweep hides them.
VPolygon? union = poly1.Union(poly2);
if (union != null) { union.Name = ""union""; union.Color = ""Cyan""; }

// Want every piece rather than a null? UnionAll never returns null: overlapping
// inputs merge, disjoint ones come back as separate pieces.
List<VPolygon> pieces = BooleanOps.UnionAll(poly1, poly2);
foreach (var piece in pieces) piece.Place();

// UnionAll does not represent holes. When the merged outline can enclose a void
// you care about, use the hole-aware form (exactly two polygons):
List<PolygonWithHoles> holed = BooleanOps.UnionWithHoles(poly1, poly2);

// The other three always return List<VPolygon> (possibly empty).
// NOTE the first one is STATIC. poly1.Intersect(poly2) does NOT reach the boolean
// extension: VPolygon already declares IntersectionResult Intersect(ICurve), and an
// instance method always beats an extension method — so the dotted form gives you the
// points where the two OUTLINES cross, not the overlapping area.
List<VPolygon> intersection = BooleanOps.Intersect(poly1, poly2);  // overlapping AREA
IntersectionResult crossings = poly1.Intersect(poly2);             // where the outlines meet
List<VPolygon> difference   = poly1.Difference(poly2);  // poly1 minus poly2
List<VPolygon> xor          = poly1.Xor(poly2);         // symmetric difference
foreach (var p in difference) { p.Name = ""diff""; p.Color = ""Tomato""; }

// Static form; Union also folds a whole set (params or IEnumerable)
var merged = BooleanOps.Union(poly1, poly2);                 // params VPolygon[]
var mergedList = BooleanOps.Union(new List<VPolygon> { poly1, poly2 });

// Hole-aware variants return List<PolygonWithHoles> (Outer + Holes)
var holed = BooleanOps.DifferenceWithHoles(poly1, poly2);

// Utility methods
bool inside = poly1.Contains(new VXYZ(50, 50));   // boundary counts as inside
double area = poly1.GetArea();                     // unsigned; BooleanOps.Area is signed

// Offset (positive = outward, negative = inward); Safe caps the inward
// distance so the polygon cannot collapse on itself.
var grown  = BooleanOps.OffsetPolygon(poly1, 10, JoinType.Round, EndType.Polygon);
var shrunk = BooleanOps.OffsetPolygonSafe(poly1, -60);
double maxIn = BooleanOps.MaxSafeInwardOffset(poly1);

// Self-intersection: test, then resolve into simple pieces
bool tangled = BooleanOps.HasSelfIntersections(poly1);
var pieces   = BooleanOps.MakeSimple(poly1);

// Simplify polygon (Douglas-Peucker; larger tolerance = fewer points)
var simplified = BooleanOps.Simplify(poly1, tolerance: 0.1);" },

                // Array Operations
                { "ArrayOps", @"// Create arrays and patterns of shapes. Every method returns a
// List<Shape> of the clones; the clones carry no Name, so finish with
// .DrawAll() (marks them explicitly drawn) or the post-run sweep hides them.

var circle = new VCircle(0, 0, 20);

// Linear array along X axis: 5 shapes TOTAL (original + 4 clones), 50 apart
circle.LinearArrayX(5, 50).DrawAll();

// Linear array along Y axis: 4 shapes total, 40 apart
circle.LinearArrayY(4, 40).DrawAll();

// Linear array along an arbitrary direction (the vector is normalised,
// so spacing is always in world units)
circle.LinearArray(new VXYZ(1, 1, 0), 6, 30).DrawAll();

// Rectangular grid: rows × cols shapes total, laid out +X and +Y from the
// original (Y is up). Zero or negative rows/cols returns an empty list.
var rect = new VRectangle(0, 0, 30, 20);
rect.RectangularArray(rows: 3, cols: 4, rowSpacing: 40, colSpacing: 50).DrawAll();

// Circular array: count shapes total, counter-clockwise from the original.
// 360° divides by count (no duplicate at the seam); a partial sweep divides
// by count-1 so the first and last land on the ends of the arc.
var shape = new VCircle(50, 0, 10);
var center = new VXYZ(0, 0);
shape.CircularArray(center, count: 8).DrawAll();                         // full circle
shape.CircularArray(center, count: 6, totalAngleDegrees: 180).DrawAll(); // half circle
shape.CircularArray(center, count: 8, 360, rotateItems: false).DrawAll();// keep orientation

// Path array — count clones spread evenly by arc length along any ICurve.
// Note: unlike the arrays above, the original is NOT part of the returned list.
var marker = new VCircle(0, 0, 5);
var path = new VSpline(new VXYZ(0,0), new VXYZ(50,100), new VXYZ(100,0));
marker.PathArray(path, count: 10, alignToPath: true).DrawAll();

// Spiral array — count clones from startRadius to endRadius over
// totalRevolutions turns. Also excludes the original.
var dot = new VCircle(0, 0, 3);
dot.SpiralArray(center, count: 30, startRadius: 20, endRadius: 100, totalRevolutions: 2).DrawAll();

// Mirror across a line — returns [original, mirrored copy]
var triangle = new VPolygon(new VXYZ(0,0), new VXYZ(50,0), new VXYZ(25,40));
var mirrorAxis = new VLine(0, -50, 0, 50);
triangle.Mirror(mirrorAxis).DrawAll();

// Static form of every one of the above
var copies = ArrayOps.LinearArray(circle, new VXYZ(1, 0, 0), 5, 50);" },
                { "PolygonWithHoles", @"// Create a polygon with a hole using boolean difference
var outer = new VRectangle(-100, -100, 200, 200);
var inner = new VCircle(0, 0, 50);
var innerPoly = new VPolygon(inner.Divide(32).ToArray());

var results = BooleanOps.DifferenceWithHoles(
    new VPolygon(outer.Points.ToArray()), innerPoly);
foreach (var pwh in results)
{
    pwh.Outer.Color = ""Cyan"";
    foreach (var hole in pwh.Holes)
        hole.Color = ""Red"";
}

// Or create directly
var pwh2 = new PolygonWithHoles(
    new VPolygon(new VXYZ(0,0), new VXYZ(200,0), new VXYZ(200,200), new VXYZ(0,200)));
pwh2.AddHole(new VPolygon(new VXYZ(50,50), new VXYZ(150,50), new VXYZ(150,150), new VXYZ(50,150)));
VizConsole.Log(pwh2.Area);        // outer area minus hole area
VizConsole.Log(pwh2.Contains(new VXYZ(100, 100)));  // false (inside hole)" },

                // Region
                { "Region", @"// Region bounded by lines (rectangle)
var p0 = new VXYZ(0, 0);
var p1 = new VXYZ(100, 0);
var p2 = new VXYZ(100, 80);
var p3 = new VXYZ(0, 80);

var curves = new List<ICurve> {
    new VLine(p0, p1),
    new VLine(p1, p2),
    new VLine(p2, p3),
    new VLine(p3, p0)
};
var region = new Region(curves);
region.Color = ""Cyan"";
region.FillColor = ""#4000FFFF"";

// Region with mixed curves (D-shape: line + arc)
var bottom = new VXYZ(0, 0);
var top = new VXYZ(0, 60);
var arc = VArc.FromStartEndRadius(top, bottom, 40, false);
var dShape = new Region(new List<ICurve> { new VLine(bottom, top), arc });

// Region with a hole
var outer = new Region(new List<ICurve> {
    new VLine(new VXYZ(0,0), new VXYZ(100,0)),
    new VLine(new VXYZ(100,0), new VXYZ(100,100)),
    new VLine(new VXYZ(100,100), new VXYZ(0,100)),
    new VLine(new VXYZ(0,100), new VXYZ(0,0))
});
outer.AddHole(new List<ICurve> {
    new VLine(new VXYZ(30,30), new VXYZ(70,30)),
    new VLine(new VXYZ(70,30), new VXYZ(70,70)),
    new VLine(new VXYZ(70,70), new VXYZ(30,70)),
    new VLine(new VXYZ(30,70), new VXYZ(30,30))
});

// Properties
VizConsole.Log(region.Area);       // 8000
VizConsole.Log(region.Perimeter);  // 360
VizConsole.Log(region.Contains(new VXYZ(50, 40)));  // true

// Convert to polygon
var poly = region.ToPolygon();           // Low-fidelity (endpoints only)
var hires = region.ToPolygonHighRes(32); // High-fidelity (sampled)

// Create from polygon
var fromPoly = Region.FromPolygon(new VPolygon(
    new VXYZ(0,0), new VXYZ(50,0), new VXYZ(50,50), new VXYZ(0,50)));

// Create directly from a single closed curve (circle/ellipse/closed polygon/spline)
var circleRegion = new Region(new VCircle(0, 0, 50));  // consumes the circle
circleRegion.FillColor = ""#4000FFFF"";
circleRegion.AddHole(new VCircle(0, 0, 20));           // hole from another closed curve" },

                { "RegionBooleanOps", @"// Boolean operations on Regions
var regionA = new Region(new List<ICurve> {
    new VLine(new VXYZ(0,0), new VXYZ(80,0)),
    new VLine(new VXYZ(80,0), new VXYZ(80,80)),
    new VLine(new VXYZ(80,80), new VXYZ(0,80)),
    new VLine(new VXYZ(0,80), new VXYZ(0,0))
});
var regionB = new Region(new List<ICurve> {
    new VLine(new VXYZ(40,40), new VXYZ(120,40)),
    new VLine(new VXYZ(120,40), new VXYZ(120,120)),
    new VLine(new VXYZ(120,120), new VXYZ(40,120)),
    new VLine(new VXYZ(40,120), new VXYZ(40,40))
});

// Union - combine regions
var union = RegionBooleanOps.Union(regionA, regionB);

// Intersection - overlapping area
var intersection = RegionBooleanOps.Intersect(regionA, regionB);

// Difference - subtract regionB from regionA
var difference = RegionBooleanOps.Difference(regionA, regionB);

// XOR - symmetric difference
var xor = RegionBooleanOps.Xor(regionA, regionB);

// Operate on a whole collection (List<Region>, array, or params)
var regions = new List<Region> { regionA, regionB };
var multiUnion = RegionBooleanOps.Union(regions);        // merged area
var common     = RegionBooleanOps.Intersect(regions);    // area common to all
var firstCut   = RegionBooleanOps.Difference(regions);   // first minus the rest
var alsoUnion  = BooleanOps.Union(regions);              // BooleanOps facade forwards to RegionBooleanOps

// Extension method syntax
var extUnion = regionA.Union(regionB);
var extDiff = regionA.Difference(regionB);

// With holes support
var diffWithHoles = RegionBooleanOps.DifferenceWithHoles(regionA, regionB);" },

                { "JoinType", @"// JoinType controls how offset polygon corners are handled
var poly = new VPolygon(new VXYZ(0,0), new VXYZ(100,0), new VXYZ(100,100), new VXYZ(0,100));

// Miter (default) - sharp corners
var miter = BooleanOps.OffsetPolygon(poly, 10, JoinType.Miter);

// Round - rounded corners
var round = BooleanOps.OffsetPolygon(poly, 10, JoinType.Round);

// Square - squared-off corners
var square = BooleanOps.OffsetPolygon(poly, 10, JoinType.Square);" },
                { "EndType", @"// EndType controls how offset polygon ends are handled (mainly for open paths)
// Polygon (default) - treats input as closed polygon
// OpenRound - rounded open ends
// OpenSquare - squared open ends
// OpenButt - flat cut open ends
var poly = new VPolygon(new VXYZ(0,0), new VXYZ(100,0), new VXYZ(100,100), new VXYZ(0,100));
var offset = BooleanOps.OffsetPolygon(poly, 10, JoinType.Miter, EndType.Polygon);" },

                // Hatch Patterns
                { "VHatch", @"// Built-in pattern with enum
var rect = new VRectangle(0, 0, 100, 80);
var hatch = new VHatch(rect, BuiltInHatch.ANSI31, scale: 10);
hatch.Color = ""Cyan"";

// Built-in pattern by name
var hatch2 = new VHatch(rect, ""BRICK"", scale: 5);
hatch2.Color = ""Yellow"";

// With rotation
var hatch3 = new VHatch(rect, BuiltInHatch.ANSI37, scale: 15, angle: 30);

// Custom pattern from string (.pat format)
var custom = VHatch.FromDefinition(rect, @""
  *CROSSHATCH, Custom crosshatch
  0, 0,0, 0,10
  90, 0,0, 0,10
"", scale: 1.0);
custom.Color = ""Lime"";

// Custom HatchType object
var pattern = new HatchType(""MyPattern"", ""Diagonal"", new List<HatchPatternLine> {
    new HatchPatternLine(45, 0, 0, 0, 5),
    new HatchPatternLine(135, 0, 0, 0, 5)
});
var hatch4 = new VHatch(rect, pattern, scale: 2.0);" },
                { "HatchType", @"// Parse from .pat format string
var pattern = HatchType.Parse(@""
  *MYHAT, My custom hatch
  45, 0,0, 0,10
  135, 0,0, 0,10
"");

// Get built-in by name
var ansi31 = HatchType.GetBuiltIn(""ANSI31"");

// Get built-in by enum
var brick = HatchType.GetBuiltIn(BuiltInHatch.BRICK);

// Build programmatically
var custom = new HatchType(""Custom"", ""My pattern"", new List<HatchPatternLine> {
    new HatchPatternLine(0, 0, 0, 0, 5, 10, -5),  // horizontal dashed
    new HatchPatternLine(90, 0, 0, 0, 5)            // vertical continuous
});

// Every lookup hands back a FRESH COPY, so a built-in is yours to adjust —
// steepen and widen ANSI31 without affecting anyone else's lookup of it.
var steep = BuiltInHatches.Get(BuiltInHatch.ANSI31);
steep.Lines[0].Angle = 60;
steep.Lines[0].DeltaY *= 2;

var pristine = BuiltInHatches.Get(BuiltInHatch.ANSI31);
VizConsole.Log(pristine.Lines[0].Angle);   // still 45

// Clone() does the same for a pattern you already hold (deep — line families
// and their Dashes arrays are copied too).
var variant = steep.Clone();
variant.Name = ""ANSI31-steep-dashed"";
variant.Lines[0].Dashes = new[] { 4.0, -2.0 };" },
                { "BuiltInHatch", @"// Use enum values for built-in patterns. VHatch is not auto-named,
// so give each one a Name or it is hidden when Main() returns.
var h1 = new VHatch(polygon, BuiltInHatch.ANSI31, scale: 10) { Name = ""h1"" };
var h2 = new VHatch(polygon, BuiltInHatch.BRICK, scale: 5) { Name = ""h2"" };
var h3 = new VHatch(polygon, BuiltInHatch.HEX, scale: 20) { Name = ""h3"" };
var h4 = new VHatch(polygon, BuiltInHatch.STEEL, scale: 10) { Name = ""h4"" };
// Enum members use _ where the pattern name has - (AR_HBONE == ""AR-HBONE"")
var h5 = new VHatch(polygon, BuiltInHatch.AR_HBONE, scale: 2) { Name = ""h5"" };

// List all 72 available patterns
foreach (var name in BuiltInHatches.GetAllNames())
    VizConsole.Log(name);" },

                { "HatchGenerator", @"// Generate hatch segments WITHOUT creating a VHatch shape.
// Pure geometry: nothing is registered on the canvas by Generate itself.
var boundary = new List<VXYZ>
{
    new VXYZ(0, 0), new VXYZ(100, 0), new VXYZ(100, 80), new VXYZ(0, 80)
};

var pattern = HatchType.GetBuiltIn(BuiltInHatch.ANSI31);

// scale multiplies spacing / dash lengths / origin; patternAngle (degrees) is
// added to every line family's own angle.
var segments = HatchGenerator.Generate(boundary, pattern, scale: 10, patternAngle: 0);
VizConsole.Log($""{segments.Count} hatch segments"");

foreach (var (start, end) in segments)
    new VLine(start, end) { Name = ""hatchline"", Color = ""DimGray"", LineWeight = 0.5 };

// Same thing through the shape (VHatch.GenerateLines calls this internally)
var hatch = new VHatch(boundary, pattern, scale: 10) { Name = ""hatch"" };
var alsoSegments = hatch.GenerateLines();" },

                { "Chart", @"// Every Chart.* method returns one VGroup holding the axes, gridlines,
// ticks, labels and data shapes. It comes back from a method rather than a
// `new`, so give it a Name or the unnamed-shape sweep will hide it.

// === Bar — categorical values with a numeric Y axis ===
var labels = new[] { ""Q1"", ""Q2"", ""Q3"", ""Q4"" };
var values = new[] { 120.0, 150, 95, 180 };

var revenue = Chart.Bar(labels, values, new ChartOptions
{
    Origin = new VXYZ(-250, -150),
    Width = 500,
    Height = 300,
    Title = ""Quarterly Revenue (M$)"",
    YAxisTitle = ""Revenue"",
    YMin = 0,                       // pin Y to zero (otherwise auto-fits)
    TickDecimalPlaces = 0
});
revenue.Name = ""revenue"";           // keeps the chart visible after Main() returns


// === Line — computed time series, auto-fit ranges ===
var xs = Enumerable.Range(0, 60).Select(i => i * 0.1).ToArray();
var ys = xs.Select(x => Math.Exp(-0.3 * x) * Math.Sin(2 * x)).ToArray();

var trace = Chart.Line(xs, ys, new ChartOptions
{
    Origin = new VXYZ(-300, -150),
    Width = 600,
    Height = 300,
    Title = ""Damped Oscillator"",
    XAxisTitle = ""Time (s)"",
    YAxisTitle = ""Amplitude""
});


// === Scatter — correlated random sample ===
var rng = new Random(42);
var sample = Enumerable.Range(0, 80).Select(_ =>
{
    double age = rng.NextDouble() * 40 + 20;
    double height = age * 0.4 + 150 + rng.NextDouble() * 20;
    return new VXYZ(age, height);
}).ToArray();

var scatter = Chart.Scatter(sample, new ChartOptions
{
    Origin = new VXYZ(-250, -150),
    Width = 500,
    Height = 300,
    Title = ""Height vs Age"",
    XAxisTitle = ""Age"",
    YAxisTitle = ""Height (cm)""
});


// === Pie — named slices, custom palette ===
var share    = new[] { 64.7, 19.5, 9.3, 3.5, 3.0 };
var browsers = new[] { ""Chrome"", ""Safari"", ""Edge"", ""Firefox"", ""Other"" };

var pie = Chart.Pie(share, browsers, new ChartOptions
{
    Origin = new VXYZ(-150, -150),
    Width = 300,
    Height = 300,
    Title = ""Browser Market Share"",
    Palette = new[] { ""DodgerBlue"", ""Tomato"", ""MediumSeaGreen"", ""Gold"", ""Gray"" }
});


// === Area — filled trend with axis titles ===
var months = Enumerable.Range(0, 12).Select(i => (double)(i + 1)).ToArray();
var mau    = new[] { 4.2, 5.1, 6.0, 7.3, 8.1, 8.8, 9.4, 9.7, 10.2, 10.5, 11.0, 11.6 };

var growth = Chart.Area(months, mau, new ChartOptions
{
    Origin = new VXYZ(-300, -150),
    Width = 600,
    Height = 300,
    Title = ""Monthly Active Users"",
    XAxisTitle = ""Month"",
    YAxisTitle = ""MAU (millions)"",
    YMin = 0
});

// A chart is a VGroup — move/rotate/scale/style as one unit
growth.Move(new VXYZ(0, 50));" },

                { "GlobalParameters", @"// Declare once — anywhere in the project. Re-running re-declares harmlessly.
GlobalParameters.Set<double>(""String Length"", 10, min: 0, max: 50, group: ""Strings"");
GlobalParameters.Set<double>(""Panel Count"", 6, min: 1, max: 20, step: 1, group: ""Strings"");
GlobalParameters.Set<bool>(""String Broken"", true);
GlobalParameters.Set<string>(""String Name"", ""String-A"");

// Read anywhere — Get(...) converts itself to the parameter's type.
double length     = GlobalParameters.Get(""String Length"");
double halfLength = GlobalParameters.Get(""String Length"") * 0.5;
int    count      = (int)GlobalParameters.Get(""Panel Count"");
string status     = GlobalParameters.Get(""String Broken"") ? "" "" : "" not "";

VizConsole.Log($""{GlobalParameters.Get(""String Name"")} is{status}broken..."");

var spine = new VLine(new VXYZ(-halfLength, 0), new VXYZ(halfLength, 0));

// Open Windows > Global Parameters (F6) and drag the sliders: the canvas re-runs
// live, and on release the new value is written back into the Set(...) call above.

// + is ambiguous (double vs string) — use .Num or the generic form there:
double wider = GlobalParameters.Get(""String Length"").Num + 5;
double also  = GlobalParameters.Get<double>(""String Length"") + 5;

// Fallbacks, existence checks and resets
double margin = GlobalParameters.Get(""Margin"", 2.5);   // undeclared -> 2.5
if (GlobalParameters.Has(""Panel Count"")) { /* ... */ }
GlobalParameters.Reset(""String Length"");                // back to the code default" },

                { "ParamValue", @"// Get(...) returns a ParamValue that converts itself
GlobalParameters.Set<double>(""Radius"", 25);

double r  = GlobalParameters.Get(""Radius"");          // implicit -> double
int    ri = (int)GlobalParameters.Get(""Radius"");     // explicit -> int
double d  = GlobalParameters.Get(""Radius"").Num;      // named accessor

var v = GlobalParameters.Get(""Radius"");
if (v.Exists) VizConsole.Log(v.ToString());
double safe = v.As<double>();" },

                { "ChartOptions", @"// Customise plot area, palette, axes
var opts = new ChartOptions
{
    Origin = new VXYZ(0, 0),
    Width = 500,
    Height = 300,
    Title = ""Monthly active users"",
    XAxisTitle = ""Month"",
    YAxisTitle = ""MAU (thousands)"",
    XTickCount = 12,
    YTickCount = 5,
    XLabelRotation = 45,        // angle long category names
    LabelFontSize = 9,
    Palette = new[] { ""DodgerBlue"", ""HotPink"" },
    TickDecimalPlaces = 0,
    ShowGrid = true,
};

var data = new[] { 4.2, 5.1, 6.0, 7.3, 8.1, 8.8, 9.4, 9.7, 10.2, 10.5, 11.0, 11.6 };
var labels = new[] { ""Jan"",""Feb"",""Mar"",""Apr"",""May"",""Jun"",""Jul"",""Aug"",""Sep"",""Oct"",""Nov"",""Dec"" };
var mau = Chart.Bar(labels, data, opts);
mau.Name = ""mau"";           // charts come back unnamed — name them to keep them visible

// ShowLegend draws a colour swatch + label per entry down the RIGHT of the
// plot area, in Palette order. Honoured by Chart.Bar (one per category) and
// Chart.Pie (one per slice, when you pass labels). Line/Scatter/Area draw a
// single series in one colour and ignore it.
var parts = new[] { ""Frame"", ""Motor"", ""Battery"", ""Wheels"" };
var mass  = new[] { 3.4, 5.9, 8.2, 2.1 };
var bom = Chart.Bar(parts, mass, new ChartOptions
{
    Origin = new VXYZ(-250, -150),
    Width = 420,               // narrower plot leaves room for the legend
    Height = 300,
    Title = ""Mass by component (kg)"",
    ShowLegend = true,
    LabelFontSize = 12         // also sets swatch size and legend row spacing
});
bom.Name = ""bom"";

// Pin the axis range instead of auto-fitting
var fixedRange = new ChartOptions { YMin = 0, YMax = 100, YTickCount = 5 };" },

                // Enums
                { "VFont", @"// VFont selects the font family for a VText. Default is VFont.Arial.
var heading = new VText(new VXYZ(-100, 120), ""Site plan"", 24);
heading.Font = VFont.Georgia;
heading.FontWeight = VFontWeight.Bold;

// Consolas is the one to reach for when digits must line up in a column
var readout = new VText(new VXYZ(-100, 80), ""X:  12.500\nY: 940.250"", 12);
readout.Font = VFont.Consolas;

// The full set: Arial, TimesNewRoman, CourierNew, Verdana, Georgia, Tahoma,
// TrebuchetMS, Consolas, Calibri, Cambria, SegoeUI, ComicSansMS, Impact,
// LucidaConsole. The name is the family — the enum has no size; use Height." },

                { "VFontWeight", @"// Two weights only: Normal (default) and Bold.
var label = new VText(new VXYZ(0, 0), ""Plot 14"", 18);
label.FontWeight = VFontWeight.Bold;

// It is a property, so it can be switched after construction
foreach (var t in new[] { label })
    t.FontWeight = t.Content.StartsWith(""Plot"") ? VFontWeight.Bold : VFontWeight.Normal;" },

                { "ColorName", @"// ColorName is the enum form of the 82 names VColor exposes as strings.
// Use it when you want a colour as a value to store, pass or switch on.
ColorName chosen = ColorName.Crimson;

var circle = new VCircle(new VXYZ(0, 0), 40);
circle.Color = VColor.FromEnum(chosen);       // -> ""Crimson""

// Colour a series from an array of enum values
var palette = new[] { ColorName.Teal, ColorName.Gold, ColorName.Tomato };
for (int i = 0; i < 3; i++)
{
    var c = new VCircle(new VXYZ(i * 100 - 100, -80), 30);
    c.FillColor = VColor.FromEnum(palette[i]);
    c.Name = $""dot{i}"";
}

// Color and FillColor take plain strings, so VColor.FromEnum is the bridge —
// there is no implicit conversion from ColorName to string." },

                { "ControlPointType", @"// ControlPointType labels the role of each handle a shape exposes for
// interactive editing. You meet it through Shape.GetControlPoints().
var arc = new VArc(new VXYZ(0, 0), 80, 0, 120);

foreach (ControlPoint cp in arc.GetControlPoints())
{
    string role = cp.Type switch
    {
        ControlPointType.Move         => ""drag the whole shape"",
        ControlPointType.Vertex       => ""an endpoint or polygon vertex"",
        ControlPointType.Radius       => ""resize a circle or arc"",
        ControlPointType.Rotation     => ""spin the shape"",
        ControlPointType.CurveControl => ""a bezier / spline handle"",
        _                             => ""unknown""
    };
    VizConsole.Log($""{cp.Label}: {role} at {cp.ToVXYZ()}"");
}

// By convention index 0 is the Move handle, and MoveControlPoint takes that index
arc.MoveControlPoint(0, new VXYZ(50, 50));   // moves the whole arc" },

                { "ParamKind", @"// ParamKind is the type tag on a Parameter: Number, Boolean, Text or Date.
// GlobalParameters.Set infers it from the value you declare.
GlobalParameters.Set(""Radius"", 50.0, min: 10, max: 200);
GlobalParameters.Set(""Show labels"", true);
GlobalParameters.Set(""Title"", ""Site plan"");

foreach (Parameter p in GlobalParameters.All)
{
    string shown = p.Kind switch
    {
        ParamKind.Number  => $""{p.AsDouble:F2} (slider {p.EffectiveMin}..{p.EffectiveMax})"",
        ParamKind.Boolean => p.AsBool ? ""on"" : ""off"",
        ParamKind.Text    => $""\""{p.AsText}\"""",
        ParamKind.Date    => p.AsDate.ToShortDateString(),
        _                 => ""?""
    };
    VizConsole.Log($""{p.Name} [{p.Kind}] = {shown}"");
}

// Only Number parameters get a slider in the F6 panel; Date parameters are
// never written back into your source." },

                { "Parameter", @"// Parameter is the record behind one row of the Global Parameters panel.
// You never construct one — Set() returns it and Find() looks it up.
Parameter radius = GlobalParameters.Set(
    ""Radius"", 50.0,
    min: 10, max: 200, step: 5,
    group: ""Geometry"",
    description: ""Radius of the main circle"");

var circle = new VCircle(new VXYZ(0, 0), radius.AsDouble);

// Read the metadata
VizConsole.Log($""{radius.Name} = {radius.ToLiteral()}"");   // Radius = 50
VizConsole.Log($""kind {radius.Kind}, default {radius.DefaultValue}"");
VizConsole.Log($""range {radius.EffectiveMin}..{radius.EffectiveMax}, step {radius.Step}"");
VizConsole.Log($""declared at {radius.SourceFile}:{radius.SourceLine}"");

// IsOverridden is true once the panel (or Assign) has changed it away from the
// declared default — that is what stops the next run resetting your slider.
if (radius.IsOverridden)
    VizConsole.Log(""dialled in from the panel, not from the code literal"");

// Find returns null rather than throwing when the name was never declared
var maybe = GlobalParameters.Find(""Radius"");
double value = maybe?.AsDouble ?? 0;

// AsDouble / AsBool / AsText / AsDate are the typed readers; each returns a
// harmless default (0, false, the empty string, default(DateTime)) when the
// parameter is not of that kind, so none of them throws." },

                // Ray casting value types
                { "RayHit", @"// RayHit is what a successful RayCaster query returns — a readonly record
// struct with three fields, so it is cheap and never null itself (the query
// returns RayHit?, and null means nothing was hit).
var wall   = new VLine(new VXYZ(200, -100), new VXYZ(200, 100));
var pillar = new VCircle(new VXYZ(120, 0), 25);

var caster = new RayCaster(new List<Shape> { wall, pillar });
RayHit? hit = caster.FindIntersection(new VXYZ(-200, 0), new VXYZ(1, 0));

if (hit is RayHit h)
{
    VizConsole.Log($""hit {h.Shape.GetType().Name} #{h.Shape.Id}"");
    VizConsole.Log($""at {h.Point} , {h.Distance:F2} away"");   // the pillar, not the wall

    // Deconstruction works, because it is a record struct
    var (shape, point, distance) = h;
    new VPoint(point) { Color = ""Red"" }.Place();
}
else
{
    VizConsole.Log(""the ray hit nothing"");
}" },

                { "RayQuery", @"// RayQuery bundles an origin and a direction so many rays can be cast in one
// batched call. It is a readonly record struct: (VXYZ Origin, VXYZ Direction).
var targets = new List<Shape>
{
    new VCircle(new VXYZ(0, 0), 60),
    new VRectangle(new VXYZ(120, -40), 80, 80)
};
var caster = new RayCaster(targets);

// A fan of 36 rays from the origin, 10 degrees apart
var queries = new List<RayQuery>();
for (int i = 0; i < 36; i++)
    queries.Add(new RayQuery(new VXYZ(-250, 0), new VXYZ(1, 0).Rotate(i * 10 - 175)));

RayHit?[] hits = caster.FindIntersections(queries);   // parallel: true by default

for (int i = 0; i < hits.Length; i++)
    if (hits[i] is RayHit h)
        new VLine(queries[i].Origin, h.Point) { Color = ""Yellow"" }.Place();

// Direction need not be normalised, and Z is ignored — queries run on XY." },

                // Hatch pattern support types
                { "BuiltInHatches", @"// BuiltInHatches is the catalogue of AutoCAD-style .pat patterns that ship
// with the library. Get() returns a HatchType you can hand to a VHatch.
HatchType brick = BuiltInHatches.Get(BuiltInHatch.BRICK);   // by enum, typo-proof
HatchType ansi  = BuiltInHatches.Get(""ANSI31"");             // by name, case-insensitive

var boundary = new VPolygon(
    new VXYZ(-100, -60), new VXYZ(100, -60), new VXYZ(100, 60), new VXYZ(-100, 60));
var hatched = new VHatch(boundary, brick, scale: 2.0, angle: 0);
hatched.Color = ""Sienna"";

// Get() hands back a CLONE, so retuning one copy cannot poison later lookups
HatchType mine = BuiltInHatches.Get(BuiltInHatch.ANSI31);
mine.Lines[0].Angle += 30;                                  // safe: only affects `mine`

// Enumerate everything available
foreach (string name in BuiltInHatches.GetAllNames())
    VizConsole.Log(name);

// An unknown name THROWS ArgumentException — prefer the BuiltInHatch enum, or
// check GetAllNames() first. Names match case-insensitively but not loosely:
// the architectural patterns are keyed with a HYPHEN (""AR-B816""), so the string
// overload wants ""AR-B816"" while the enum member is BuiltInHatch.AR_B816 and
// maps across for you. 72 patterns are available.
// HatchType.GetBuiltIn(name) / GetBuiltIn(enum) are aliases for these." },

                { "HatchPatternLine", @"// A HatchType is a list of HatchPatternLine — one infinite family of parallel
// lines each. This is the level at which you build a pattern by hand.
var line = new HatchPatternLine(
    angle:  45,    // degrees, counter-clockwise from +X
    originX: 0,    // a point the family passes through
    originY: 0,
    deltaX:  0,    // shift ALONG the line between successive passes (staggering)
    deltaY:  6,    // perpendicular spacing between the lines
    dashes:  new double[] { 4, -2 });   // 4 on, 2 off; empty array = continuous

// Dashes: positive = dash length, negative = gap, 0 = a dot.
var dotted = new HatchPatternLine(0, 0, 0, 0, 8, 0, -8);

var pattern = new HatchType(""diag45"", ""45-degree dashed"", new List<HatchPatternLine> { line, dotted });

var boundary = new VPolygon(
    new VXYZ(-80, -50), new VXYZ(80, -50), new VXYZ(80, 50), new VXYZ(-80, 50));
var hatch = new VHatch(boundary, pattern, scale: 1.0, angle: 0);

// Clone() is deep — the dash array is copied too, so editing one is safe
HatchPatternLine copy = line.Clone();
copy.Angle = 135;                       // `line` is untouched" },

                // Host seam
                { "IGlyphOutlineProvider", @"// IGlyphOutlineProvider is a HOST seam, not something user code normally
// implements: C2VGeometry has no font engine, so DoodleSharp plugs a WPF-based
// provider into VText.GlyphOutlineProvider at startup. You mostly just check
// that one is present before asking for glyph geometry.
var word = new VText(new VXYZ(-120, 0), ""ORBIT"", 60);

if (VText.GlyphOutlineProvider == null)
{
    VizConsole.Log(""no glyph provider — ToCharShape/LiftChar will return null"");
}
else
{
    // Read the raw contours: one inner list per closed contour, in world
    // coordinates matching where the character is drawn (font, height, anchor
    // and rotation all honoured). Null for whitespace or an out-of-range index.
    var contours = VText.GlyphOutlineProvider.GetCharContours(word, 0);
    VizConsole.Log($""'O' has {contours?.Count ?? 0} contours"");   // 2: outer + bowl

    // The shape-level API is the one to prefer — it wraps the contours up for you
    var o = word.ToCharShape(0);           // single contour -> VPolyline, holes -> VGroup
    o?.Place();
}" },

                // Extension-method containers
                { "ShapeArrayExtensions", @"// ShapeArrayExtensions is what makes the array operations read as methods on a
// shape. Every one clones the source and returns List<Shape>; the source itself
// is not part of the result unless the operation places a copy on top of it.
var tile = new VRectangle(new VXYZ(-250, -150), 40, 40);
tile.FillColor = ""SteelBlue"";

tile.LinearArrayX(6, 50).DrawAll();                       // 6 across, 50 apart
tile.LinearArrayY(4, 50).DrawAll();                       // 4 up
tile.LinearArray(new VXYZ(1, 1, 0), 5, 60).DrawAll();     // along any direction
tile.RectangularArray(3, 4, rowSpacing: 55, colSpacing: 55).DrawAll();

var pip = new VCircle(new VXYZ(0, 120), 8);
pip.CircularArray(new VXYZ(0, 0), 12, totalAngleDegrees: 360, rotateItems: true).DrawAll();

var track = new VArc(new VXYZ(0, 0), 160, 200, 340);
pip.PathArray(track, 9, alignToPath: true).DrawAll();

pip.SpiralArray(new VXYZ(0, 0), 40, startRadius: 10, endRadius: 180,
                totalRevolutions: 3, rotateItems: false).DrawAll();

pip.Mirror(new VLine(new VXYZ(0, -200), new VXYZ(0, 200))).DrawAll();

// ALWAYS finish with .DrawAll(). The clones carry no Name, so without it the
// post-run sweep hides every one of them. Each also exists as a static call:
// ArrayOps.CircularArray(pip, centre, 12).
List<Shape> ring = pip.CircularArray(new VXYZ(300, 0), 8);
foreach (var s in ring) s.Color = ""Gold"";
ring.DrawAll();" },

                { "VPolygonBooleanExtensions", @"// VPolygonBooleanExtensions puts the Clipper2 boolean operations on the polygon
// itself. Every one has a BooleanOps.X(a, b) static equivalent.
var a = new VPolygon(new VXYZ(0,0), new VXYZ(100,0), new VXYZ(100,100), new VXYZ(0,100));
var b = new VPolygon(new VXYZ(50,50), new VXYZ(150,50), new VXYZ(150,150), new VXYZ(50,150));

var merged          = a.Union(b);       // ONE polygon, or null when it cannot be one
List<VPolygon> only = a.Difference(b);  // a minus b
List<VPolygon> odd  = a.Xor(b);         // symmetric difference, hole-free pieces

// TRAP: there is no usable a.Intersect(b) extension. VPolygon already declares
// IntersectionResult Intersect(ICurve), and an instance method always beats an
// extension method, so the dotted form returns the OUTLINE CROSSINGS, not the area.
List<VPolygon> both  = BooleanOps.Intersect(a, b);   // the overlapping AREA
IntersectionResult xs = a.Intersect(b);              // where the two outlines cross

if (merged != null) { merged.Name = ""merged""; }
foreach (var p in only) p.Place();      // results are unnamed — Place() keeps them

// Offsets. The extension form takes no join/end type; use BooleanOps for those.
List<VPolygon> grown  = a.OffsetPolygon(15);      // positive = outward
List<VPolygon> shrunk = a.OffsetPolygonSafe(-40); // clamped so it cannot collapse
double cap = a.MaxSafeInwardOffset();             // the clamp OffsetPolygonSafe uses

// Repair and query
List<VPolygon> simple = a.MakeSimple();           // split a tangled outline
bool tangled = a.HasSelfIntersections();
bool inside  = a.Contains(new VXYZ(50, 50));      // boundary counts as inside
double area  = a.GetArea();                       // UNSIGNED, unlike BooleanOps.Area" },

                { "RegionBooleanExtensions", @"// RegionBooleanExtensions is the Region counterpart of the polygon boolean
// extensions — same four operations, plus two queries.
var outerA = new VCircle(new VXYZ(-30, 0), 80);
var outerB = new VCircle(new VXYZ(30, 0), 80);
var regionA = new Region(outerA);      // the constructor CONSUMES its curve
var regionB = new Region(outerB);

var merged          = regionA.Union(regionB);      // null when it cannot be one region
List<Region> onlyA  = regionA.Difference(regionB);
List<Region> either = regionA.Xor(regionB);

// TRAP: regionA.Intersect(regionB) does NOT reach the extension. Region inherits
// Shape.Intersect(Shape) and does not override it, so the dotted form compiles and
// ALWAYS RETURNS NULL. Call the static instead.
List<Region> lens   = RegionBooleanOps.Intersect(regionA, regionB);

foreach (var r in lens) { r.FillColor = ""#6000FFFF""; r.Place(); }

bool inside = regionA.ContainsPoint(new VXYZ(-30, 0));  // holes excluded
double area = regionA.GetArea();                        // outer minus holes

// For more than two regions, or for the params form, call RegionBooleanOps:
//   RegionBooleanOps.Union(r1, r2, r3)" }
            };
        }

        private void InitializeMemberDescriptions()
        {
            _memberDescriptions = new Dictionary<string, string>
            {
                // GlobalParameters
                { "GlobalParameters.Set", "Declares a parameter and its default value, returning the Parameter record. Idempotent: re-running your code re-declares the same parameter without discarding a value you dialled in from the Global Parameters panel — unless the declared default itself changed, in which case the code wins. Optional min/max/step drive the panel's slider, group adds a heading, description becomes a tooltip. The declaring file and line are captured automatically so panel edits can be written back into this call." },
                { "GlobalParameters.Get", "Reads a parameter. The non-generic overload returns a ParamValue that converts implicitly to double, bool, string or DateTime. Get<T>(name) reads as a specific type and is always unambiguous; Get<T>(name, fallback) returns the fallback when the parameter is undeclared. Throws a descriptive InvalidOperationException (listing the declared names) when an undeclared parameter is read without a fallback." },
                { "GlobalParameters.Assign", "Writes a value imperatively and marks it as an override, so the next Set(...) with an unchanged default leaves it alone. This is what the Global Parameters panel calls on every slider tick." },
                { "GlobalParameters.Reset", "Drops any override on one parameter, restoring the value declared in code." },
                { "GlobalParameters.ResetAll", "Drops every override, restoring all code-declared defaults." },
                { "GlobalParameters.SetRange", "Retargets a number parameter's slider range. Panel metadata only — never written to your code — and the pinned range survives subsequent runs." },
                { "GlobalParameters.Has", "Returns true when a parameter with this name is declared. Names are case-insensitive." },
                { "GlobalParameters.Find", "Returns the full Parameter record for a name, or null when it is not declared." },
                { "GlobalParameters.ClearAll", "Empties the registry. Called when a different project is opened." },
                { "GlobalParameters.All", "Every parameter, in the order the code declared them." },
                { "GlobalParameters.Count", "How many parameters are currently declared — the number of rows the F6 panel shows. Equivalent to All.Count but without materialising the list, and thread-safe like the rest of the registry. Zero after ClearAll(), and it drops when EndRun(pruneStale: true) removes parameters whose Set(...) line you deleted." },
                { "GlobalParameters.Changed", "Raised when a parameter's value changes. Suppressed while user code is running, so the Set(...) calls inside Main() cannot trigger a re-run loop." },
                { "GlobalParameters.Reloaded", "Raised when the set of parameters changes (declared, removed, or cleared) — the signal for the panel to rebuild its rows." },
                { "GlobalParameters.BeginRun", "Marks the start of a user-code run: suppresses change notifications and starts a new declaration epoch. The host calls this; pair with EndRun in a finally." },
                { "GlobalParameters.EndRun", "Ends a run and re-enables notifications. With pruneStale: true, parameters not re-declared during the run are removed, so deleting a Set(...) line clears its panel row. Pass false when the run failed so a compile error does not blank the panel." },

                // ParamValue
                { "ParamValue.Num", "The value as a double. Unambiguous alternative to the implicit conversion — use it where + would otherwise fail to pick an overload." },
                { "ParamValue.Flag", "The value as a bool." },
                { "ParamValue.Text", "The value as a string." },
                { "ParamValue.Date", "The value as a DateTime." },
                { "ParamValue.Raw", "The boxed underlying value (double, bool, string or DateTime), or null when the parameter is undeclared." },
                { "ParamValue.Exists", "True when the parameter is declared in the registry." },
                { "ParamValue.As", "Reads the value as T, throwing a descriptive InvalidOperationException when the parameter is undeclared or holds another type. Widens the stored double to int, float or long." },

                // Parameter
                { "Parameter.Value", "The live value. Stored as double, bool, string or DateTime." },
                { "Parameter.DefaultValue", "The value the last Set(...) call declared. Reset() restores this." },
                { "Parameter.IsOverridden", "True when Value was changed from outside the code (the panel, MCP, or Assign)." },
                { "Parameter.EffectiveMin", "The slider's lower bound: the declared min, or a range derived from the default when none was given." },
                { "Parameter.EffectiveMax", "The slider's upper bound. See EffectiveMin." },
                { "Parameter.RangePinned", "True once the slider range was retargeted from the panel, after which a re-declaring Set(...) leaves Min/Max alone." },
                { "Parameter.SourceFile", "Path of the file whose Set(...) call declared this parameter, captured via CallerFilePath." },
                { "Parameter.SourceLine", "Line of the declaring Set(...) call, captured via CallerLineNumber. Used to write panel edits back into the source." },
                { "Parameter.ToLiteral", "Renders the current value the way it should appear as a C# literal in your source." },

                // VGrid Properties
                { "VGrid.Points", "Gets the collection of all VPoint objects in the grid. Points are stored in row-major order (left to right, bottom to top)." },
                { "VGrid.Location", "Gets the reference location point. If Centered is true, this is the center of the grid. If false, this is the bottom-left corner." },
                { "VGrid.XCount", "Gets the number of points along the X (horizontal) axis." },
                { "VGrid.YCount", "Gets the number of points along the Y (vertical) axis." },
                { "VGrid.XSpacing", "Gets the spacing distance between adjacent points along the X axis." },
                { "VGrid.YSpacing", "Gets the spacing distance between adjacent points along the Y axis." },
                { "VGrid.Centered", "Gets whether the grid is centered at the Location point. If true, grid is centered; if false, Location is the bottom-left corner." },
                { "VGrid.Count", "Gets the total number of points in the grid (XCount × YCount)." },
                { "VGrid.Item", "Gets a point by index (single parameter) or by column and row indices (two parameters). Indexer: grid[index] or grid[col, row]." },

                // VGrid Methods
                { "VGrid.Draw", "Draws all points in the grid to the canvas. Each point is rendered using its individual style properties." },
                { "VGrid.Clone", "Creates a deep copy of this grid with all points cloned. Returns a new VGrid instance with the same properties and point positions." },
                { "VGrid.Move", "Translates all points in the grid by the specified displacement vector. Also updates the Location property." },
                { "VGrid.Rotate", "Rotates all points in the grid around a specified pivot point by the given angle in degrees (counter-clockwise)." },
                { "VGrid.Flip", "Mirrors all points in the grid across the specified line (mirror axis). Creates a reflection of the grid." },
                { "VGrid.Scale", "Scales all points in the grid relative to a center point by the specified factor. Factor > 1 enlarges, < 1 shrinks." },
                { "VGrid.GetBounds", "Returns the axis-aligned bounding box of all points as a tuple (minPoint, maxPoint)." },
                { "VGrid.DistanceTo", "Returns the minimum distance from any point in the grid to the specified point." },
                { "VGrid.ApplyStyle", "Applies the grid's Color, FillColor, and LineWeight to all contained points." },
                { "VGrid.GetRow", "Returns a list of all points in the specified row (0-based index, row 0 is the bottom row)." },
                { "VGrid.GetColumn", "Returns a list of all points in the specified column (0-based index, column 0 is the leftmost)." },
                { "VGrid.GetCenter", "Calculates and returns the geometric center point of the grid based on its bounding box." },
                { "VGrid.ToString", "Returns a string representation of the grid: \"VGrid(XCount×YCount, Location=..., Centered=...)\"" },

                // VGroup Properties
                { "VGroup.Shapes", "Gets the list of Shape objects contained in this group. Shapes can be added, removed, or modified directly." },
                { "VGroup.Count", "Gets the number of shapes currently in the group." },
                { "VGroup.Item", "Gets a shape at the specified index. Indexer: group[index]." },

                // VGroup Methods
                { "VGroup.Add", "Adds a shape to the group and returns the group for method chaining." },
                { "VGroup.AddRange", "Adds multiple shapes to the group and returns the group for method chaining." },
                { "VGroup.Remove", "Removes the specified shape from the group. Returns true if successful." },
                { "VGroup.RemoveAt", "Removes the shape at the specified index from the group." },
                { "VGroup.Clear", "Removes all shapes from the group." },
                { "VGroup.ContainsShape", "Returns true if the specified shape is in the group." },
                { "VGroup.Flatten", "Returns a flat list of all shapes, expanding any nested groups recursively." },
                { "VGroup.ForEach", "Executes the specified action on each shape in the group." },
                { "VGroup.Where", "Returns a new VGroup containing only shapes that match the predicate." },
                { "VGroup.GetShapesOfType", "Returns all shapes of the specified type T from the group." },
                { "VGroup.ApplyStyle", "Applies the group's Color, FillColor, and LineWeight to all contained shapes." },
                { "VGroup.ApplyColor", "Applies only the group's Color to all contained shapes." },
                { "VGroup.ApplyFillColor", "Applies only the group's FillColor to all contained shapes." },
                { "VGroup.ApplyLineWeight", "Applies only the group's LineWeight to all contained shapes." },
                { "VGroup.SetOpacity", "Sets the opacity (0.0 to 1.0) for all shapes in the group by adjusting their fill color alpha." },
                { "VGroup.GetCenter", "Calculates and returns the geometric center point of all shapes in the group." },

                // VPoint Properties
                { "VPoint.X", "Gets or sets the X coordinate of the point in world units." },
                { "VPoint.Y", "Gets or sets the Y coordinate of the point in world units." },

                // VPoint Methods
                { "VPoint.AsVXYZ", "Converts this VPoint to a VXYZ coordinate with Z = 0. A VPoint also converts to VXYZ implicitly, so you can pass one anywhere a coordinate is expected — but prefer constructing VXYZ directly in new code, because every VPoint you construct draws a marker." },
                { "VPoint.Draw", "The historical name for Place(), and exactly equivalent. A point you construct is already on the canvas, so on it the call only sets IsExplicitlyDrawn, which exempts an unnamed point from the auto-hide pass. Prefer Place() in new code." },
                { "VPoint.Clone", "Creates a deep copy of this point with all properties duplicated." },
                { "VPoint.Move", "Translates the point by the specified displacement vector." },
                { "VPoint.Rotate", "Rotates the point around the specified pivot by the given angle in degrees." },
                { "VPoint.Flip", "Mirrors the point across the specified line (axis of reflection)." },
                { "VPoint.Scale", "Scales the point position relative to a center point by the specified factor." },
                { "VPoint.GetBounds", "Returns the bounding box (point itself for both min and max)." },
                { "VPoint.DistanceTo", "Returns the Euclidean distance from this point to another point." },
                { "VPoint.Intersect", "Returns a copy of this point if it lies inside the other shape, otherwise null." },
                { "VPoint.GetControlPoints", "Returns the single Move handle at the point's own position, labelled \"Position\"." },
                { "VPoint.MoveControlPoint", "Index 0 sets X and Y to the given position; any other index is ignored." },
                { "VPoint.ToString", "Returns a string representation: \"VPoint(X, Y)\"." },

                // VLine Properties
                { "VLine.Start", "Gets or sets the starting point of the line segment (VXYZ). This is the canonical endpoint API — a concrete VLine has no StartPoint property; ICurve.StartPoint is implemented explicitly and only visible through the ICurve interface." },
                { "VLine.End", "Gets or sets the ending point of the line segment (VXYZ). See Start for the note about EndPoint." },
                { "VLine.MidPoint", "Gets the midpoint of the line segment, computed as Evaluate(0.5)." },
                { "VLine.Direction", "Gets the unit vector pointing from Start to End. Returns VXYZ.Zero for a zero-length line rather than throwing." },
                { "VLine.Vertices", "Gets a new list containing Start and End." },
                { "VLine.Evaluate", "Returns the point at the given normalized parameter by linear interpolation. Values outside [0, 1] are not clamped, so Evaluate(2) lies beyond End on the extended line." },
                { "VLine.SelfIntersecting", "Always returns false (lines cannot self-intersect)." },

                // VLine Methods
                { "VLine.Draw", "Kept for backwards compatibility — the line is already on the canvas from construction. Calling it marks the shape as explicitly drawn, which exempts it from the auto-hide pass for unnamed shapes." },
                { "VLine.Clone", "Creates a deep copy of this line with all properties duplicated." },
                { "VLine.Move", "Translates the line by the specified displacement vector." },
                { "VLine.Rotate", "Rotates the line around the specified pivot by the given angle in degrees." },
                { "VLine.Flip", "Mirrors the line across the specified axis line." },
                { "VLine.Scale", "Scales the line relative to a center point by the specified factor." },
                { "VLine.GetBounds", "Returns the axis-aligned bounding box of the line segment." },
                { "VLine.Contains", "Returns true when the point lies ON the segment. A line encloses no area, so that is the only sensible reading of containment; the test is the distance to the segment judged against a tolerance scaled to the line's own length (CurveGeometry.IsOnStroke). It is no longer a bounding-box test, so a point at the far corner of a diagonal's bounding box correctly returns false." },
                { "VLine.DistanceTo", "Returns the exact shortest distance from the point to this line SEGMENT, computed by projecting onto the line and clamping to [Start, End] — so a point beyond either end measures to that endpoint rather than to the infinite line. Zero for a point on the segment." },
                { "VLine.GetLength", "Returns the length of the line segment (the distance from Start to End)." },
                { "VLine.Divide", "Divides the line into the given number of equal segments and returns numberOfSegments + 1 points, including both endpoints. Returns an empty list when numberOfSegments is zero or negative." },
                { "VLine.Measure", "Returns points from Start at fixed distance intervals along the line. The last point may fall short of End when the length is not an exact multiple of the interval. Returns an empty list for a non-positive interval or a degenerate line." },
                { "VLine.Project", "Projects a point onto the line and returns the closest point, clamped to the segment: a point beyond either end projects onto that endpoint." },
                { "VLine.PointAtSegmentLength", "Returns the point at the specified distance from Start, measured along the line's direction. Not clamped — a distance greater than the length returns a point beyond End." },
                { "VLine.PointAtParameter", "Returns a point on the line at the given normalized parameter (0 to 1)." },
                { "VLine.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the line to the given point." },
                { "VLine.Offset", "Creates a parallel line offset by the specified distance." },
                { "VLine.SplitAtPoint", "Splits the line at the specified point, returning two line segments." },
                { "VLine.SetBounds", "Trims the line in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. Because VXYZ is immutable, Start and End are reassigned to new instances — anything holding the old endpoint objects will not see the change. Parameters are clamped to [0,1] and swapped if reversed; passing equal values collapses the line to a point." },
                { "VLine.NormalAtPoint", "Returns the unit normal (perpendicular) to the line. It is constant along a line, so the argument is ignored; the direction is the line direction rotated -90 degrees." },
                { "VLine.Intersect", "Computes intersection with another curve." },
                { "VLine.PointsAtChordLengthFromPoint", "Returns points on the line at a chord distance from a given point." },
                { "VLine.ToString", "Returns a string representation of the line." },

                // VCircle Properties
                { "VCircle.Center", "Gets or sets the center point of the circle." },
                { "VCircle.Radius", "Gets or sets the radius of the circle." },
                { "VCircle.Diameter", "Gets or sets the diameter — always 2 x Radius, so the two stay in step whichever you set. Setting it resizes the circle about its centre; Center does not move, and a diameter of 0 collapses the circle to its centre point. Note the static factories are named FromCenterDiameter(center, diameter) and FromCenterDiameter(cx, cy, diameter); there is no ByDiameter." },
                { "VCircle.Circumference", "Gets the circumference of the circle (2π × Radius)." },
                { "VCircle.Area", "Gets the area of the circle (π × Radius²)." },
                { "VCircle.SelfIntersecting", "Always returns false (circles cannot self-intersect)." },
                { "VCircle.StartPoint", "Gets a point on the circle (at 0 degrees)." },
                { "VCircle.EndPoint", "Gets a point on the circle (same as StartPoint for closed curves)." },

                // VCircle Methods
                { "VCircle.Draw", "Renders the circle to the canvas." },
                { "VCircle.Clone", "Creates a deep copy of this circle with all properties duplicated." },
                { "VCircle.Move", "Translates the circle by the specified displacement vector." },
                { "VCircle.Rotate", "Rotates the circle around the specified pivot by the given angle in degrees." },
                { "VCircle.Flip", "Mirrors the circle across the specified axis line." },
                { "VCircle.Scale", "Scales the circle relative to a center point by the specified factor." },
                { "VCircle.GetBounds", "Returns the axis-aligned bounding box of the circle." },
                { "VCircle.Contains", "Returns true if the specified point is inside or on the circle — an exact disc test, not the bounding box." },
                { "VCircle.DistanceTo", "Returns the shortest distance from the point to the CIRCUMFERENCE: zero for a point exactly on the circle, and positive both inside and outside. It is a distance to the outline (matching VPolygon.DistanceTo), not a signed depth, so pair it with Contains when you need to know which side of the circle the point is on." },
                { "VCircle.GetLength", "Returns the circumference of the circle." },
                { "VCircle.Divide", "Divides the circle into equal arc segments, returning the division points." },
                { "VCircle.Measure", "Returns points along the circle at fixed arc length intervals." },
                { "VCircle.Project", "Projects a point onto the circle, returning the closest point on the circle." },
                { "VCircle.PointAtParameter", "Returns a point on the circle at the given normalized parameter (0 to 1), where 0 and 1 are at angle 0 (3 o'clock)." },
                { "VCircle.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the circle to the given point." },
                { "VCircle.Offset", "Creates a concentric circle offset by the specified distance (+ = outward)." },
                { "VCircle.SetBounds", "Not supported: trimming a circle would produce an arc (a different shape type). Throws NotSupportedException. Use SplitAtPoint to obtain VArc segments instead." },
                { "VCircle.NormalAtPoint", "Returns the normal vector at the specified point on the circle (points outward)." },
                { "VCircle.Intersect", "Computes intersection with another curve." },
                { "VCircle.ToString", "Returns a string representation of the circle." },

                // VXLine Properties (infinite construction line)
                { "VXLine.BasePoint", "Gets or sets the base point that the infinite line passes through." },
                { "VXLine.Direction", "Gets or sets the direction vector of the line (normalized)." },
                { "VXLine.RenderExtent", "Gets or sets the extent used for rendering (default: 10000). Points at ±RenderExtent define the visual segment." },
                { "VXLine.StartPoint", "Gets a point far in the negative direction (for rendering)." },
                { "VXLine.EndPoint", "Gets a point far in the positive direction (for rendering)." },
                { "VXLine.SelfIntersecting", "Always returns false (infinite lines cannot self-intersect)." },
                { "VXLine.Vertices", "Gets the base point as the only vertex." },

                // VXLine Constructors
                { "VXLine(VXYZ, VXYZ)", "Creates an infinite line through basePoint in the given DIRECTION — the second argument is a direction vector, NOT a second point on the line. It is normalised for you, so its length is irrelevant. To build a line through two points p1 and p2, either use the four-coordinate overload or subtract: new VXLine(p1, p2 - p1)." },
                { "VXLine(double, double, double, double)", "Creates an infinite line THROUGH the two points (x1, y1) and (x2, y2). This is the through-two-points overload; the two-VXYZ constructor is base-point-plus-direction instead." },

                // VXLine Static Methods
                { "VXLine.Horizontal", "Creates a horizontal infinite line at the specified Y coordinate." },
                { "VXLine.Vertical", "Creates a vertical infinite line at the specified X coordinate." },

                // VXLine Methods
                { "VXLine.Draw", "Renders the infinite line to the canvas (clipped to render extent)." },
                { "VXLine.Clone", "Creates a deep copy of this infinite line." },
                { "VXLine.Move", "Translates the line by moving the base point." },
                { "VXLine.Rotate", "Rotates the line around the specified pivot by the given angle in degrees." },
                { "VXLine.Flip", "Mirrors the line across the specified axis line." },
                { "VXLine.Scale", "Scales the line by moving the base point relative to a center." },
                { "VXLine.GetBounds", "Returns bounds based on render extent." },
                { "VXLine.Contains", "Returns true when the point lies ON the infinite line, judged with a tolerance scaled to how far the point is from BasePoint. Unlike a segment there is no end to fall off — a point a mile away along the direction is still on the line." },
                { "VXLine.DistanceTo", "Returns the PERPENDICULAR distance from the point to the infinite line. Nothing is clamped, because the line extends forever in both directions — this is the one measurement where a segment and an infinite line genuinely differ. Note the line's point property is BasePoint (VRay's is Origin)." },
                { "VXLine.GetLength", "Returns positive infinity (infinite line)." },
                { "VXLine.Project", "Projects a point onto the infinite line." },
                { "VXLine.GetPointAtParameter", "Gets a point on the line at the specified parameter (0 = BasePoint)." },
                { "VXLine.PointAtParameter", "Returns a point at normalized parameter (0 to 1 maps to -RenderExtent to +RenderExtent)." },
                { "VXLine.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the infinite line to the given point." },
                { "VXLine.GetTwoPoints", "Gets two distinct points on the line for algorithms requiring two points." },
                { "VXLine.ToFiniteLine", "Converts to a finite VLine segment spanning RenderExtent either side of BasePoint, for intersection and length calculations. The returned line is NOT drawn — this is a conversion for maths, not a request for a second line on the canvas. Call .Place() on it if you do want it shown." },
                { "VXLine.SplitAtPoint", "Splits the line at a point, returning two rays going in opposite directions." },
                { "VXLine.SetBounds", "Not supported: trimming an infinite construction line would produce a finite line (a different shape type). Throws NotSupportedException. Use SplitAtPoint to obtain ray segments instead." },
                { "VXLine.Intersect", "Computes intersection with another curve." },
                { "VXLine.ToString", "Returns a string representation of the infinite line." },

                // VRay Properties (semi-infinite ray)
                { "VRay.Origin", "Gets or sets the origin point where the ray starts." },
                { "VRay.Direction", "Gets or sets the direction vector of the ray (normalized)." },
                { "VRay.RenderExtent", "Gets or sets the extent used for rendering (default: 10000)." },
                { "VRay.StartPoint", "Gets the origin (same as Origin property)." },
                { "VRay.EndPoint", "Gets a point at RenderExtent distance from origin (for rendering)." },
                { "VRay.SelfIntersecting", "Always returns false (rays cannot self-intersect)." },
                { "VRay.Vertices", "Gets the origin as the only vertex." },

                // VRay Constructors
                { "VRay(VXYZ, VXYZ)", "Creates a ray starting at origin and extending in the given DIRECTION — the second argument is a direction vector, NOT a point the ray passes through. It is normalised for you, so its length is irrelevant. To aim a ray at a target point, use the four-coordinate overload, or subtract: new VRay(origin, target - origin)." },
                { "VRay(double, double, double, double)", "Creates a ray from (originX, originY) THROUGH (throughX, throughY) — the through-point form. This is the only constructor that takes a second point rather than a direction; the two-VXYZ overload is origin-plus-direction." },

                // VRay Static Methods
                { "VRay.HorizontalRight", "Creates a horizontal ray pointing right from the specified point." },
                { "VRay.HorizontalLeft", "Creates a horizontal ray pointing left from the specified point." },
                { "VRay.VerticalUp", "Creates a vertical ray pointing up from the specified point." },
                { "VRay.VerticalDown", "Creates a vertical ray pointing down from the specified point." },
                { "VRay.AtAngle", "Creates a ray at a specified angle from the origin (degrees, counter-clockwise from +X)." },

                // VRay Methods
                { "VRay.Draw", "Renders the ray to the canvas (from origin to render extent)." },
                { "VRay.Clone", "Creates a deep copy of this ray." },
                { "VRay.Move", "Translates the ray by moving the origin." },
                { "VRay.Rotate", "Rotates the ray around the specified pivot by the given angle in degrees." },
                { "VRay.Flip", "Mirrors the ray across the specified axis line." },
                { "VRay.Scale", "Scales the ray by moving the origin relative to a center." },
                { "VRay.GetBounds", "Returns bounds from origin to render extent." },
                { "VRay.Contains", "Returns true when the point lies ON the ray, judged with a tolerance scaled to how far the point is from Origin. It is FALSE for anything behind the origin, even when the point sits exactly on the ray's backwards extension — that half of the line is not part of the ray. (ContainsPoint is the older, equivalent named method.)" },
                { "VRay.DistanceTo", "Returns the shortest distance from the point to the ray: the perpendicular distance where the point projects onto the ray, and the distance to Origin for anything behind the start. Note the ray's point property is Origin (VXLine's is BasePoint)." },
                { "VRay.GetLength", "Returns positive infinity (semi-infinite ray)." },
                { "VRay.Project", "Projects a point onto the ray. Returns origin if projection is behind the ray." },
                { "VRay.GetPointAtDistance", "Gets a point on the ray at the specified distance from origin." },
                { "VRay.PointAtParameter", "Returns a point at normalized parameter (0 = origin, 1 = RenderExtent)." },
                { "VRay.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the ray to the given point." },
                { "VRay.ContainsPoint", "Checks if a point is on the ray (within tolerance)." },
                { "VRay.ToFiniteLine", "Converts to a finite VLine segment running from Origin out to RenderExtent, for intersection and length calculations. The returned line is NOT drawn — call .Place() on it if you want it on the canvas." },
                { "VRay.ToXLine", "Converts to an infinite VXLine through the same origin and direction, so the geometry extends backwards past Origin as well. The returned line is NOT drawn — call .Place() on it if you want it on the canvas." },
                { "VRay.SplitAtPoint", "Splits the ray at a point, returning a line segment and a continuing ray." },
                { "VRay.SetBounds", "Not supported: trimming a ray would produce a finite line (a different shape type). Throws NotSupportedException. Use SplitAtPoint to obtain a VLine segment instead." },
                { "VRay.Intersect", "Computes intersection with another curve." },
                { "VRay.ToString", "Returns a string representation of the ray." },

                // VRectangle Properties (inherits from VPolygon)
                { "VRectangle.Corner", "Gets or sets the bottom-left corner point of the rectangle. Setting this updates the underlying polygon points." },
                { "VRectangle.Width", "Gets or sets the width of the rectangle (along X axis). Setting this updates the underlying polygon points." },
                { "VRectangle.Height", "Gets or sets the height of the rectangle (along Y axis). Setting this updates the underlying polygon points." },
                { "VRectangle.RotationAngle", "Gets or sets the rotation angle in degrees (counter-clockwise) of the rectangle about its own centre. Setting it rebuilds the four corner points. It OVERRIDES Shape.RotationAngle rather than shadowing it, so there is only one property: it means the same thing whether you reach the rectangle through a VRectangle-typed or a Shape-typed variable, and RotateAnimation — which writes through a Shape reference — drives the real geometry. While this was a `new` member the writer and the reader resolved to two different properties, so rotation animations on rectangles silently did nothing." },
                { "VRectangle.Area", "Inherited from VPolygon. The shoelace area, always positive regardless of vertex winding. Use SignedArea when you need the winding direction." },
                { "VRectangle.SignedArea", "Inherited from VPolygon. The shoelace area with sign: positive for counter-clockwise vertices, negative for clockwise." },
                { "VRectangle.Points", "Inherited from VPolygon. Gets the four corner vertices as a list of VXYZ." },

                // VRectangle Constructors
                { "VRectangle(VXYZ, double, double)", "Creates a rectangle from a corner point, width, and height." },
                { "VRectangle(double, double, double, double)", "Creates a rectangle from x, y coordinates, width, and height." },
                { "VRectangle(VXYZ, VXYZ)", "Creates a rectangle from two corner points (bottom-left and top-right)." },

                // VRectangle Methods
                { "VRectangle.Draw", "Renders the rectangle to the canvas." },
                { "VRectangle.Clone", "Creates a deep copy of this rectangle with all properties duplicated." },
                { "VRectangle.Move", "Translates the rectangle by the specified displacement vector." },
                { "VRectangle.Rotate", "Rotates the rectangle around the specified pivot by the given angle in degrees. Also accumulates the RotationAngle." },
                { "VRectangle.Flip", "Mirrors the rectangle across the specified axis line." },
                { "VRectangle.Scale", "Scales the rectangle relative to a center point by the specified factor." },
                { "VRectangle.GetBounds", "Returns the axis-aligned bounding box of the rectangle." },
                { "VRectangle.Contains", "Returns true if the specified point is inside or on the rectangle. Uses simple bounds check for axis-aligned, polygon containment for rotated." },
                { "VRectangle.DistanceTo", "Returns the shortest distance from the point to the rectangle's BOUNDARY, inherited from VPolygon. Zero on an edge, and positive both inside and outside — it is not a signed depth. Honours the rotation, since the corner points are the real geometry." },
                { "VRectangle.PointAtParameter", "Returns a point on the rectangle perimeter at the given normalized parameter (0 to 1)." },
                { "VRectangle.Slice", "Inherited from VPolygon. Slices the rectangle along the infinite line through two points; being convex, a rectangle always yields two pieces (or one, if the line misses or grazes an edge)." },
                { "VRectangle.ToString", "Returns a string representation of the rectangle." },

                // VArc Properties
                { "VArc.Center", "Gets or sets the center point of the arc." },
                { "VArc.Radius", "Gets or sets the radius of the arc." },
                { "VArc.StartAngle", "Gets or sets the start angle in degrees (0 = positive X axis)." },
                { "VArc.EndAngle", "Gets or sets the end angle in degrees (counter-clockwise from start)." },
                { "VArc.StartPoint", "Gets the starting point of the arc." },
                { "VArc.EndPoint", "Gets the ending point of the arc." },
                { "VArc.SelfIntersecting", "Always returns false (arcs cannot self-intersect)." },

                // VArc Methods
                { "VArc.Draw", "Renders the arc to the canvas." },
                { "VArc.Clone", "Creates a deep copy of this arc with all properties duplicated." },
                { "VArc.Move", "Translates the arc by the specified displacement vector." },
                { "VArc.Rotate", "Rotates the arc around the specified pivot by the given angle in degrees." },
                { "VArc.Flip", "Mirrors the arc across the specified axis line." },
                { "VArc.Scale", "Scales the arc relative to a center point by the specified factor." },
                { "VArc.GetBounds", "Returns the axis-aligned bounding box of the arc." },
                { "VArc.Contains", "Returns true when the point lies ON the arc — an arc encloses no area, so that is the only sensible reading. It is DistanceTo judged against a tolerance scaled to Radius, and it honours the sweep: a point on the circle but outside StartAngle..EndAngle returns false." },
                { "VArc.DistanceTo", "Returns the exact shortest distance from the point to the arc, honouring the sweep: when the ray from the centre through the point passes through the swept sector the distance is purely radial (|distanceToCentre - Radius|), and otherwise it is the distance to the nearer of StartPoint/EndPoint. A point at the centre returns Radius. Computed in closed form rather than by sampling, so the centre of a radius-10 half-circle measures exactly 10." },
                { "VArc.GetLength", "Returns the arc length." },
                { "VArc.Divide", "Divides the arc into equal segments, returning the division points." },
                { "VArc.MidPoint", "The point halfway along the arc — Evaluate(0.5), so it follows the sweep rather than being the midpoint of the chord. Read-only; move the arc by setting Center." },
                { "VArc.Evaluate", "The point on the arc at the normalised parameter, 0 at StartAngle and 1 at EndAngle, interpolating the sweep angle linearly. On a circular arc equal angle steps are equal arc-length steps, so this is also the arc-length parameterisation and agrees with PointAtParameter and Divide. Parameters outside [0, 1] are NOT clamped here — they extrapolate around the full circle." },
                { "VArc.Measure", "Returns points along the arc at fixed distance intervals." },
                { "VArc.Project", "Projects a point onto the arc, returning the closest point on the arc." },
                { "VArc.PointAtParameter", "Returns a point on the arc at the given normalized parameter (0 to 1)." },
                { "VArc.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the arc to the given point." },
                { "VArc.Offset", "Creates a concentric arc offset by the specified distance." },
                { "VArc.SetBounds", "Trims the arc in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. StartAngle/EndAngle are rescaled to span the new range. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VArc.NormalAtPoint", "Returns the normal vector at the specified point on the arc." },
                { "VArc.Intersect", "Computes intersection with another curve." },
                { "VArc.ToString", "Returns a string representation of the arc." },

                // VEllipse Properties
                { "VEllipse.Center", "Gets or sets the center point of the ellipse." },
                { "VEllipse.RadiusX", "Gets or sets the horizontal radius (semi-major or semi-minor axis)." },
                { "VEllipse.RadiusY", "Gets or sets the vertical radius (semi-major or semi-minor axis)." },
                { "VEllipse.Area", "Gets the area of the ellipse (π × RadiusX × RadiusY)." },
                { "VEllipse.Circumference", "Gets the approximate circumference of the ellipse using Ramanujan's formula." },
                { "VEllipse.SelfIntersecting", "Always returns false (ellipses cannot self-intersect)." },

                // VEllipse Methods
                { "VEllipse.Draw", "Renders the ellipse to the canvas." },
                { "VEllipse.Clone", "Creates a deep copy of this ellipse with all properties duplicated." },
                { "VEllipse.Move", "Translates the ellipse by the specified displacement vector." },
                { "VEllipse.Rotate", "Rotates the ellipse around the specified pivot by the given angle in degrees." },
                { "VEllipse.Flip", "Mirrors the ellipse across the specified axis line." },
                { "VEllipse.Scale", "Scales the ellipse relative to a center point by the specified factor." },
                { "VEllipse.GetBounds", "Returns the axis-aligned bounding box of the ellipse." },
                { "VEllipse.Contains", "For a FULL ellipse (a 360-degree sweep) this is an exact interior test — the implicit equation (dx/RadiusX)² + (dy/RadiusY)² <= 1, with dx/dy measured from Center. For a PARTIAL sweep there is no enclosed area, so it means 'lies on the curve' instead, judged with a tolerance scaled to the larger radius. Either way it is not a bounding-box test: a point in a corner of the box is outside." },
                { "VEllipse.Evaluate", "Returns the point at a parameter in [0, 1] measured by ARC LENGTH, so 0.5 is the halfway point along the curve and Divide(n) gives evenly spaced points. This is what PointAtParameter calls. It used to interpolate the sweep angle linearly, which on an eccentric ellipse bunched divisions up near the flat ends; every other ICurve is length-parameterised, and callers like Measure and the animation samplers assume it. Use EvaluateByAngle when you want the angle-linear reading instead." },
                { "VEllipse.EvaluateByAngle", "Returns the point at a parameter in [0, 1] interpolated linearly through the sweep ANGLE, from StartAngle to EndAngle. This is the right choice when you want equal angles rather than equal distances — radial spokes, sector boundaries, a hand sweeping round a dial. For anything spaced along the curve use Evaluate. On a circle the two agree, because angle and arc length are proportional there; they diverge as the ellipse becomes more eccentric." },
                { "VEllipse.DistanceTo", "Returns the shortest distance from the point to the ellipse's CURVE, computed by sampling it. It honours the sweep: on a partial ellipse a point past either end measures to the nearer endpoint, not to the full ellipse. Zero on the curve, positive both inside and outside — pair it with Contains for the side." },
                { "VEllipse.GetLength", "Returns the approximate perimeter of the ellipse." },
                { "VEllipse.PointAtParameter", "Returns a point on the ellipse at the given normalized parameter (0 to 1)." },
                { "VEllipse.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the ellipse to the given point." },
                { "VEllipse.SetBounds", "Trims the ellipse in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. The trim is by ARC LENGTH, matching Evaluate — SetBounds(0.25, 0.75) keeps the middle half of the CURVE, not of the sweep angle — and StartAngle/EndAngle are set to the angles at those arc fractions. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VEllipse.Intersect", "Computes intersection with another curve." },
                { "VEllipse.ToString", "Returns a string representation of the ellipse." },

                // VPolygon Properties
                { "VPolygon.Points", "Gets or sets the list of vertex points defining the polygon." },
                { "VPolygon.Curves", "Gets the list of curves used to construct the polygon (if created from curves)." },
                { "VPolygon.StartPoint", "Gets the first vertex of the polygon." },
                { "VPolygon.EndPoint", "Gets the last vertex (same as StartPoint for closed polygon)." },
                { "VPolygon.SelfIntersecting", "Returns true if any edges of the polygon cross each other." },
                { "VPolygon.Area", "The area enclosed by the polygon, by the shoelace formula. ALWAYS POSITIVE — the sign of the winding is taken out with Math.Abs, so use SignedArea when you need the winding order. Returns 0 for fewer than three points. Computed on every read from the current Points, so it follows edits to the list." },
                { "VPolygon.SignedArea", "The shoelace area WITH its sign: positive when the vertices wind counter-clockwise, negative when they wind clockwise. Use it to detect or normalise winding order (`if (poly.SignedArea < 0) poly.Points.Reverse();`); use Area when you only want the magnitude. Returns 0 for fewer than three points. Note BooleanOps.Area(polygon) returns this signed value, while the polygon.GetArea() extension returns the unsigned one." },

                // VPolygon Methods
                { "VPolygon.Draw", "Renders the polygon to the canvas (closed shape)." },
                { "VPolygon.Clone", "Creates a deep copy of this polygon with all properties duplicated." },
                { "VPolygon.Move", "Translates the polygon by the specified displacement vector." },
                { "VPolygon.Rotate", "Rotates the polygon around the specified pivot by the given angle in degrees." },
                { "VPolygon.Flip", "Mirrors the polygon across the specified axis line." },
                { "VPolygon.Scale", "Scales the polygon relative to a center point by the specified factor." },
                { "VPolygon.GetBounds", "Returns the axis-aligned bounding box of the polygon." },
                { "VPolygon.Contains", "Returns true when the point lies INSIDE the polygon — a genuine even-odd ray-cast interior test (it delegates to BooleanOps.PointInPolygon), with the boundary counting as inside. This is not a bounding-box check, so it is correct for concave and other non-rectangular outlines." },
                { "VPolygon.DistanceTo", "Returns the shortest distance from the point to the polygon's BOUNDARY, including the implicit closing edge. Zero on an edge, and positive both inside and outside — it measures to the outline, not a signed depth, so pair it with Contains when you also need to know which side you are on." },
                { "VPolygon.GetLength", "Returns the total perimeter of the polygon." },
                { "VPolygon.Divide", "Divides the polygon perimeter into equal segments, returning the division points." },
                { "VPolygon.Measure", "Returns points along the polygon perimeter at fixed distance intervals." },
                { "VPolygon.AddPoint", "Adds a vertex point to the polygon." },
                { "VPolygon.Project", "Projects a point onto the polygon boundary, returning the closest point." },
                { "VPolygon.PointAtSegmentLength", "Returns the point at the specified distance along the polygon perimeter." },
                { "VPolygon.PointAtParameter", "Returns a point on the polygon perimeter at the given normalized parameter (0 to 1)." },
                { "VPolygon.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the polygon boundary to the given point." },
                { "VPolygon.Offset", "Creates an offset polygon at the specified distance (+ = outward, - = inward)." },
                { "VPolygon.PointsAtChordLengthFromPoint", "Returns points on the polygon at a chord distance from a given point." },
                { "VPolygon.SplitAtPoint", "Splits the polygon at the specified point into two polylines." },
                { "VPolygon.SetBounds", "Not supported: trimming a closed polygon would produce a polyline (a different shape type). Throws NotSupportedException. Use SplitAtPoint to obtain polyline segments instead." },
                { "VPolygon.NormalAtPoint", "Returns the outward normal vector at the specified point on the polygon." },
                { "VPolygon.Intersect", "Computes intersection with another curve." },
                { "VPolygon.Slice", "Slices the polygon along the INFINITE line through linePoint1 and linePoint2 (not the segment between them), returning every resulting piece. Implemented as two half-plane intersections through Clipper2, so it is AREA-PRESERVING: the pieces always sum back to Area. Do not assume two pieces — a concave polygon whose notch straddles the cut is crossed more than twice, so one slice can legitimately return three or more. The list is never empty: a line that misses the polygon, or only grazes a single vertex or a whole edge, returns one piece (a clone of the original), as do a polygon with fewer than three points and two coincident line points (that last case also reports itself through GeometryDiagnostics, so watch the console). Every piece inherits the source polygon's Color, FillColor, LineWeight, LineType and LineTypeScale. The pieces are registered on the canvas but carry no Name, so call Place() on the ones you want to keep or the post-run sweep hides them." },
                { "VPolygon.ToString", "Returns a string representation of the polygon." },

                // VPolyline Properties
                { "VPolyline.Points", "Gets the list of points defining the polyline." },
                { "VPolyline.PointCount", "The number of vertices — equivalent to Points.Count, but null-safe, returning 0 if the point list has not been built yet. Read-only; add vertices with AddPoint. Note a closed polyline repeats its first point as its last, so PointCount counts that vertex twice." },
                { "VPolyline.StartPoint", "Gets the first point of the polyline." },
                { "VPolyline.EndPoint", "Gets the last point of the polyline." },
                { "VPolyline.SelfIntersecting", "Returns true if any segments of the polyline cross each other." },

                // VPolyline Methods
                { "VPolyline.Draw", "Renders the polyline to the canvas (open shape)." },
                { "VPolyline.AddPoint", "Appends a vertex to the END of the polyline, extending the path. Two overloads: AddPoint(VXYZ point) and AddPoint(double x, double y). To close a polyline, add a final point equal to Points[0] — nothing closes it for you, unlike VPolygon. The point list is public, so use Points.Insert to add a vertex anywhere other than the end." },
                { "VPolyline.Clone", "Creates a deep copy of this polyline with all properties duplicated." },
                { "VPolyline.Move", "Translates the polyline by the specified displacement vector." },
                { "VPolyline.Rotate", "Rotates the polyline around the specified pivot by the given angle in degrees." },
                { "VPolyline.Flip", "Mirrors the polyline across the specified axis line." },
                { "VPolyline.Scale", "Scales the polyline relative to a center point by the specified factor." },
                { "VPolyline.GetBounds", "Returns the axis-aligned bounding box of the polyline." },
                { "VPolyline.Contains", "Returns true when the point lies ON the polyline — a polyline encloses no area. It is DistanceTo judged against a tolerance scaled to the polyline's own extent (the larger of its bounding-box width and height), so a point merely inside the bounding box but off the path returns false." },
                { "VPolyline.DistanceTo", "Returns the exact shortest distance from the point to the nearest segment of the polyline. No closing edge is added — a closed polyline is written by repeating the first point as the last, so its closing segment is already in Points." },
                { "VPolyline.GetLength", "Returns the total length of all segments." },
                { "VPolyline.Divide", "Divides the polyline into equal segments, returning the division points." },
                { "VPolyline.Measure", "Returns points along the polyline at fixed distance intervals." },
                { "VPolyline.Project", "Projects a point onto the polyline, returning the closest point." },
                { "VPolyline.PointAtParameter", "Returns a point on the polyline at the given normalized parameter (0 to 1)." },
                { "VPolyline.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the polyline to the given point." },
                { "VPolyline.SetBounds", "Trims the polyline in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. Rebuilds the Points list with the trimmed endpoints plus interior vertices that fall strictly within the range. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VPolyline.Offset", "Creates a parallel polyline offset by the specified distance." },
                { "VPolyline.Intersect", "Computes intersection with another curve." },
                { "VPolyline.ToString", "Returns a string representation of the polyline." },

                // VText Properties
                { "VText.Location", "Gets or sets the anchor position of the text (VXYZ). Which corner or edge of the text box lands here is decided by Anchor; rotation by Angle happens about this point." },
                { "VText.Content", "Gets or sets the string to display. LiftChar and the indexer rewrite this string, replacing the lifted character with a space." },
                { "VText.Height", "Gets or sets the font height in world units. Default 12." },
                { "VText.Width", "Gets or sets the width of the text box in world units. Default 0, which means \"measure the rendered string\" — set a value only when you want to override the measured width used by GetBounds and by anchoring." },
                { "VText.Font", "Gets or sets the font family (VFont enum). Default VFont.Arial." },
                { "VText.FontWeight", "Gets or sets the weight (VFontWeight.Normal or Bold). Default Normal." },
                { "VText.GlyphOutlineProvider", "Static. The host-supplied IGlyphOutlineProvider that turns characters into vector contours. The desktop app sets it at startup; when it is null, ToCharShape/LiftChar/LiftChars all return null." },
                { "VText.BlankChar", "Replaces the character at the given index with a space without returning a shape. Out-of-range indices are ignored." },
                { "VText.GetAnchorOffset", "Given a measured text width and height, returns the (offsetX, offsetY) that must be added to Location to reach the box's bottom-left corner for the current Anchor." },
                { "VText.DoesIntersect", "Text-aware overlap test: the text's rotated, anchor-aware bounding quad is tested against the other shape's bounding box using the Separating Axis Theorem. Shape.DoesIntersect delegates back here, so other.DoesIntersect(text) gives the same answer." },
                { "VText.Anchor", "Gets or sets the text anchor point (VTextAnchor enum). Controls which point of the text bounding box is placed at the text's position. Default is BottomLeft." },
                { "VText.Angle", "Gets or sets the rotation of the text block in degrees, counterclockwise around Location. Characters rotate with the block (Excel-style). 0 = horizontal (default), 90 = reads bottom-to-top." },

                // VTextAnchor enum values
                { "VTextAnchor.BottomLeft", "Anchor at the bottom-left corner of the text (default). Text extends right and up from the position." },
                { "VTextAnchor.BottomCenter", "Anchor at the bottom-center of the text. Text is horizontally centered and extends up from the position." },
                { "VTextAnchor.BottomRight", "Anchor at the bottom-right corner of the text. Text extends left and up from the position." },
                { "VTextAnchor.MiddleLeft", "Anchor at the middle-left of the text. Text extends right and is vertically centered on the position." },
                { "VTextAnchor.MiddleCenter", "Anchor at the center of the text. Text is both horizontally and vertically centered on the position." },
                { "VTextAnchor.MiddleRight", "Anchor at the middle-right of the text. Text extends left and is vertically centered on the position." },
                { "VTextAnchor.TopLeft", "Anchor at the top-left corner of the text. Text extends right and down from the position." },
                { "VTextAnchor.TopCenter", "Anchor at the top-center of the text. Text is horizontally centered and extends down from the position." },
                { "VTextAnchor.TopRight", "Anchor at the top-right corner of the text. Text extends left and down from the position." },

                // VText Methods
                { "VText.Draw", "Renders the text to the canvas." },
                { "VText.Clone", "Creates a deep copy of this text with all properties duplicated." },
                { "VText.Move", "Translates the text by the specified displacement vector." },
                { "VText.Rotate", "Rotates the text around the specified pivot by the given angle in degrees. Both Location (moved around pivot) and Angle (text's own orientation) are updated, so the characters tilt by the same amount." },
                { "VText.Flip", "Mirrors the text across the specified axis line." },
                { "VText.Scale", "Scales the text relative to a center point by the specified factor." },
                { "VText.GetBounds", "Returns the axis-aligned bounding box of the text." },
                { "VText.ToCharShape", "Builds a shape from the outline of the character at the given index, positioned in world space where the character is rendered. Returns a closed VPolyline for a single-contour glyph, or a VGroup of polylines for glyphs with holes (e.g. 'O', 'A'). Does NOT modify the text. Returns null for whitespace, an out-of-range index, or when no glyph provider is available." },
                { "VText.LiftChar", "Extracts the character at the given index as a shape (see ToCharShape) AND replaces it with a space, so the glyph appears to lift out of the word. Returns the extracted shape." },
                { "VText.LiftChars", "Lifts a run of characters (start, count) into a single VGroup and blanks each in the text. Useful for morphing a selection." },
                { "VText.Item", "Indexer: text[i] lifts the character at index i out as a shape and replaces it with a space (same as LiftChar). The ergonomic form for new TransformAnimation(text[0], circle, 2). Note: reading the indexer mutates the text." },
                { "VText.ToString", "Returns a string representation of the text object." },

                // VBezier Properties
                { "VBezier.P0", "The start point of the curve (settable VXYZ). The curve passes exactly through it." },
                { "VBezier.P1", "The first control handle (settable VXYZ). The curve is pulled towards it but does not pass through it; it sets the departure tangent at P0." },
                { "VBezier.P2", "The second control handle (settable VXYZ). It sets the arrival tangent at P3." },
                { "VBezier.P3", "The end point of the curve (settable VXYZ). The curve passes exactly through it." },
                { "VBezier.Segments", "How many straight segments the curve is tessellated into for rendering, length and parameter queries. Default 32; raise it for a large or tightly curved Bezier." },
                { "VBezier.MidPoint", "The point at Bernstein parameter 0.5, read-only. Not the arc-length midpoint." },
                { "VBezier.GetRenderPoints", "The tessellated polyline the renderer draws: Segments + 1 points from P0 to P3. Useful for feeding the curve to code that wants plain vertices." },
                { "VBezier.Evaluate", "The exact point at Bernstein parameter t (0 at P0, 1 at P3), computed from the cubic directly rather than from the tessellation. Note this is NOT arc-length parameterisation — for evenly spaced points use PointAtParameter or Divide." },
                { "VBezier.StartPoint", "Gets the starting point of the Bezier curve (same as P0). Read-only — assign P0 to move it." },
                { "VBezier.EndPoint", "Gets the ending point of the Bezier curve (same as P3). Read-only — assign P3 to move it." },
                { "VBezier.SelfIntersecting", "Returns true if the Bezier curve crosses itself." },

                // VBezier Methods
                { "VBezier.Draw", "Renders the Bezier curve to the canvas." },
                { "VBezier.Clone", "Creates a deep copy of this Bezier with all properties duplicated." },
                { "VBezier.Move", "Translates the Bezier by the specified displacement vector." },
                { "VBezier.Rotate", "Rotates the Bezier around the specified pivot by the given angle in degrees." },
                { "VBezier.Flip", "Mirrors the Bezier across the specified axis line." },
                { "VBezier.Scale", "Scales the Bezier relative to a center point by the specified factor." },
                { "VBezier.GetBounds", "Returns the axis-aligned bounding box of the Bezier curve." },
                { "VBezier.GetLength", "Returns the approximate arc length of the Bezier curve." },
                { "VBezier.Contains", "Returns true when the point lies ON the curve — a Bezier encloses no area. It is DistanceTo judged against a tolerance scaled to the curve's own extent (the larger of its bounding-box width and height)." },
                { "VBezier.DistanceTo", "Returns the shortest distance from the point to the curve. There is no practical closed form, so the curve is sampled into 96 points and measured as a polyline — accurate to well under a pixel at normal zoom, and cheap enough for interactive use." },
                { "VBezier.Divide", "Divides the Bezier into equal arc-length segments." },
                { "VBezier.PointAtParameter", "Returns a point on the Bezier curve at the given normalized parameter (0 to 1)." },
                { "VBezier.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the Bezier curve to the given point." },
                { "VBezier.SetBounds", "Trims the Bezier in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. Uses De Casteljau subdivision twice (split at end, then at start/end) for an exact trim. P0..P3 are REASSIGNED to new VXYZ values, since VXYZ is immutable — anything holding the old control points will not see the change. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VBezier.Intersect", "Computes intersection with another curve." },
                { "VBezier.ToString", "Returns a string representation of the Bezier curve." },

                // VSpline Properties
                { "VSpline.ControlPoints", "Gets the list of control points defining the spline." },
                { "VSpline.StartPoint", "Gets the starting point of the spline." },
                { "VSpline.EndPoint", "Gets the ending point of the spline." },
                { "VSpline.SelfIntersecting", "Returns true if the spline crosses itself." },

                // VSpline Methods
                { "VSpline.Draw", "Renders the spline curve to the canvas." },
                { "VSpline.Clone", "Creates a deep copy of this spline with all properties duplicated." },
                { "VSpline.Move", "Translates the spline by the specified displacement vector." },
                { "VSpline.Rotate", "Rotates the spline around the specified pivot by the given angle in degrees." },
                { "VSpline.Flip", "Mirrors the spline across the specified axis line." },
                { "VSpline.Scale", "Scales the spline relative to a center point by the specified factor." },
                { "VSpline.GetBounds", "Returns the axis-aligned bounding box of the spline." },
                { "VSpline.GetLength", "Returns the approximate arc length of the spline." },
                { "VSpline.Contains", "Returns true when the point lies ON the spline — a spline encloses no area. It is DistanceTo judged against a tolerance scaled to the curve's own extent (the larger of its bounding-box width and height)." },
                { "VSpline.DistanceTo", "Returns the shortest distance from the point to the spline. There is no practical closed form, so the curve is sampled into 96 points and measured as a polyline — accurate to well under a pixel at normal zoom, and cheap enough for interactive use." },
                { "VSpline.Divide", "Divides the spline into equal arc-length segments." },
                { "VSpline.Evaluate", "The point on the spline at the normalised parameter t. The parameter is spread evenly across the SPANS between control points, not by arc length — so t = 0.5 on a four-point spline lands on the middle control point regardless of how the spacing varies. Use Divide(n) or Measure(len) when you need genuinely even spacing. Two control points degrade to a straight interpolation, and fewer than two returns the single point (or the origin)." },
                { "VSpline.GetRenderPoints", "The tessellated polyline the renderer actually draws: (ControlPoints.Count - 1) × SegmentsPerSpan + 1 points sampled through Evaluate. Raise SegmentsPerSpan for a smoother curve at the cost of vertices. Handy for exporting the spline as a point list, or for feeding a VPolyline; the list is freshly built on every call, so cache it in a loop." },
                { "VSpline.PointAtParameter", "Returns a point on the spline at the given normalized parameter (0 to 1)." },
                { "VSpline.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the spline to the given point." },
                { "VSpline.SetBounds", "Trims the spline in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. The trimmed curve is resampled at the original render resolution so the new Catmull-Rom passes through dense interpolating points and tracks the original path closely. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VSpline.Intersect", "Computes intersection with another curve." },
                { "VSpline.ToString", "Returns a string representation of the spline." },

                // VArrow Properties
                { "VArrow.Start", "Gets or sets the starting point of the arrow." },
                { "VArrow.End", "Gets or sets the ending point (tip) of the arrow." },
                { "VArrow.HeadLength", "Length of each arrowhead wing in world units. Default 15. It does not scale with the shaft, so a short arrow with the default head looks head-heavy — reduce it for small arrows." },
                { "VArrow.HeadAngle", "Half-angle in degrees between each arrowhead wing and the shaft. Default 30, giving a 60-degree head. Larger values give a broader, flatter head." },
                { "VArrow.DoubleEnded", "When true, an identical head is drawn at Start as well as at End. Default false." },
                { "VArrow.MidPoint", "The midpoint of the shaft, read-only. This is control point 0, the whole-shape move handle." },
                { "VArrow.GetStartArrowhead", "The two wing tip coordinates of the head at Start, as a (VXYZ, VXYZ) tuple. Returned whether or not DoubleEnded is set, so check the flag first if you are reproducing what is drawn." },
                { "VArrow.GetEndArrowhead", "The two wing tip coordinates of the head at End, as a (VXYZ, VXYZ) tuple — the geometry the renderer draws." },

                // VArrow Methods
                { "VArrow.Draw", "Renders the arrow to the canvas." },
                { "VArrow.Clone", "Creates a deep copy of this arrow with all properties duplicated." },
                { "VArrow.Move", "Translates the arrow by the specified displacement vector." },
                { "VArrow.Rotate", "Rotates the arrow around the specified pivot by the given angle in degrees." },
                { "VArrow.Flip", "Mirrors the arrow across the specified axis line." },
                { "VArrow.Scale", "Scales the arrow relative to a center point by the specified factor." },
                { "VArrow.GetBounds", "Returns the axis-aligned bounding box of the arrow." },
                { "VArrow.ToString", "Returns a string representation of the arrow." },

                // VDimension Properties
                { "VDimension.Point1", "Gets or sets the first measurement point." },
                { "VDimension.Point2", "Gets or sets the second measurement point." },
                { "VDimension.Offset", "Gets or sets the offset distance for the dimension line from the measured points." },
                { "VDimension.ExtensionLength", "Length of the extension lines that run from the measured points out to the dimension line. Default 10." },
                { "VDimension.ArrowSize", "Gets or sets the size of the arrowheads at both ends of the dimension line." },
                { "VDimension.TextHeight", "Gets or sets the height of the dimension text." },
                { "VDimension.DecimalPlaces", "Gets or sets the number of decimal places for distance display." },
                { "VDimension.ExtendBeyondDimLines", "Gets or sets how far extension lines extend beyond the dimension line." },
                { "VDimension.OffsetFromOrigin", "Gets or sets the gap between the origin point and the start of the extension line." },
                { "VDimension.SuppressExtLine1", "If true, the first extension line (at Point1) is not drawn." },
                { "VDimension.SuppressExtLine2", "If true, the second extension line (at Point2) is not drawn." },
                { "VDimension.Prefix", "Gets or sets the text prefix prepended to the dimension value (e.g. \"L=\")." },
                { "VDimension.Suffix", "Gets or sets the text suffix appended to the dimension value (e.g. \"mm\")." },
                { "VDimension.CustomText", "Gets or sets custom text. If null, shows the calculated distance with Prefix/Suffix." },
                { "VDimension.Distance", "Gets the calculated distance between Point1 and Point2 (read-only)." },
                { "VDimension.TextBackgroundOpaque", "If true, an opaque background is drawn behind the dimension text using the canvas background color." },
                { "VDimension.DisplayText", "Gets the display text including Prefix and Suffix (read-only)." },
                { "VDimension.ExtensionLineColor", "Gets or sets the color for extension lines. When null (default), uses the base Color property." },
                { "VDimension.DimensionLineColor", "Gets or sets the color for the dimension line and arrowheads. When null (default), uses the base Color property." },
                { "VDimension.TextColor", "Gets or sets the color for the dimension text. When null (default), uses the base Color property." },
                { "VDimension.SuppressDimensionLine", "If true, the dimension line and arrowheads are not drawn. Extension lines and text are still rendered." },

                // VDimension Methods
                { "VDimension.Draw", "Renders the dimension annotation to the canvas." },
                { "VDimension.Clone", "Creates a deep copy of this dimension with all properties duplicated." },
                { "VDimension.Move", "Translates the dimension by the specified displacement vector." },
                { "VDimension.Rotate", "Rotates the dimension around the specified pivot by the given angle in degrees." },
                { "VDimension.Flip", "Mirrors the dimension across the specified axis line." },
                { "VDimension.Scale", "Scales the dimension relative to a center point by the specified factor." },
                { "VDimension.GetBounds", "Returns the axis-aligned bounding box of the dimension." },
                { "VDimension.GetDimensionGeometry", "Returns the seven points the renderer lays the dimension out from, as a named tuple: (dimStart, dimEnd, textPos, ext1Start, ext1End, ext2Start, ext2End). dimStart/dimEnd are the ends of the measuring line, offset perpendicular from Point1/Point2 by Offset; textPos is where the label sits; the ext pairs are the two witness lines, which begin OffsetFromOrigin away from the measured points and run ExtendBeyondDimLines past the measuring line. Use it to draw your own annotation in the same place, or to align other geometry to a dimension. All seven collapse to Point1 when Point1 and Point2 coincide." },
                { "VDimension.ToString", "Returns a string representation of the dimension." },

                // VRadialDimension
                { "VRadialDimension.Center", "Gets or sets the center point of the circle/arc being dimensioned." },
                { "VRadialDimension.Radius", "Gets or sets the radius of the circle/arc being dimensioned." },
                { "VRadialDimension.LeaderAngle", "Gets or sets the angle (in degrees) at which the leader line points to the circumference." },
                { "VRadialDimension.ShowDiameter", "If true, shows diameter (line through center, both arrowheads) instead of radius." },
                { "VRadialDimension.ArrowSize", "Gets or sets the size of the arrowhead." },
                { "VRadialDimension.TextHeight", "Gets or sets the height of the dimension text." },
                { "VRadialDimension.DecimalPlaces", "Gets or sets the number of decimal places for the displayed value." },
                { "VRadialDimension.Prefix", "Gets or sets the text prefix prepended to the dimension value." },
                { "VRadialDimension.Suffix", "Gets or sets the text suffix appended to the dimension value." },
                { "VRadialDimension.CustomText", "Gets or sets custom text. If null, shows the calculated value with R/\u2300 symbol." },
                { "VRadialDimension.Value", "Gets the calculated radius or diameter value (read-only)." },
                { "VRadialDimension.DisplayText", "Gets the display text including symbol and Prefix/Suffix (read-only)." },
                { "VRadialDimension.TextBackgroundOpaque", "If true, an opaque background is drawn behind the dimension text." },
                { "VRadialDimension.DimensionLineColor", "Gets or sets the color for the leader line and arrowhead. When null, uses base Color." },
                { "VRadialDimension.TextColor", "Gets or sets the color for the dimension text. When null, uses base Color." },
                { "VRadialDimension.GetDimensionGeometry", "Returns the leader line start/end points and text position for rendering." },
                { "VRadialDimension.Clone", "Creates a deep copy of this radial dimension with all properties duplicated." },
                { "VRadialDimension.Move", "Translates the radial dimension by the specified displacement vector." },
                { "VRadialDimension.Rotate", "Rotates the radial dimension around the specified pivot by the given angle in degrees." },
                { "VRadialDimension.Scale", "Scales the radial dimension relative to a center point by the specified factor." },

                // Shape base class properties
                { "Shape.Id", "Gets the unique identifier for this shape, automatically assigned on creation." },
                { "Shape.Color", "Gets or sets the outline/stroke color as a string (named color or hex code like '#FF0000' or '#80FF0000')." },
                { "Shape.FillColor", "Gets or sets the fill color as a string. Use 'Transparent' for no fill." },
                { "Shape.LineWeight", "Gets or sets the thickness of the outline stroke in pixels." },
                { "Shape.LineType", "Gets or sets the stroke style (line pattern). Options: Continuous (solid), Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden." },
                { "Shape.LineTypeScale", "Gets or sets the scale factor for stroke patterns (default 1.0). Values > 1.0 create longer dashes/gaps, < 1.0 create shorter ones." },
                { "Shape.DrawFactor", "Gets or sets the draw factor (0.0 to 1.0) for progressive drawing animations." },
                { "Shape.OffsetX", "Gets or sets the X offset for translation animations." },
                { "Shape.OffsetY", "Gets or sets the Y offset for translation animations." },
                { "Shape.RotationAngle", "Gets or sets the rotation angle in degrees, counter-clockwise, about RotationPivot; written by RotateAnimation. The renderer applies it uniformly to every shape type, so any shape rotates. Declared virtual so a shape that rotates by rebuilding its own geometry can hook the setter — VRectangle overrides it and rebuilds its four corners, which is why it is the one shape excluded from the render transform (applying both would turn it twice). Rotation is otherwise a RENDER-TIME transform: Contains, DistanceTo and click-to-select operate on the unrotated geometry, so point queries against a rotated shape answer for its pre-rotation position. VRectangle, having baked the rotation into its corners, is again the exception." },
                { "Shape.RotationPivot", "Gets or sets the pivot point for rotation animations. Null uses shape center." },
                { "Shape.IsVisible", "Gets or sets whether this shape is visible on the canvas. Hidden shapes are not rendered but remain in the shape collection." },

                // Shape base class methods
                { "Shape.Place", "Puts the shape on the canvas and keeps it there: registers it with Shape.DefaultRegistry and sets IsExplicitlyDrawn = true, which exempts it from the pass that hides unnamed shapes after Main() returns. Idempotent — calling it twice, or on a shape that is already placed, is harmless — and Remove() is the inverse. A shape you construct yourself needs no Place() call, because construction already registered it. Reach for it when the shape did not come from a plain `new`: results of boolean ops, ArrayOps and Chart (registered but unnamed, so otherwise swept away — setting Name does the same job); the query results that deliberately do not draw their answer (GeometryHelper.IntersectLineLine and friends, VRay.ToFiniteLine, VRay.ToXLine, VXLine.ToFiniteLine); and anything built while Shape.AutoRegister was false." },
                { "Shape.Draw", "The historical name for Place(), and exactly equivalent to it — a one-line forward, pinned by a test so the two cannot drift apart. It appears throughout older projects and samples, and the canvas drawing tools still emit it in the code they generate, so it is in no way discouraged; there is nothing to migrate. New code reads better with Place(), which says what actually happens: shapes render because they are registered, not because something was 'drawn'." },
                { "Shape.CopyStyleTo", "Copies this shape's styling onto another shape and returns that target, so the call chains. Copies exactly five members — Color, FillColor, LineWeight, LineType, LineTypeScale — and touches nothing else: geometry, Name, Id, IsVisible and placement are all left alone. It is a no-op (returning the argument unchanged) when the target is null or is this same shape, which is what makes it comfortable to use on a boolean-op result that may legitimately be null. The motivating case is restyling a computed shape to match the input it came from: a.CopyStyleTo(a.Union(b))." },
                { "Shape.Remove", "Unregisters the shape from the canvas — the inverse of Place(). Unlike Hide(), the shape is gone from the collection, not merely unrendered." },
                { "Shape.Name", "Optional label for the shape, default an empty string. Also load-bearing for visibility: after Main() returns, shapes whose Name is empty and which were never explicitly drawn are hidden as construction leftovers." },
                { "Shape.Opacity", "Transparency multiplier from 0 (invisible) to 1 (opaque). Default 1.0. Applied on top of any alpha already present in Color or FillColor." },
                { "Shape.AutoRegister", "Static switch. When false, newly constructed shapes are not added to the canvas. Use it to build throwaway geometry cheaply, and always restore it in a finally block." },
                { "Shape.DefaultRegistry", "Static. The IShapeRegistry that receives every shape on construction — the mechanism behind auto-registration. The host application sets it; user code rarely touches it." },
                { "Shape.ResetDefaults", "Static. Restores DefaultColor, DefaultFillColor, DefaultLineWeight, DefaultLineType and DefaultLineTypeScale to their built-in values (Cyan, Transparent, 2.0, Continuous, 1.0)." },
                { "Shape.GetControlPoints", "Returns the interactive editing handles for this shape. The base implementation returns a single Move handle at the bounding-box centre; most shapes override it with vertex, radius or control handles." },
                { "Shape.MoveControlPoint", "Moves the handle at the given index to a new position. The base implementation treats index 0 as \"move the whole shape\"." },
                { "Shape.DoesIntersect", "Returns true when this shape overlaps another. The base implementation reports whether Intersect() produced a result, and defers to VText's specialised test when the other shape is text." },
                { "Shape.Clone", "Creates a deep copy of the shape with all properties duplicated. Returns the same type as the original (covariant return type), so no casting is needed." },
                { "Shape.Move", "Translates the shape by the specified displacement vector." },
                { "Shape.Rotate", "Rotates the shape around the specified pivot point by the given angle in degrees." },
                { "Shape.Flip", "Mirrors the shape across the specified line (axis of reflection)." },
                { "Shape.Scale", "Scales the shape relative to a center point by the specified factor." },
                { "Shape.GetBounds", "Returns the axis-aligned BoundingBox of the shape (Min, Max, Width, Height, Center, Area). It also deconstructs to a (min, max) tuple. VRay and VXLine are infinite, so their bounds are non-finite." },
                { "Shape.Contains", "Returns true if the specified point is inside or on the shape. The base implementation is a bounding-box test, but every shape with a real outline overrides it: the open curves (VLine, VPolyline, VArc, VBezier, VSpline, VXLine, VRay) answer 'lies on the stroke' — VRay is false behind its Origin — and the area types (VCircle, VEllipse, VRectangle, VPolygon, VGroup, VHatch, Region) do a genuine interior test. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension keep the bounding-box answer, because for those the box is the shape or there is no outline to test." },
                { "Shape.DistanceTo", "Returns the distance from the shape to the specified point. The base implementation measures from the bounding-box centre, but every shape with a real outline overrides it with the true shortest distance: exact for VLine, VArc, VPolyline, VPolygon (so also VRectangle), VCircle, VXLine and VRay; sampled for VEllipse, VBezier and VSpline; to the boundary for VHatch and Region; the nearest child for VGroup. For an area type this is the distance to the OUTLINE — zero on it and positive on both sides, not a signed depth — so pair it with Contains for the side. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension use the base behaviour." },
                { "Shape.Intersect", "Computes geometric intersection with another shape." },
                { "Shape.ToString", "Returns a string representation of the shape." },
                { "Shape.Show", "Shows this shape on the canvas by setting IsVisible to true." },
                { "Shape.Hide", "Hides this shape from the canvas by setting IsVisible to false. The shape remains in the collection but is not rendered." },
                { "Shape.BringAbove", "Moves this shape above the specified shape in the draw order, so it renders on top." },
                { "Shape.SendBehind", "Moves this shape behind the specified shape in the draw order, so it renders underneath." },

                // Shape state flags and static style defaults
                { "Shape.IsPlaced", "True once the shape has been accepted by the registry. Set by Place() (and by construction, since shapes auto-register) and cleared by Remove(). It is what makes Place() idempotent: registering an already-placed shape is a no-op rather than a duplicate entry. Reading it tells you whether the shape is currently on the canvas; do not set it by hand, because writing true without registering leaves the canvas out of step with the flag." },
                { "Shape.IsExplicitlyDrawn", "True when Place() (or its alias Draw()) has been called on this shape. It is the flag the post-Main() sweep consults: a shape with an empty Name and IsExplicitlyDrawn false is treated as construction leftover and hidden. Setting Name achieves the same exemption, so a shape needs one or the other to survive. Default false." },
                { "Shape.IsSelected", "True while the shape is part of the canvas selection. Written by the selection tool and by Ctrl+A; the renderer draws selection handles for shapes where it is true. Setting it from code marks the shape as selected but does not scroll to it or update the Properties panel. Default false." },
                { "Shape.FlipProgress", "How far through a mirror the shape is drawn, 0 (unflipped) to 1 (fully mirrored across FlipAxis). Written by FlipAnimation each frame. Values outside [0, 1] are not clamped. Default 0, which is why FlipAxis alone changes nothing." },
                { "Shape.FlipAxis", "The VLine that FlipProgress mirrors across, or null for no flip. Only its geometry is read — the line is not drawn as part of the flip, and can be hidden or left off the canvas entirely. Set together with FlipProgress; FlipAnimation writes both. Default null." },
                { "Shape.DefaultColor", "Static. The stroke colour every new shape starts with unless its own type overrides it (VArc is Orange, VCircle Yellow, VPolygon LightBlue, VRectangle Magenta, VPoint White) or ShapeDefaults.GlobalColor is set. Default \"Cyan\". Changing it affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.DefaultFillColor", "Static. The fill colour every new shape starts with. Default \"Transparent\", which is why shapes are outlines until you set FillColor. Affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.DefaultLineWeight", "Static. The stroke thickness every new shape starts with. Default 2.0. Whether that number means device pixels or world units depends on the Line Weight rendering mode in Settings; ResetDefaults() restores it." },
                { "Shape.DefaultLineType", "Static. The stroke pattern every new shape starts with. Default LineType.Continuous. Affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.DefaultLineTypeScale", "Static. The dash-pattern scale every new shape starts with. Default 1.0 — larger stretches the dashes, smaller compresses them, and a non-positive value renders solid. Affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.ResetIdCounter", "Static. Sets the shape Id counter back to 0, so the next shape constructed gets Id 1. DoodleSharp calls it at the start of every run, which is why IDs are stable between runs and you can rely on them in the Outliner or Ctrl+G. Call it yourself only if you are hosting the library and want the same guarantee." },

                // VXYZ Properties
                { "VXYZ.X", "Gets the X component. VXYZ is immutable — every operation returns a new instance, so a VXYZ can be shared without aliasing surprises." },
                { "VXYZ.Y", "Gets the Y component. Read-only; see X." },
                { "VXYZ.Z", "Gets the Z component. Read-only; 0 for the two-argument constructor, and ignored by all 2D rendering." },
                { "VXYZ.Item", "Gets the component at the specified index (0 = X, 1 = Y, 2 = Z). Throws IndexOutOfRangeException for anything else." },
                { "VXYZ.Clone", "Returns a new VXYZ with the same components. Virtual so subclasses can narrow the return type; rarely needed given immutability." },
                { "VXYZ.IsWithinLengthLimits", "Static sanity check: true when every component of the given point is under 1e5 in absolute value. Useful for rejecting runaway coordinates before they reach the canvas." },
                { "VXYZ.Zero", "Static. The origin, (0, 0, 0) — also what Normalize() returns for a zero-length vector. Because VXYZ is immutable, this single instance is safe to share and compare against; note == is the fuzzy IsAlmostEqualTo comparison, so a vector within 1e-9 of the origin equals VXYZ.Zero." },
                { "VXYZ.BasisX", "Static. The unit vector (1, 0, 0) — canvas right. Use it for axis-aligned directions and as a rotation axis: VCoordinateSystem.Rotate(VXYZ.BasisZ, 90), VRay.HorizontalRight is new VRay(origin, VXYZ.BasisX), and VXYZ.BasisX * -1 points left." },
                { "VXYZ.BasisY", "Static. The unit vector (0, 1, 0) — canvas UP, since the coordinate system is Y-up with the origin at the canvas centre. VXYZ.BasisY * -1 points down." },
                { "VXYZ.BasisZ", "Static. The unit vector (0, 0, 1) — out of the screen. Nothing renders along it, but it is the axis for planar rotation: VCoordinateSystem.Rotate(VXYZ.BasisZ, 90) and VTransform.CreateRotationDegrees(VXYZ.BasisZ, 90) both turn the XY plane a quarter turn counter-clockwise." },

                // VXYZ Methods
                { "VXYZ.Add", "Returns a new vector that is the sum of this vector and another." },
                { "VXYZ.Subtract", "Returns a new vector that is the difference of this vector and another." },
                { "VXYZ.Multiply", "Returns a new vector with each component multiplied by the scalar value." },
                { "VXYZ.Divide", "Returns a new vector with each component divided by the scalar value." },
                { "VXYZ.Negate", "Returns a new vector with all components negated (reversed direction)." },
                { "VXYZ.AsVPoint", "Converts this VXYZ to a VPoint (ignores Z component)." },
                { "VXYZ.GetLength", "Returns the magnitude (length) of the vector." },
                { "VXYZ.Normalize", "Returns a unit vector in the same direction (length = 1). A zero-length vector returns VXYZ.Zero rather than throwing or producing NaN." },
                { "VXYZ.DistanceTo", "Returns the Euclidean distance from this point/vector to another." },
                { "VXYZ.DotProduct", "Returns the dot product (scalar product) of this vector with another vector." },
                { "VXYZ.CrossProduct", "Returns the cross product of this vector with another vector (3D only)." },
                { "VXYZ.TripleProduct", "Returns the scalar triple product of three vectors: this · (a × b)." },
                { "VXYZ.AngleTo", "Returns the unsigned angle in radians between this vector and another, in the range 0 to π. Returns 0 when either vector has zero length. Use Math.Atan2 if you need a signed angle." },
                { "VXYZ.IsZeroLength", "Returns true if the vector has zero length (all components are zero)." },
                { "VXYZ.IsUnitLength", "Returns true if the vector has unit length (magnitude ≈ 1)." },
                { "VXYZ.IsAlmostEqualTo", "Returns true if this vector is approximately equal to another within the given tolerance (default GeometryTolerance.Epsilon, 1e-9)." },
                { "VXYZ.Equals", "Returns true if the other object is a VXYZ that is almost equal to this one. The == and != operators use the same fuzzy comparison, so exact bit equality is never required." },
                { "VXYZ.GetHashCode", "Returns a hash code built from the components rounded to 8 decimal places, so that values considered equal by IsAlmostEqualTo usually hash together in dictionaries and sets." },
                { "VXYZ.ToString", "Returns a string representation: \"(X, Y, Z)\"." },
                { "VXYZ.Rotate", "Returns a new VXYZ rotated around the Z-axis by the specified angle in degrees." },

                // ICurve interface
                { "ICurve.StartPoint", "Gets the starting point of the curve." },
                { "ICurve.EndPoint", "Gets the ending point of the curve." },
                { "ICurve.Vertices", "Gets the key vertices/control points of the curve. For lines: start and end. For circles/ellipses: center. For arcs: center, start, end. For polygons/polylines: all vertices. For beziers/splines: all control points." },
                { "ICurve.SelfIntersecting", "Gets whether this curve intersects itself. Simple curves (lines, circles) always return false." },
                { "ICurve.GetLength", "Returns the total arc length of the curve." },
                { "ICurve.Divide", "Divides the curve into the specified number of equal segments, returning the division points." },
                { "ICurve.Measure", "Returns points along the curve at fixed distance intervals." },
                { "ICurve.Project", "Projects a point onto the curve, returning the closest point on the curve." },
                { "ICurve.PointAtSegmentLength", "Returns the point at the specified distance along the curve from the start." },
                { "ICurve.PointAtParameter", "Returns a point on the curve at the given normalized parameter (0 to 1), where 0 is the start and 1 is the end." },
                { "ICurve.ParameterAtPoint", "Returns the normalized parameter (0 to 1) for the closest point on the curve to the given point." },
                { "ICurve.Offset", "Creates a new curve offset by the specified distance (positive = left, negative = right)." },
                { "ICurve.SplitAtPoint", "Splits the curve at the specified point, returning two curve segments." },
                { "ICurve.SetBounds", "Trims the curve in place so that the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. Parameters are clamped to [0,1] and swapped if reversed. Implemented for VLine/VArc/VEllipse/VPolyline/VBezier/VSpline. Throws NotSupportedException on VCircle/VPolygon/VRay/VXLine because their trimmed result is a different shape type — use SplitAtPoint there." },
                { "ICurve.NormalAtPoint", "Returns the normal vector (perpendicular) to the curve at the specified point." },
                { "ICurve.PointsAtChordLengthFromPoint", "Returns the points on this curve that are exactly chordLength away from the given point in a straight line — the intersections of a circle of that radius with the curve. The reference point does not have to lie on the curve; it is projected onto it first. The list is empty when the circle never reaches the curve, and typically holds one point on each side when it does. Use it to step along a curve by true chord distance (setting out a fence line, spacing bolts on an arc); use Measure(segmentLength) instead when you want arc-length spacing." },
                { "ICurve.Place", "Puts the curve on the canvas and keeps it there. Declared on IDrawable, which ICurve extends, so the recommended name is reachable through an ICurve reference and not only through Shape. Exactly equivalent to Draw()." },
                { "ICurve.Draw", "The historical name for Place(), and exactly equivalent to it. Declared on IDrawable, which ICurve extends." },
                { "ICurve.Intersect", "Computes intersection with another curve, returning an IntersectionResult with points and overlapping segments." },

                // Frame
                { "Frame.Request", "Queues a callback for the next frame and returns a handle. The Action<double> overload receives elapsed seconds since the loop started; the Action overload is for callbacks that do not need it. Call it again from inside the callback to keep the loop running - that request lands on the next frame, not the current one, so the function does not re-enter itself. Requesting the same method twice queues it twice, as in JavaScript." },
                { "Frame.Cancel", "Removes a callback queued by Request, using the handle it returned. Unknown or already-run handles are ignored, so cancelling twice is safe." },
                { "Frame.Clear", "Drops every queued callback. Called automatically before each run, so a script never inherits the previous run's loops." },
                { "Frame.HasPending", "True while at least one callback is queued." },

                // Animator
                { "Animator.Duration", "Gets the total duration of all animations in seconds — the end of the last animation added, gaps from Pause() included. Read-only; it extends automatically as you add animations." },
                { "Animator.Repeat", "Gets or sets whether playback loops. Default false. When true each animation loops independently on its own duration, so a 1-second and a 3-second animation drift apart rather than restarting together." },
                { "Animator.Speed", "Gets or sets the playback speed multiplier (1.0 = normal speed, 2.0 = twice as fast). Not clamped. The toolbar speed slider writes this same value, so moving it overrides what you set in code." },
                { "Animator.Fps", "Gets or sets the target frame rate in frames per second (1-120). Default is 60. Lower values reduce rendering frequency for slower visual updates." },
                { "Animator.AddToAnimations", "Adds animation(s) to play. A single animation is queued to start after everything added so far has finished; a List<Animation> is queued to start together, and the next sequential item waits for the longest of them. Adding an animation also places its target shape on the canvas if it is not already there." },
                { "Animator.Pause", "Adds a pause (in seconds) before the next animation. Example: anim.Pause(5) inserts a 5-second gap. It does not affect anything already added." },
                { "Animator.Animate", "Starts playback and makes this animator's timeline the active one, replacing any previously playing timeline. Only one Animator plays at a time." },
                { "Animator.Stop", "Stops playback and clears the active timeline. Shapes keep whatever state the animations last wrote." },

                // IntersectionResult
                { "IntersectionResult.Points", "Gets the list of intersection points." },
                { "IntersectionResult.Curves", "Gets the list of overlapping curve segments (for collinear/coincident curves)." },
                { "IntersectionResult.HasIntersection", "Returns true if there is at least one intersection point or overlapping segment." },
                { "IntersectionResult.IsSinglePoint", "Returns true if there is exactly one intersection point." },
                { "IntersectionResult.HasOverlap", "Returns true if the curves share an overlapping segment." },
                { "IntersectionResult.Count", "Gets the total number of intersection elements (points + curves)." },

                // Animation base class
                { "Animation.Target", "Gets the shape that this animation affects. Null for ObjectPropertyAnimation, which targets an arbitrary object instead." },
                { "Animation.StartTime", "Gets the time in seconds when the animation begins. Assigned by the Animator as you add animations — you never set it yourself." },
                { "Animation.Duration", "Gets the animation duration in seconds (set in constructor)." },
                { "Animation.EasingFunction", "Gets or sets the easing function: a Func<double, double> mapping normalized time t to eased t. Defaults to EasingFunctions.Linear. It only reshapes the curve between the same start and end states — it never changes where the animation begins or ends. A custom lambda works as well as the built-ins." },
                { "Animation.Name", "Optional label for this animation, shown on its track in the timeline panel. Falls back to the type name (Draw, Move, Rotate, ...) when left empty." },
                { "Animation.Apply", "Applies the animation at the specified normalized time (0 to 1). Called by the timeline every frame — user code does not call this." },

                // DrawAnimation
                { "DrawAnimation.Target", "Gets the shape to animate drawing. Its DrawFactor is set to 0 at construction so it stays invisible until this animation's turn; a VGroup target is set recursively, children included." },
                { "DrawAnimation.Duration", "Gets how long the drawing takes (in seconds)." },
                { "DrawAnimation.EasingFunction", "Gets or sets the easing function for the draw effect." },
                { "DrawAnimation.Apply", "Applies the draw animation, setting DrawFactor (and every child's, for a VGroup) to the eased progress." },

                // MoveAnimation
                { "MoveAnimation.Target", "Gets the shape to move. Its OffsetX/OffsetY are written; the displacement is relative to wherever it sits when this animation starts, so chained moves accumulate." },
                { "MoveAnimation.Duration", "Gets how long the movement takes (in seconds)." },
                { "MoveAnimation.EasingFunction", "Gets or sets the easing function for smooth movement." },
                { "MoveAnimation.Apply", "Applies the move, offsetting the target by the eased fraction of the displacement vector. The starting offset is captured the first time this runs, not at construction." },

                // PathAnimation
                { "PathAnimation.Target", "Gets the shape to move along the path. The centre of its bounding box is placed on the curve each frame." },
                { "PathAnimation.Duration", "Gets how long the path animation takes (in seconds)." },
                { "PathAnimation.EasingFunction", "Gets or sets the easing function for the path animation." },
                { "PathAnimation.Apply", "Applies the path animation, positioning the target at path.PointAtParameter(eased t). The path is used purely as maths, so hiding the curve does not affect the motion." },

                // RotateAnimation
                { "RotateAnimation.Target", "Gets the shape to rotate. Its RotationAngle and RotationPivot are written." },
                { "RotateAnimation.Duration", "Gets how long the rotation takes (in seconds)." },
                { "RotateAnimation.EasingFunction", "Gets or sets the easing function for smooth rotation." },
                { "RotateAnimation.Apply", "Applies the rotation, adding the eased fraction of the angle (degrees, counter-clockwise) to the target's rotation at the moment this animation started, and setting RotationPivot. Called by the timeline, not by you." },

                // FlipAnimation
                { "FlipAnimation.Target", "Gets the shape to flip. Its FlipProgress and FlipAxis are written; progress always ends at a complete mirror (1.0)." },
                { "FlipAnimation.Duration", "Gets how long the flip takes (in seconds)." },
                { "FlipAnimation.EasingFunction", "Gets or sets the easing function for the flip effect." },
                { "FlipAnimation.Apply", "Applies the flip animation, progressively mirroring the shape." },

                // TransformAnimation
                { "TransformAnimation.Target", "Gets the source shape being morphed from. For the VText character overload this is the lifted glyph outline, not the text itself." },
                { "TransformAnimation.Duration", "Gets how long the morph takes (in seconds)." },
                { "TransformAnimation.EasingFunction", "Gets or sets the easing function controlling the morph progress." },
                { "TransformAnimation.Apply", "Applies the transform, interpolating the morphing outline between the two shapes and revealing the destination on completion." },

                // FadeInAnimation
                { "FadeInAnimation.Target", "Gets the shape to fade in. Its Opacity is set to 0 at construction, recursing into VGroup children." },
                { "FadeInAnimation.Duration", "Gets how long the fade-in takes (in seconds)." },
                { "FadeInAnimation.EasingFunction", "Gets or sets the easing function for smooth fade-in." },
                { "FadeInAnimation.Apply", "Applies the fade-in animation, raising Opacity from 0 to 1 (and every child's, for a VGroup)." },

                // FadeOutAnimation
                { "FadeOutAnimation.Target", "Gets the shape to fade out. Its Opacity is set to 1 at construction, recursing into VGroup children." },
                { "FadeOutAnimation.Duration", "Gets how long the fade-out takes (in seconds)." },
                { "FadeOutAnimation.EasingFunction", "Gets or sets the easing function for smooth fade-out." },
                { "FadeOutAnimation.Apply", "Applies the fade-out animation, lowering opacity from 1 to the constructor's targetOpacity (0 by default)." },

                // ValueAnimation
                { "ValueAnimation.Target", "Gets the shape whose property is being animated (T must be a Shape)." },
                { "ValueAnimation.Duration", "Gets how long the value animation takes (in seconds)." },
                { "ValueAnimation.EasingFunction", "Gets or sets the easing function for smooth value interpolation." },
                { "ValueAnimation.Apply", "Applies the value animation, interpolating the property between start and end values (or through the sequence of values, each leg taking an equal share of the duration)." },

                // ObjectPropertyAnimation
                { "ObjectPropertyAnimation.Target", "Always null — this animation drives a property on an arbitrary object rather than a shape, so nothing is auto-drawn for it. The object's property setter is what moves the geometry." },
                { "ObjectPropertyAnimation.Duration", "Gets how long the object property animation takes (in seconds)." },
                { "ObjectPropertyAnimation.EasingFunction", "Gets or sets the easing function for smooth value interpolation." },
                { "ObjectPropertyAnimation.Apply", "Applies the object property animation, interpolating the property between start and end values." },

                // VizConsole
                { "VizConsole.Log", "Prints a value to the console panel below the canvas. Signature: Log(object? value, bool itemize = true) — the file path and line number parameters are filled in by the compiler, so you never pass them. Null prints an empty line; anything else prints its ToString(). With itemize true (the default) a collection (any IEnumerable except string) is printed one item per line, and an empty collection prints \"(empty)\"; with itemize false the collection's own ToString() is printed. Output is prefixed [ModuleName:LineNumber], where ModuleName is the calling file without its extension." },

                // EasingFunctions
                { "EasingFunctions.Linear", "Returns linear easing (constant speed, no acceleration)." },
                { "EasingFunctions.EaseInQuad", "Returns quadratic ease-in (slow start, accelerating)." },
                { "EasingFunctions.EaseOutQuad", "Returns quadratic ease-out (fast start, decelerating)." },
                { "EasingFunctions.EaseInOutQuad", "Returns quadratic ease-in-out (slow start and end)." },
                { "EasingFunctions.EaseInCubic", "Returns cubic ease-in (slower start than quadratic)." },
                { "EasingFunctions.EaseOutCubic", "Returns cubic ease-out (slower end than quadratic)." },
                { "EasingFunctions.EaseInOutCubic", "Returns cubic ease-in-out (smoother start and end)." },

                // ArrayOps
                { "ArrayOps.LinearArray", "Creates copies of a shape along a direction vector." },
                { "ArrayOps.RectangularArray", "Creates a grid pattern of shape copies (rows × columns)." },
                { "ArrayOps.CircularArray", "Creates copies arranged in a circle around a center point." },
                { "ArrayOps.PathArray", "Creates copies distributed along a curve path." },
                { "ArrayOps.SpiralArray", "Creates copies arranged in a spiral pattern." },
                { "ArrayOps.Mirror", "Creates a mirrored copy of a shape across an axis line." },

                // BooleanOps
                { "BooleanOps.Union", "Combines two or more polygons into one. Returns a single VPolygon if successful, or null when it cannot form one — and then it reports why through GeometryDiagnostics (the console, tagged 'Geometry'): no polygons passed, an empty result, or N disjoint regions because the inputs never overlapped or touched. When you want every piece instead of a null, call BooleanOps.UnionAll, which returns List<VPolygon> and never returns null; or BooleanOps.UnionWithHoles(a, b) when the merged outline can enclose voids you care about." },
                { "BooleanOps.UnionAll", "Unions any number of polygons and returns EVERY resulting piece as a List<VPolygon> — never null, which is the difference from Union. Overlapping inputs merge into one piece; inputs that touch nothing come back as separate pieces; an empty input gives an empty list and a single input gives a copy of it. Overloads take params VPolygon[] or IEnumerable<VPolygon>. This is what the console diagnostic points you at when Union returns null. HOLES ARE NOT REPRESENTED in the result: if the merged outline can enclose a void that matters to you, use UnionWithHoles(a, b), which returns List<PolygonWithHoles> — though that form takes exactly two polygons. Results are unnamed method results, so Place() or name anything you want to keep." },
                { "BooleanOps.Intersect", "Returns the overlapping area of two polygons (logical AND)." },
                { "BooleanOps.Difference", "Subtracts one polygon from another." },
                { "BooleanOps.Xor", "Returns the symmetric difference of two polygons (non-overlapping areas)." },
                { "BooleanOps.OffsetPolygon", "Grows or shrinks a polygon by the specified distance." },
                { "BooleanOps.Area", "Calculates the area of a polygon." },
                { "BooleanOps.PointInPolygon", "Tests if a point is inside a polygon." },

                // ControlPoint
                { "ControlPoint.Type", "Gets the control point type (Move, Vertex, Radius, Rotation, CurveControl). Read-only, set at construction." },
                { "ControlPoint.X", "Gets or sets the X coordinate of the handle in world units." },
                { "ControlPoint.Y", "Gets or sets the Y coordinate of the handle in world units." },
                { "ControlPoint.Label", "Gets the display label shown for this handle (for example \"Center\", \"Radius\", \"Start\"). Read-only, defaults to an empty string." },
                { "ControlPoint.ToVXYZ", "Returns the handle's position as a VXYZ with Z = 0." },

                // CurveIntersection
                { "CurveIntersection.Intersect", "Computes the intersection of two curves, dispatching on the pair of runtime types: line/line, line/circle, line/arc, line/ellipse, circle/circle, circle/arc and arc/arc use exact closed-form math (in either argument order); every other combination falls through to IntersectGeneric, which samples both curves into segments. Returns an IntersectionResult holding Points and, for collinear overlapping lines, Curves." },
                { "CurveIntersection.IsSelfIntersecting", "Returns true when a curve crosses itself. VLine, VCircle, VArc, VEllipse and VRectangle are always false by construction; VPolyline, VPolygon, VBezier and VSpline are actually tested. Any other curve type returns false." },
                { "CurveIntersection.IntersectLineLine", "Exact intersection of two line segments. Returns a single point when they cross within both segments, or — when they are collinear and overlap — an IntersectionResult whose Curves holds the shared segment (HasOverlap is true). Parallel non-collinear lines give an empty result." },
                { "CurveIntersection.IntersectLineCircle", "Exact intersection of a line segment and a circle: 0, 1 (tangent) or 2 points, limited to the extent of the segment." },
                { "CurveIntersection.IntersectLineArc", "Exact intersection of a line segment and an arc. Circle roots outside the arc's start/end angle sweep are discarded." },
                { "CurveIntersection.IntersectLineEllipse", "Exact intersection of a line segment and an axis-aligned ellipse: 0, 1 (tangent) or 2 points. The ellipse is treated as a full ellipse — a partial VEllipse's StartAngle/EndAngle sweep is not applied here, so filter the points yourself if you need only the drawn part." },
                { "CurveIntersection.IntersectCircleCircle", "Exact intersection of two circles: 0, 1 (tangent) or 2 points. Two coincident circles (same centre and radius) return the circle itself in Curves, so HasOverlap is true and Points is empty." },
                { "CurveIntersection.IntersectCircleArc", "Exact circle/circle intersection filtered to the arc's angular sweep." },
                { "CurveIntersection.IntersectArcArc", "Exact circle/circle intersection filtered to both arcs' angular sweeps." },
                { "CurveIntersection.IntersectGeneric", "Fallback intersection by segment decomposition: both curves are sampled with GetSegments, every segment pair is tested, and duplicate points are merged. Works for any ICurve pair, at sampling accuracy." },
                { "CurveIntersection.GetSegments", "Samples a curve into line segments — VLine returns itself, VPolygon/VPolyline return their edges, and other curves are divided into length × segmentsPerUnit pieces (minimum 2, capped at 1000). The synthesised segments are built through an internal non-registering factory, so they never appear on the canvas — but they are ordinary VLine objects to you, and moving or styling one has no effect on the source curve." },
                { "CurveIntersection.IsPolylineSelfIntersecting", "Tests a raw List<VXYZ> vertex chain for self-intersection without allocating any shapes. Adjacent segments are exempt, as is the closing pair when the first and last vertices coincide. Fewer than 4 points is always false." },

                // IntersectionResult factories and helpers
                { "IntersectionResult.None", "An empty result — no points and no overlapping curves." },
                { "IntersectionResult.FromPoint", "Builds a result holding a single intersection point." },
                { "IntersectionResult.FromPoints", "Builds a result holding several intersection points." },
                { "IntersectionResult.FromCurve", "Builds a result holding one overlapping curve (used when two curves share a segment rather than crossing)." },
                { "IntersectionResult.FromCurves", "Builds a result holding several overlapping curves." },
                { "IntersectionResult.Merge", "Appends another result's points and curves to this one. A null argument is ignored." },
                { "IntersectionResult.RemoveDuplicatePoints", "Collapses points that lie within tolerance (default 1e-6) of an already-kept point. Applied automatically by the segment-sampled generic path, where one true crossing can be found by several segment pairs." },

                // RayCaster
                { "RayCaster.FindIntersection", "Casts a ray and returns the closest RayHit, or null when nothing is hit. Overloads: (location, direction, exclusionList = null) and (location, direction, maxDistance, exclusionList = null). Queries run in the XY plane — the Z of location/direction is ignored — and direction need not be normalised (a zero-length XY direction returns null). maxDistance also prunes BVH sub-trees, so capping is cheaper, not just filtered. exclusionList skips specific shapes by reference equality, which is how you cast off a source shape or find the next hit past a set." },
                { "RayCaster.HasIntersection", "Returns true as soon as any indexed shape is hit within maxDistance (default infinite). Faster than FindIntersection because traversal stops at the first hit and children are not ordered front-to-back — the right call for shadow-ray / 'is anything blocking?' tests." },
                { "RayCaster.FindIntersections", "Casts a batch of RayQuery values and returns an array of the same length, entry i being the closest hit for query i or null. Parallel by default (the BVH is read-only after construction, so this is thread-safe); pass parallel: false for deterministic single-threaded execution." },
                { "RayCaster.Refit", "Recomputes every node's bounding box from the indexed shapes' current bounds in O(N), keeping the tree topology. Use it after small movements; build a new RayCaster after large structural changes. A shape whose bounds have become non-finite keeps its previous box rather than corrupting the tree." },
                { "RayCaster.Count", "How many shapes are actually indexed — after invisible shapes, VPoint markers and shapes with null or non-finite bounds have been dropped, so it can be smaller than the collection you passed in." },
                { "RayHit.Shape", "The shape that was hit." },
                { "RayHit.Point", "The world-space hit location as a VXYZ (Z is always 0)." },
                { "RayHit.Distance", "Distance from the ray origin to the hit point, in world units." },
                { "RayQuery.Origin", "Ray origin in world coordinates." },
                { "RayQuery.Direction", "Ray direction; need not be normalised, and its Z component is ignored." },

                // Chart
                { "Chart.Bar", "Builds a bar chart from parallel label/value arrays and returns a VGroup of every axis, gridline, tick, label and bar. Bars occupy 70% of their category slot and cycle through ChartOptions.Palette. The Y range auto-fits the data (always including zero) unless YMin/YMax are set. Throws ArgumentException when labels and values differ in length." },
                { "Chart.Line", "Builds a line chart from parallel xs/ys arrays: a VPolyline through the points plus a circular marker at each one, in the first palette colour. Both axes get numeric ticks. Throws ArgumentException when xs and ys differ in length." },
                { "Chart.Scatter", "Builds a scatter plot from an array of VXYZ data points (X and Y are the data values, not canvas coordinates — the chart maps them into the plot area). Points render as translucent dots in the first palette colour." },
                { "Chart.Pie", "Builds a pie chart from a value array, optionally labelled. Slices start at 12 o'clock and run clockwise, sized by each value's share of the total; negative and zero values are skipped, and a non-positive total draws nothing. Sectors are polygon approximations (about one segment per 4°) — there is no VSector shape. No axes are drawn, only the title, slices and labels." },
                { "Chart.Area", "Builds a filled area chart from parallel xs/ys arrays: a translucent VPolygon down to the baseline plus a solid VPolyline along the top edge. Needs at least two points. Throws ArgumentException when xs and ys differ in length." },
                { "ChartOptions.Origin", "Bottom-left corner of the plot area in world coordinates (Y is up, origin at canvas centre). Default (0, 0)." },
                { "ChartOptions.Width", "Width of the plot area in world units. Default 400." },
                { "ChartOptions.Height", "Height of the plot area in world units. Default 250." },
                { "ChartOptions.Title", "Chart title, drawn centred above the plot. Null or empty draws nothing." },
                { "ChartOptions.XAxisTitle", "Label drawn below the X axis. Null or empty draws nothing." },
                { "ChartOptions.YAxisTitle", "Label drawn to the left of the Y axis, rotated 90°. Null or empty draws nothing." },
                { "ChartOptions.XMin", "Pins the low end of the X axis. Null (default) auto-fits from the data and rounds to a nice number." },
                { "ChartOptions.XMax", "Pins the high end of the X axis. Null (default) auto-fits from the data." },
                { "ChartOptions.YMin", "Pins the low end of the Y axis — set 0 to stop a bar chart floating off the baseline. Null (default) auto-fits." },
                { "ChartOptions.YMax", "Pins the high end of the Y axis. Null (default) auto-fits." },
                { "ChartOptions.XTickCount", "Approximate number of X ticks (default 6). The real count comes from the nice-number rounding, so it is a target, not a guarantee; values below 2 are clamped to 2." },
                { "ChartOptions.YTickCount", "Approximate number of Y ticks. Default 6." },
                { "ChartOptions.ShowGrid", "Draws light gridlines behind the data. Default true." },
                { "ChartOptions.ShowLegend", "Draws a legend down the right-hand side of the plot area — one row per entry, each a colour swatch (LabelFontSize square, filled from Palette in the same order the chart uses) followed by the label in TextColor. Rows start at the top of the plot and step down by LabelFontSize × 1.6. The legend is laid out OUTSIDE Width, beginning one LabelFontSize to the right of the plot, so leave room for it rather than expecting the plot to shrink. Honoured by Chart.Bar (one entry per category) and Chart.Pie (one per slice, and only when the optional labels argument is supplied). Chart.Line, Chart.Scatter and Chart.Area draw a single series in Palette[0] and ignore it. Blank labels are skipped. Default false." },
                { "ChartOptions.XLabelRotation", "Rotation of X tick labels in degrees; anything non-zero also right-aligns them. Useful for long category names. Default 0." },
                { "ChartOptions.LabelFontSize", "Text height for tick labels; axis titles are drawn one unit larger. Default 10." },
                { "ChartOptions.TitleFontSize", "Text height for the chart title. Default 14." },
                { "ChartOptions.AxisColor", "Colour of the two axis lines and tick marks. Default \"White\"." },
                { "ChartOptions.GridColor", "Colour of the gridlines. Default \"DimGray\"." },
                { "ChartOptions.TextColor", "Colour of every label and the title. Default \"White\"." },
                { "ChartOptions.Palette", "Colour names cycled across bars, slices and series; series i uses Palette[i % Length]. Line, Area and Scatter use only the first entry. Defaults to a 10-colour qualitative palette." },
                { "ChartOptions.TickDecimalPlaces", "Fixed decimal places for numeric tick labels. Null (default) auto-formats: up to three decimals normally, scientific notation beyond 1e6 or below 1e-3." },

                // HatchGenerator
                { "HatchGenerator.Generate", "Generates the hatch line segments for a boundary, returning them as (VXYZ Start, VXYZ End) tuples clipped to the boundary polygon — no shapes are created or registered. Arguments: boundary (closed polygon points, at least 3), pattern, scale (multiplies pattern spacing, dash lengths and origin), patternAngle (extra rotation in degrees added to every line family's own angle). Returns an empty list for a degenerate boundary or a pattern with no line families, and skips any family that would need more than 10,000 parallel lines. This is what VHatch.GenerateLines() calls." },

                // Parameter (remaining members)
                { "Parameter.Name", "The parameter's display name and lookup key. Case-insensitive at lookup time." },
                { "Parameter.Kind", "The storage family: ParamKind.Number, Boolean, Text or Date." },
                { "Parameter.Min", "Slider lower bound, or null when the declaration did not supply one (EffectiveMin then derives one)." },
                { "Parameter.Max", "Slider upper bound, or null when the declaration did not supply one." },
                { "Parameter.Step", "Slider increment for number parameters, or null for the panel default." },
                { "Parameter.Group", "Optional heading the panel groups this parameter under." },
                { "Parameter.Description", "Optional tooltip text shown in the panel." },
                { "Parameter.AsDouble", "The value as a double, or 0 when it is not a number." },
                { "Parameter.AsBool", "The value as a bool, or false when it is not a boolean." },
                { "Parameter.AsText", "The value as a string — the raw string for text parameters, ToString() otherwise." },
                { "Parameter.AsDate", "The value as a DateTime, or default when it is not a date." },
                { "ParamValue.Name", "The parameter name this value was read from." },
                { "ParamValue.ToString", "The value as a display string — never throws, and returns an empty string for an undeclared parameter." },

                // DoubleExtensions
                { "DoubleExtensions.ToRadians", "Extension method on double: converts an angle in degrees to radians (multiplies by π/180), for handing to System.Math. Usage: Math.Sin(30.0.ToRadians()). Plain arithmetic — nothing is normalised or clamped, so 450.0.ToRadians() converts literally rather than folding to 90 first." },
                { "DoubleExtensions.ToDegrees", "Extension method on double: converts an angle in radians — typically one coming back from System.Math — to the degrees this library uses everywhere. Usage: Math.Atan2(dy, dx).ToDegrees(). Atan2 returns [-π, π], so the result is in [-180, 180]; pass it through GeometryHelper.NormalizeAngle if you need [0, 360)." },

                // GeometryHelper
                { "GeometryHelper.RotatePoint", "Rotates a point about a pivot by an angle in DEGREES, counter-clockwise. Returns a new VXYZ with Z = 0; nothing is drawn." },
                { "GeometryHelper.FlipPoint", "Mirrors a point across the infinite line through the given VLine's Start and End. Returns the point unchanged when the mirror line has zero length." },
                { "GeometryHelper.MovePoint", "Adds a displacement vector to a point. Returns a new VXYZ; the Z component is dropped (always 0)." },
                { "GeometryHelper.ScalePoint", "Moves a point towards or away from a centre by a factor: 1 leaves it alone, 0.5 halves the distance, 2 doubles it, a negative factor puts it on the far side. Z is dropped." },
                { "GeometryHelper.NormalizeAngle", "Folds an angle in degrees into [0, 360). NormalizeAngle(-90) is 270. For radians use GeometryTolerance.NormalizeAngle." },
                { "GeometryHelper.AngleDifference", "The smallest signed turn in degrees from source to target, in [-180, 180]. AngleDifference(10, 350) is 20, not -340 — this is what you want when interpolating a rotation the short way round." },
                { "GeometryHelper.IntersectCircleCircle", "The intersection points of two circles given as centre/radius pairs. Returns a List<VXYZ> of two points normally, one when the circles are exactly tangent, and an empty list when they are separate, nested, or concentric. Pure maths — no shape is created." },
                { "GeometryHelper.GetPolylineNormalAtPoint", "The unit normal of the polyline segment nearest the given point, as (dy, -dx) of that segment. Pass isClosed: true to include the segment from the last vertex back to the first. Returns (0, 1, 0) for a null or single-point list." },
                { "GeometryHelper.IntersectLineLine", "Intersects two line SEGMENTS: returns a VPoint where they cross, a VLine covering the shared stretch when they are collinear and overlap, or null. The Shape? return type is how the answer carries its own shape — but the result is NOT drawn, because asking where two lines meet should not add anything to the canvas. Pattern-match it (if (hit is VPoint p)) and read the coordinates, or call .Place() on it when you do want the marker. If you would rather have plain coordinates than a shape, line1.Intersect(line2) returns an IntersectionResult of VXYZ points." },
                { "GeometryHelper.IntersectLineRect", "Clips a line segment to an axis-aligned rectangle (Liang-Barsky): returns the VLine portion inside, a VPoint when the line only grazes a corner, or null when it misses. Like IntersectLineLine, the returned shape is NOT drawn — call .Place() on it if you want it on the canvas. Rotation on the rectangle is ignored." },
                { "GeometryHelper.IntersectRectRect", "The overlapping area of two axis-aligned rectangles as a new VRectangle, or null when they do not overlap (touching edges count as no overlap). The result is NOT drawn — call .Place() on it if you want it on the canvas. Rotation on either rectangle is ignored." },

                // GeometryTolerance — constants
                { "GeometryTolerance.Epsilon", "Const 1e-9. The library's general-purpose comparison tolerance and the default for every helper on this class, for VXYZ.IsAlmostEqualTo, and for the == / != operators on VXYZ. Also the vertex-stitching tolerance used when Region and VPolygon build their loops, which is why genuinely sub-micron geometry survives a boolean operation." },
                { "GeometryTolerance.VisualEpsilon", "Const 1e-6. A looser tolerance for \"the same as far as the screen is concerned\" — coincidence tests where a difference smaller than this could never be seen or clicked. Pass it explicitly; nothing uses it by default." },
                { "GeometryTolerance.AngleEpsilon", "Const 1e-5, in RADIANS. The default tolerance for AnglesAreEqual, which is deliberately looser than Epsilon because an angle derived from Atan2 of two nearly-equal coordinates carries much more error than the coordinates did." },

                // GeometryTolerance — comparisons
                { "GeometryTolerance.AreEqual", "True when two doubles differ by less than epsilon (default Epsilon, 1e-9). The tolerance is ABSOLUTE, not relative, so it stops being meaningful for values in the millions — pass a larger epsilon there." },
                { "GeometryTolerance.IsZero", "True when |value| is less than epsilon (default 1e-9). Use it instead of `== 0` on anything computed, especially a determinant or a cross product where the exact zero almost never arrives." },
                { "GeometryTolerance.IsLessThan", "True when a is less than b by more than epsilon — so values that are merely equal-within-tolerance return false rather than being decided by floating-point noise." },
                { "GeometryTolerance.IsGreaterThan", "True when a exceeds b by more than epsilon. The mirror of IsLessThan; both return false for values equal within tolerance." },
                { "GeometryTolerance.IsLessOrEqual", "True when a is less than b, or equal to it within epsilon. Together with IsGreaterOrEqual this is the pair to use for interval endpoints you want to include." },
                { "GeometryTolerance.IsGreaterOrEqual", "True when a exceeds b, or equals it within epsilon." },
                { "GeometryTolerance.IsInRange", "True when value lies in [min, max], with epsilon of slack at both ends — so a parameter that computed to 1.0000000001 still counts as inside [0, 1]." },
                { "GeometryTolerance.Sign", "The sign of a value as -1, 0 or +1, with anything within epsilon of zero reported as 0. The tolerant form of Math.Sign, and what you want for orientation and side-of-line tests where an exact zero is the interesting case." },

                // GeometryTolerance — point and vector comparisons
                { "GeometryTolerance.PointsAreEqual", "True when two points coincide within epsilon. Two overloads: raw (x1, y1, x2, y2) and (VXYZ, VXYZ). Both compare X and Y ONLY — Z is ignored, because the canvas is planar." },
                { "GeometryTolerance.VectorsAreEqual", "True when two VXYZ vectors are equal within epsilon. Unlike PointsAreEqual this compares all THREE components, so it is the one to use for directions and axes." },
                { "GeometryTolerance.AnglesAreEqual", "True when two angles in RADIANS are equal within epsilon (default AngleEpsilon, 1e-5). It handles wraparound, so 0 and 2π compare equal. Normalise degrees with NormalizeAngleDegrees and convert before calling — this overload does not take degrees." },

                // GeometryTolerance — angles and clamping
                { "GeometryTolerance.NormalizeAngle", "Folds an angle in RADIANS into [0, 2π). Note the name collision: GeometryHelper.NormalizeAngle does the same job in DEGREES, which is the library's usual unit — this one is the radians counterpart for code already working with Math." },
                { "GeometryTolerance.NormalizeAngleDegrees", "Folds an angle in degrees into [0, 360). NormalizeAngleDegrees(-90) is 270, and 720 is 0. Identical in effect to GeometryHelper.NormalizeAngle." },
                { "GeometryTolerance.ClampParametric", "Clamps a curve parameter into [0, 1]. Every ICurve method that takes a normalised parameter uses this, which is why PointAtParameter(1.5) returns the end point rather than extrapolating." },
                { "GeometryTolerance.Clamp", "Clamps a value into [min, max]. No tolerance is involved — it is Math.Max(min, Math.Min(max, value)) — and it does not check that min is below max, so an inverted range returns min." },

                // GeometryTolerance — distances and orientation
                { "GeometryTolerance.DistanceSquared", "The squared 2D distance between two points, as raw doubles (x1, y1, x2, y2) or as two VXYZ. Z is ignored. Prefer it over Distance whenever you only need to COMPARE distances — it skips the square root, which matters in a nearest-point loop." },
                { "GeometryTolerance.Distance", "The 2D distance between two points, as raw doubles or as two VXYZ. Z is ignored, which is what distinguishes it from VXYZ.DistanceTo (that one is fully 3D)." },
                { "GeometryTolerance.PointOnSegment", "True when a point lies on the segment [lineStart, lineEnd] within epsilon — a perpendicular-distance test AND a parametric bounds check, so a point on the infinite line but beyond an endpoint returns false. A degenerate segment (start equals end) reduces to a coincidence test against that point." },
                { "GeometryTolerance.PointToLineDistance", "The perpendicular distance from a point to the INFINITE line through lineStart and lineEnd — nothing is clamped to the segment, unlike VLine.DistanceTo. Falls back to the distance to lineStart when the two line points coincide." },
                { "GeometryTolerance.Orientation", "Twice the signed area of the triangle p1-p2-p3: positive when the three points turn counter-clockwise, negative for clockwise, zero when collinear. It is a raw cross product, so its MAGNITUDE scales with the size of your geometry — test it with Sign or IsZero rather than comparing it to a fixed number." },
                { "GeometryTolerance.AreCollinear", "True when three points lie on one straight line within epsilon. It is IsZero applied to Orientation, so the same scale caveat applies: for very large coordinates pass a larger epsilon." },

                // IDrawable
                { "IDrawable.Draw", "Registers the object for rendering. Redundant for a normally-constructed shape, which registers itself. The historical name for Place(), and exactly equivalent to it; both are declared on this interface, so either is reachable through an IDrawable or ICurve reference. Prefer Place() in new code." },
                { "IDrawable.Color", "Gets or sets the stroke/outline color as a string: a named color like \"Cyan\", or hex \"#RRGGBB\" / \"#AARRGGBB\"." },
                { "IDrawable.FillColor", "Gets or sets the fill color as a string. \"Transparent\" means no fill." },
                { "IDrawable.LineWeight", "Gets or sets the stroke thickness. Interpreted as world units by default, or as screen pixels when Settings > Line Style Rendering is switched to absolute." },
                { "IDrawable.LineType", "Gets or sets the stroke pattern: Continuous, Dashed, Dotted, DashDot, DashDotDot, Center, Phantom or Hidden." },
                { "IDrawable.LineTypeScale", "Gets or sets the scale factor for stroke patterns. Default is 1.0; values above 1 lengthen dashes and gaps, below 1 shorten them." },

                // BoundingBox
                { "BoundingBox.Min", "The lower-left corner (VXYZ) of the axis-aligned box." },
                { "BoundingBox.Max", "The upper-right corner (VXYZ) of the axis-aligned box." },
                { "BoundingBox.Width", "Max.X minus Min.X." },
                { "BoundingBox.Height", "Max.Y minus Min.Y." },
                { "BoundingBox.Center", "The midpoint of the box, as a VXYZ with Z = 0." },
                { "BoundingBox.Area", "Width multiplied by Height." },
                { "BoundingBox.Contains", "True when the point lies inside or exactly on the box. Z is ignored." },
                { "BoundingBox.Intersects", "True when this box overlaps another, including when they only touch along an edge." },
                { "BoundingBox.Union", "Returns the smallest box that contains both this box and the other." },
                { "BoundingBox.Expand", "Returns a copy grown by the given distance on all four sides. A negative distance contracts, and can invert the box if it exceeds half the width or height." },
                { "BoundingBox.Deconstruct", "Allows tuple deconstruction: var (min, max) = shape.GetBounds();" },

                // VCell
                { "VCell.UniqueId", "The cell's index within its parent VSpatialGrid, assigned in row-major order starting at 0. Read-only." },
                { "VCell.Neighbours", "The adjacent cells with 4-connectivity (left, right, below, above). Populated by the parent VSpatialGrid, so an edge cell has 3 and a corner cell has 2." },
                { "VCell.Center", "The centre point of the square cell (VXYZ). Kept in step by Move, Rotate, Flip and Scale." },
                { "VCell.Blocked", "Marks the cell as an obstacle. FindPath on the parent grid routes around blocked cells. Default false." },
                { "VCell.CellSize", "The edge length of the square cell." },
                { "VCell.Column", "The cell's 0-based column index within the grid. Read-only." },
                { "VCell.Row", "The cell's 0-based row index within the grid, counting up from the bottom. Read-only." },

                // VSpatialGrid
                { "VSpatialGrid.Cells", "All cells in row-major order (left to right, bottom to top)." },
                { "VSpatialGrid.Location", "The centre of the bottom-left cell — not the corner of the grid." },
                { "VSpatialGrid.XCount", "Number of cells across (columns)." },
                { "VSpatialGrid.YCount", "Number of cells up (rows)." },
                { "VSpatialGrid.CellSize", "The edge length of each square cell." },
                { "VSpatialGrid.Count", "Total number of cells (XCount × YCount)." },
                { "VSpatialGrid.Item", "Indexer: grid[index] by flat row-major index, or grid[col, row] by column and row." },
                { "VSpatialGrid.FindPath", "A* shortest path from one cell to another using 4-connectivity, skipping cells whose Blocked is true. Returns the cells from start to end inclusive, or an empty list when no route exists." },
                { "VSpatialGrid.GetClosestCell", "Returns the cell nearest the given point, in O(log n) via an internal KD-tree over cell centres. The point need not be inside the grid. Pass a VXYZ: there is also a VPoint overload, but constructing a VPoint draws a marker on the canvas." },
                { "VSpatialGrid.GetCellAt", "Returns the cell that contains the given point, or null when the point falls outside the grid." },
                { "VSpatialGrid.GetRow", "Returns every cell in the given 0-based row, counting up from the bottom." },
                { "VSpatialGrid.GetColumn", "Returns every cell in the given 0-based column, counting from the left." },
                { "VSpatialGrid.GetCenter", "Returns the centre point of the whole grid." },
                { "VSpatialGrid.ApplyStyle", "Pushes the grid's Color, FillColor and LineWeight onto every cell." },

                // IShapeRegistry
                { "IShapeRegistry.Register", "Called by every Shape constructor when Shape.AutoRegister is true and Shape.DefaultRegistry is set — this is why shapes appear without an explicit call. Also called by Shape.Place() (and its alias Draw())." },
                { "IShapeRegistry.Unregister", "Removes a shape from the canvas. Called by Shape.Remove()." },
                { "IShapeRegistry.MoveAbove", "Reorders a shape so it renders after (on top of) a reference shape. Called by Shape.BringAbove()." },
                { "IShapeRegistry.MoveBehind", "Reorders a shape so it renders before (underneath) a reference shape. Called by Shape.SendBehind()." },

                // IGlyphOutlineProvider
                { "IGlyphOutlineProvider.GetCharContours", "Returns the closed contours of one character of a VText, in world coordinates matching where it is rendered (font, height, anchor and rotation all honoured). One inner list per contour: a simple glyph has one, an 'O' or 'A' has an outer contour plus its holes. Returns null for whitespace or when no outline is available." },

                // ShapeDefaults
                { "ShapeDefaults.GlobalColor", "Gets or sets the default stroke color for new shapes." },
                { "ShapeDefaults.GlobalFillColor", "Gets or sets the default fill color for new shapes." },
                { "ShapeDefaults.GlobalLineWeight", "Gets or sets the default stroke thickness for new shapes." },
                { "ShapeDefaults.GlobalLineType", "Gets or sets the default stroke style for new shapes. Options: Continuous, Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden." },
                { "ShapeDefaults.GlobalLineTypeScale", "Gets or sets the default stroke style scale for new shapes. Controls the scale of dash patterns (default 1.0)." },
                { "ShapeDefaults.Reset", "Sets every property on this class back to null, so each shape type reverts to its own built-in default. It does not touch Shape.DefaultColor and friends — Shape.ResetDefaults() is the call for those." },

                // ShapeDefaults — dimension defaults. All nullable; null means "leave the shape's own
                // default alone", and each is read only while a dimension is being constructed.
                { "ShapeDefaults.DimOffset", "Default VDimension.Offset for new dimensions: how far the dimension line sits from the line joining Point1 and Point2, perpendicular to it. Null (the default) leaves VDimension's own 20. Negative values put the dimension line on the other side." },
                { "ShapeDefaults.DimArrowSize", "Default VDimension.ArrowSize / VRadialDimension.ArrowSize for new dimensions, in world units. Null leaves the shapes' own 8." },
                { "ShapeDefaults.DimTextHeight", "Default TextHeight for new dimensions, in world units. Null leaves the shapes' own 12." },
                { "ShapeDefaults.DimDecimalPlaces", "Default DecimalPlaces for the measured value on new dimensions. Null leaves the shapes' own 2. Setting 0 gives whole units. Ignored where CustomText is set, since that replaces the measurement outright." },
                { "ShapeDefaults.DimExtendBeyondDimLines", "Default VDimension.ExtendBeyondDimLines: how far the extension lines carry on PAST the dimension line, in world units. Null leaves 1.25. This is the small overshoot that makes a dimension read as drafted rather than as a bare bracket." },
                { "ShapeDefaults.DimOffsetFromOrigin", "Default VDimension.OffsetFromOrigin: the gap left between the measured point itself and the start of its extension line, so the witness line does not touch the geometry. Null leaves 0.625." },
                { "ShapeDefaults.DimPrefix", "Default Prefix string placed before the measured value on new dimensions (for instance \"R\" or \"approx. \"). Null leaves the empty string." },
                { "ShapeDefaults.DimSuffix", "Default Suffix string placed after the measured value on new dimensions (for instance \" mm\"). Null leaves the empty string." },
                { "ShapeDefaults.DimTextBgOpaque", "Default TextBackgroundOpaque for new dimensions. When true an opaque panel is drawn behind the dimension text so it stays legible over hatching or other geometry. Null leaves false." },
                { "ShapeDefaults.DimExtensionLineColor", "Default VDimension.ExtensionLineColor — the colour of the two witness lines only. Null leaves the shape's own null, which means \"use the dimension's Color\"." },
                { "ShapeDefaults.DimDimensionLineColor", "Default DimensionLineColor — the colour of the measuring line and its arrowheads only. Null means the dimension's Color is used." },
                { "ShapeDefaults.DimTextColor", "Default TextColor — the colour of the measurement text only. Null means the dimension's Color is used. Set the three Dim*Color defaults together to draw the line work faint and keep the numbers bright." },
                { "ShapeDefaults.DimSuppressDimensionLine", "Default VDimension.SuppressDimensionLine. When true the measuring line and its arrowheads are omitted and only the extension lines and the text are drawn. Null leaves false." },

                // LineType enum values
                { "LineType.Continuous", "Solid continuous line (default). Standard line with no gaps." },
                { "LineType.Dashed", "Dashed line pattern with long dashes and short gaps." },
                { "LineType.Dotted", "Dotted line pattern with short dots and gaps." },
                { "LineType.DashDot", "Alternating dash and dot pattern (dash-dot-dash-dot)." },
                { "LineType.DashDotDot", "Alternating dash and two dots pattern (dash-dot-dot-dash)." },
                { "LineType.Center", "Center line pattern (long-short-long), commonly used for centerlines in technical drawings." },
                { "LineType.Phantom", "Phantom line pattern (long-short-short), used for alternate positions or hidden features." },
                { "LineType.Hidden", "Hidden line pattern with short dashes, used for hidden edges in technical drawings." },

                // VCoordinateSystem
                { "VCoordinateSystem.Origin", "The origin of the local frame, in world coordinates. Read-only — use Translate() for a shifted copy." },
                { "VCoordinateSystem.XAxis", "The normalised X direction of the local frame. Read-only. (VTransform calls its equivalent BasisX; a coordinate system does not have a BasisX.)" },
                { "VCoordinateSystem.YAxis", "The normalised Y direction of the local frame. Read-only." },
                { "VCoordinateSystem.ZAxis", "The normalised Z direction of the local frame. Read-only. For a system built on the drawing plane this is (0, 0, 1), so a point's local Z is its signed height above that plane." },
                { "VCoordinateSystem.Identity", "The world frame: origin at (0, 0, 0) with the standard X, Y and Z axes. A fresh instance each time you read it." },
                { "VCoordinateSystem.ByOrigin", "Creates a frame at the given origin with the standard axes — a pure translation of the world frame. Overloads take a VXYZ or three doubles." },
                { "VCoordinateSystem.ByOriginVectors", "Creates a frame from an origin and all three axis vectors. The vectors are normalised but NOT re-orthogonalised, so pass a genuinely orthogonal set or ToLocal/ToWorld will not round-trip." },
                { "VCoordinateSystem.ByOriginXY", "Creates a frame from an origin and the X and Y directions. Z is their cross product, and Y is then recomputed as Z × X so the result is always orthonormal even if the two inputs were not perpendicular." },
                { "VCoordinateSystem.ByOriginZAxis", "Creates a frame with Z aligned to the given direction; X and Y are chosen arbitrarily but deterministically. Use it when only the facing direction matters." },
                { "VCoordinateSystem.ByPlane", "Creates a frame from a VPlane: origin from the plane, X and Y from its XVec/YVec, Z from its Normal." },
                { "VCoordinateSystem.ToLocal", "Converts a world point into this frame's coordinates, by subtracting Origin and projecting onto each axis." },
                { "VCoordinateSystem.ToWorld", "Converts a point expressed in this frame back to world coordinates. Overloads take a VXYZ or three doubles." },
                { "VCoordinateSystem.Translate", "Returns a NEW coordinate system with its origin moved by the vector and the same axes. This instance is unchanged." },
                { "VCoordinateSystem.Rotate", "Returns a NEW coordinate system with its axes rotated about the given axis; the origin stays put and this instance is unchanged. The angle is in DEGREES, like every other rotation in the library: Rotate(VXYZ.BasisZ, 90) is a quarter turn, and turns the X axis onto Y exactly as VXYZ.Rotate(90) does." },

                // VPlane
                { "VPlane.Origin", "A point on the plane. Read-only — a VPlane is immutable once built." },
                { "VPlane.Normal", "The unit vector perpendicular to the plane, computed as XVec × YVec. Read-only." },
                { "VPlane.XVec", "The in-plane X direction, normalised. Named XVec, not XAxis (VCoordinateSystem is the type with XAxis)." },
                { "VPlane.YVec", "The in-plane Y direction, normalised and perpendicular to XVec." },
                { "VPlane.CreateByNormalAndOrigin", "Creates a plane from a normal and a point on it. XVec and YVec are chosen arbitrarily but consistently, since a normal alone does not fix them." },
                { "VPlane.CreateByOriginAndBasis", "Creates a plane from an origin and two in-plane directions; both are normalised and the normal is their cross product." },
                { "VPlane.CreateByThreePoints", "Creates a plane through three points: p1 becomes the Origin, p2 - p1 the X direction, p3 - p1 the Y direction. Collinear points give a degenerate plane rather than an exception." },

                // VTransform
                { "VTransform.BasisX", "The image of the X axis under this transform — the first column of the (implicit) rotation matrix. Settable." },
                { "VTransform.BasisY", "The image of the Y axis under this transform. Settable." },
                { "VTransform.BasisZ", "The image of the Z axis under this transform. Settable." },
                { "VTransform.Origin", "The translation applied by OfPoint (and ignored by OfVector). Set it directly for a pure translation — there is no CreateTranslation factory." },
                { "VTransform.Identity", "A transform that changes nothing: the standard basis with a zero origin. A fresh instance each time you read it." },
                { "VTransform.CreateRotationDegrees", "Builds a rotation about an arbitrary axis, angle in DEGREES — matching the convention every other rotation in the library uses (Shape.Rotate, VXYZ.Rotate, VCoordinateSystem.Rotate, GeometryHelper.RotatePoint). This is the one to prefer. A thin wrapper: it multiplies by π/180 and forwards to CreateRotationRadians, so CreateRotationDegrees(axis, 90) and CreateRotationRadians(axis, Math.PI / 2) produce identical transforms." },
                { "VTransform.CreateRotationRadians", "Builds a rotation about an arbitrary axis using Rodrigues' formula, angle in RADIANS. The axis is normalised for you and Origin is left at zero, so the rotation is about the world origin — set Origin afterwards if you need it elsewhere. Reach for this only when you already hold radians (say, straight out of System.Math); otherwise CreateRotationDegrees reads better and matches the rest of the library." },
                { "VTransform.CreateRotation", "DEPRECATED — the original name for CreateRotationRadians, marked [Obsolete]. Behaviour is unchanged and existing calls still compile, but the name never said which unit it took, which is the whole problem: it reads as though it follows the library's degrees convention and does not. Replace it with CreateRotationDegrees(axis, degrees) if you want the usual convention, or CreateRotationRadians(axis, radians) for the identical behaviour under an honest name. It was retired rather than redefined to take degrees, because redefining it would leave every existing call compiling and silently rotating by 1/57th of what it used to." },
                { "VTransform.CreateReflection", "Builds a reflection across the given VPlane, mirroring both the basis vectors and the origin, so OfPoint mirrors correctly about a plane that does not pass through (0, 0, 0)." },
                { "VTransform.OfPoint", "Applies the full affine transform to a point: the basis, then the Origin translation." },
                { "VTransform.OfVector", "Applies only the basis to a direction, ignoring Origin — the right method for normals, velocities and other free vectors." },

                // DxfExporter
                { "DxfExporter.Export", "Exports shapes to a DXF file (AutoCAD format)." },
                { "DxfExporter.ExportToString", "Exports shapes to a DXF string." },

                // PdfExporter
                { "PdfExporter.Export", "Exports shapes to a PDF file." },
                { "PdfExporter.PageSize", "Gets or sets the page size (A4, Letter, etc.)." },
                { "PdfExporter.Margin", "Gets or sets the page margins." },

                // SvgExporter
                { "SvgExporter.Export", "Exports shapes to an SVG file." },
                { "SvgExporter.ExportToString", "Exports shapes to an SVG string." },
                { "SvgExporter.Width", "Gets or sets the SVG canvas width." },
                { "SvgExporter.Height", "Gets or sets the SVG canvas height." },

                // GifEncoder
                { "GifEncoder.AddFrame", "Adds a frame to the GIF animation." },
                { "GifEncoder.Save", "Saves the GIF to a file." },
                { "GifEncoder.FrameDelay", "Gets or sets the delay between frames in milliseconds." },
                { "GifEncoder.Repeat", "Gets or sets whether the GIF loops infinitely." },

                // VideoExporter
                { "VideoExporter.AddFrame", "Adds a frame (RenderTargetBitmap) to the video. Frames are encoded in sequence at the configured frame rate." },
                { "VideoExporter.Dispose", "Finalizes the video encoding and releases resources. Must be called to produce a valid MP4 file." },

                // ShapeArrayExtensions (extension methods)
                { "ShapeArrayExtensions.DrawAll", "Draws all shapes in the collection." },
                { "ShapeArrayExtensions.LinearArrayX", "Extension: creates copies along the X axis." },
                { "ShapeArrayExtensions.LinearArrayY", "Extension: creates copies along the Y axis." },
                { "ShapeArrayExtensions.LinearArray", "Extension: creates copies along a direction." },
                { "ShapeArrayExtensions.RectangularArray", "Extension: creates a grid pattern of copies." },
                { "ShapeArrayExtensions.CircularArray", "Extension: creates copies in a circle." },
                { "ShapeArrayExtensions.PathArray", "Extension: creates copies along a path." },
                { "ShapeArrayExtensions.SpiralArray", "Extension: creates copies in a spiral." },
                { "ShapeArrayExtensions.Mirror", "Extension: creates a mirrored copy." },

                // VPolygonBooleanExtensions (extension methods)
                { "VPolygonBooleanExtensions.Union", "Extension: combines polygons into one. Returns VPolygon or null if they don't overlap." },
                { "VPolygonBooleanExtensions.Intersect", "Extension: returns overlapping area (boolean AND)." },
                { "VPolygonBooleanExtensions.Difference", "Extension: subtracts one polygon from another." },
                { "VPolygonBooleanExtensions.Xor", "Extension: returns symmetric difference." },
                { "VPolygonBooleanExtensions.Contains", "Extension: tests if a point is inside the polygon." },
                { "VPolygonBooleanExtensions.GetArea", "Extension: calculates polygon area." },

                // VColor Static Properties (common colors)
                { "VColor.Red", "Returns \"Red\" color string." },
                { "VColor.Green", "Returns \"Green\" color string." },
                { "VColor.Blue", "Returns \"Blue\" color string." },
                { "VColor.Yellow", "Returns \"Yellow\" color string." },
                { "VColor.Orange", "Returns \"Orange\" color string." },
                { "VColor.Purple", "Returns \"Purple\" color string." },
                { "VColor.Pink", "Returns \"Pink\" color string." },
                { "VColor.Cyan", "Returns \"Cyan\" color string." },
                { "VColor.Magenta", "Returns \"Magenta\" color string." },
                { "VColor.White", "Returns \"White\" color string." },
                { "VColor.Black", "Returns \"Black\" color string." },
                { "VColor.Gray", "Returns \"Gray\" color string." },
                { "VColor.LimeGreen", "Returns \"LimeGreen\" color string." },
                { "VColor.Gold", "Returns \"Gold\" color string." },
                { "VColor.Coral", "Returns \"Coral\" color string." },

                // VColor Static Properties (extended palette)
                { "VColor.Brown", "Returns the \"Brown\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Crimson", "Returns the \"Crimson\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DarkBlue", "Returns the \"DarkBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DarkGreen", "Returns the \"DarkGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DarkOrange", "Returns the \"DarkOrange\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DarkRed", "Returns the \"DarkRed\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DarkViolet", "Returns the \"DarkViolet\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DeepPink", "Returns the \"DeepPink\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DeepSkyBlue", "Returns the \"DeepSkyBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.DodgerBlue", "Returns the \"DodgerBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.ForestGreen", "Returns the \"ForestGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Fuchsia", "Returns the \"Fuchsia\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.GreenYellow", "Returns the \"GreenYellow\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.HotPink", "Returns the \"HotPink\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.IndianRed", "Returns the \"IndianRed\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Indigo", "Returns the \"Indigo\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Khaki", "Returns the \"Khaki\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Lavender", "Returns the \"Lavender\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LawnGreen", "Returns the \"LawnGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightBlue", "Returns the \"LightBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightCoral", "Returns the \"LightCoral\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightGreen", "Returns the \"LightGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightPink", "Returns the \"LightPink\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightSalmon", "Returns the \"LightSalmon\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightSeaGreen", "Returns the \"LightSeaGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightSkyBlue", "Returns the \"LightSkyBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.LightYellow", "Returns the \"LightYellow\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Lime", "Returns the \"Lime\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Maroon", "Returns the \"Maroon\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumBlue", "Returns the \"MediumBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumOrchid", "Returns the \"MediumOrchid\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumPurple", "Returns the \"MediumPurple\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumSeaGreen", "Returns the \"MediumSeaGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumSlateBlue", "Returns the \"MediumSlateBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumSpringGreen", "Returns the \"MediumSpringGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumTurquoise", "Returns the \"MediumTurquoise\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MediumVioletRed", "Returns the \"MediumVioletRed\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.MidnightBlue", "Returns the \"MidnightBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Navy", "Returns the \"Navy\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Olive", "Returns the \"Olive\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.OliveDrab", "Returns the \"OliveDrab\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.OrangeRed", "Returns the \"OrangeRed\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Orchid", "Returns the \"Orchid\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.PaleGreen", "Returns the \"PaleGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.PaleTurquoise", "Returns the \"PaleTurquoise\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.PaleVioletRed", "Returns the \"PaleVioletRed\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Peru", "Returns the \"Peru\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Plum", "Returns the \"Plum\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.RoyalBlue", "Returns the \"RoyalBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Salmon", "Returns the \"Salmon\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SandyBrown", "Returns the \"SandyBrown\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SeaGreen", "Returns the \"SeaGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Sienna", "Returns the \"Sienna\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Silver", "Returns the \"Silver\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SkyBlue", "Returns the \"SkyBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SlateBlue", "Returns the \"SlateBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SlateGray", "Returns the \"SlateGray\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SpringGreen", "Returns the \"SpringGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.SteelBlue", "Returns the \"SteelBlue\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Tan", "Returns the \"Tan\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Teal", "Returns the \"Teal\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Thistle", "Returns the \"Thistle\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Tomato", "Returns the \"Tomato\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Turquoise", "Returns the \"Turquoise\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Violet", "Returns the \"Violet\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.Wheat", "Returns the \"Wheat\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },
                { "VColor.YellowGreen", "Returns the \"YellowGreen\" color string. Assign it straight to Color or FillColor; VColor members are plain strings, not colour objects." },

                // VColor Static Methods
                { "VColor.GetRandomColor", "Returns a random color string. If returnPastelColor is true (default), returns soft pastel colors; if false, returns vibrant colors." },
                { "VColor.GetRandomVibrantColor", "Returns a random vibrant color (good for strokes on dark backgrounds)." },
                { "VColor.GetRandomPastelColor", "Returns a random pastel color (good for fills)." },
                { "VColor.FromEnum", "Converts a ColorName enum value to its string representation." },
                { "VColor.FromRgb", "Creates a hex color string from RGB values (0-255). Example: FromRgb(255, 128, 0) returns \"#FF8000\"." },
                { "VColor.FromArgb", "Creates a hex color string from ARGB values (0-255). Example: FromArgb(128, 255, 0, 0) returns \"#80FF0000\"." },
                { "VColor.WithOpacity", "Creates a semi-transparent color from RGB values and opacity (0.0-1.0)." },
                { "VColor.GetVibrantColors", "Returns an array of all vibrant color names." },
                { "VColor.GetPastelColors", "Returns an array of all pastel color names." },

                // VArc Factory Methods
                { "VArc.FromStartCenterEnd", "Creates an arc from start point, center, and end point (determines angles from geometry)." },
                { "VArc.FromCenterStartEnd", "Creates an arc from center, start point, and end point (determines angles from geometry)." },
                { "VArc.FromStartCenterAngle", "Creates an arc from start point, center, and sweep angle in degrees." },
                { "VArc.FromCenterStartAngle", "Creates an arc from center, start point, and sweep angle in degrees." },
                { "VArc.FromStartCenterLength", "Creates an arc from start point, center, and desired arc length." },
                { "VArc.FromCenterStartLength", "Creates an arc from center, start point, and desired arc length." },
                { "VArc.FromStartEndRadius", "Creates an arc from start point, end point, and radius. Optional largeArc parameter (default false) selects the larger or smaller arc." },
                { "VArc.FromStartEndAngle", "Creates an arc from start point, end point, and sweep angle in degrees." },
                { "VArc.Continue", "Creates an arc that continues tangentially from the end of a previous ICurve with the specified arc length." },

                // VCircle Factory Methods
                { "VCircle.FromCenterDiameter", "Creates a circle from center point (or coordinates) and diameter (not radius)." },
                { "VCircle.FromTwoPoints", "Creates a circle using two points as diameter endpoints. Center is the midpoint." },

                // VSpline Properties
                { "VSpline.Tension", "Gets or sets the tension parameter (default 0.5). Range: 0 = sharp corners, 0.5 = standard Catmull-Rom, higher = looser curves." },
                { "VSpline.SegmentsPerSpan", "Gets or sets the number of segments rendered between each pair of control points (default 16). Higher values = smoother curve." },

                // VEllipse Angle Properties
                { "VEllipse.StartAngle", "Gets or sets the start angle in degrees for partial ellipses (default 0)." },
                { "VEllipse.EndAngle", "Gets or sets the end angle in degrees for partial ellipses (default 360)." },

                // Extended Boolean Operations
                { "BooleanOps.OffsetPolygonSafe", "Safely offsets a polygon inward, capping at the maximum safe distance to prevent collapse. Uses JoinType and EndType parameters." },
                { "BooleanOps.MaxSafeInwardOffset", "Returns the maximum safe inward offset distance for a polygon before it would collapse." },
                { "BooleanOps.MakeSimple", "Resolves self-intersections in a polygon, returning a list of simple (non-self-intersecting) polygons." },
                { "BooleanOps.HasSelfIntersections", "Returns true if the polygon has any self-intersections." },
                { "BooleanOps.Simplify", "Simplifies a polygon using the Douglas-Peucker algorithm. Optional tolerance parameter (default 0.1)." },
                { "BooleanOps.IntersectWithHoles", "Computes intersection of two polygons, returning PolygonWithHoles objects that preserve hole information." },
                { "BooleanOps.UnionWithHoles", "Computes union of two polygons, returning PolygonWithHoles objects that preserve hole information." },
                { "BooleanOps.DifferenceWithHoles", "Computes difference of two polygons, returning PolygonWithHoles objects that preserve hole information." },

                // VPolygonBooleanExtensions (missing extension methods)
                { "VPolygonBooleanExtensions.OffsetPolygon", "Extension: offsets polygon edges by a distance. Positive = outward, negative = inward." },
                { "VPolygonBooleanExtensions.OffsetPolygonSafe", "Extension: safely offsets polygon inward, capping at maximum safe distance." },
                { "VPolygonBooleanExtensions.MaxSafeInwardOffset", "Extension: returns the maximum safe inward offset distance." },
                { "VPolygonBooleanExtensions.HasSelfIntersections", "Extension: returns true if the polygon has self-intersections." },
                { "VPolygonBooleanExtensions.MakeSimple", "Extension: resolves self-intersections into simple polygons." },

                // PolygonWithHoles Members
                { "PolygonWithHoles.Outer", "Gets or sets the outer boundary polygon (counter-clockwise winding)." },
                { "PolygonWithHoles.Holes", "Gets or sets the list of hole polygons (clockwise winding)." },
                { "PolygonWithHoles.Area", "Gets the net area (outer area minus the sum of all hole areas)." },
                { "PolygonWithHoles.AddHole", "Adds a hole polygon to this polygon." },
                { "PolygonWithHoles.Contains", "Returns true if a point is inside the outer boundary and not inside any hole." },
                { "PolygonWithHoles.Clone", "Creates a deep copy of this PolygonWithHoles including outer and all holes." },
                { "PolygonWithHoles.FromPolygonList", "Static method that analyzes a list of polygons and builds PolygonWithHoles structures by detecting containment." },

                // Region Properties
                { "Region.OuterLoop", "Gets the outer boundary of the region as an ordered list of ICurve forming a closed loop. Curves are stored in traversal order: the end of each curve connects to the start of the next." },
                { "Region.Holes", "Gets the inner holes of the region. Each hole is an ordered list of ICurve forming a closed loop." },
                { "Region.Area", "Gets the area of the region (outer area minus hole areas). Computed via polygon approximation of the boundary curves." },
                { "Region.SignedArea", "Gets the signed area of the outer loop. Positive for counter-clockwise, negative for clockwise winding." },
                { "Region.Perimeter", "Gets the total perimeter length (outer loop + all holes)." },

                // Region Methods
                { "Region.AddHole", "Adds a hole to the region. Overloads: AddHole(List<ICurve>) where the curves form a closed, non-self-intersecting loop, or AddHole(ICurve) taking a single closed curve (circle, ellipse, closed polygon/polyline/spline). The hole should lie entirely inside the outer boundary; the source curve is consumed (removed from the canvas)." },
                { "Region.DistanceTo", "Returns the shortest distance from the point to the region's nearest boundary — the outer loop or any hole edge, whichever is closer — handling both straight VLine edges and curved segments (arcs, beziers, splines, a whole circle or ellipse). Zero on an outline, positive both inside and outside; it is a distance to the boundary, not a signed depth, so pair it with Contains for the side. Holes are included so this agrees with Contains, which already excludes them." },
                { "Region.Contains", "Returns true if a point is inside the outer loop and outside all holes. Uses winding number algorithm on a polygon approximation." },
                { "Region.SampleLoop", "Static helper that flattens one boundary loop — a List<ICurve> such as OuterLoop or an entry of Holes — into plain vertices, sampling each non-linear curve into segmentsPerCurve pieces. This is the sampling the region boolean operations use internally, exposed so you can reproduce or inspect it. Nothing is drawn." },
                { "Region.ToPolygon", "Converts the region to a VPolygon using curve endpoints only (low-fidelity). Curved segments become straight edges." },
                { "Region.ToPolygonHighRes", "Converts the region to a VPolygon by densely sampling each curve (high-fidelity). Parameter: segmentsPerCurve (default 32)." },
                { "Region.ToPolygonWithHoles", "Converts the region to a PolygonWithHoles (high-fidelity polygon approximation including holes). Parameter: segmentsPerCurve (default 32)." },
                { "Region.FromPolygon", "Static method: creates a Region from a VPolygon. Each polygon edge becomes a VLine in the region's OuterLoop." },
                { "Region.FromPolygonWithHoles", "Static method: creates a Region from a PolygonWithHoles, including outer boundary and all holes." },
                { "Region.Clone", "Creates a deep copy of this region with all curves and holes cloned, and the styling copied across. The cloned boundary curves are internal to the region and are not registered individually, so cloning a region no longer leaves a loose copy of every edge on the canvas — only the region itself is a shape." },
                { "Region.Move", "Translates the region (outer loop and all holes) by the specified displacement vector." },
                { "Region.Rotate", "Rotates the region around the specified pivot by the given angle in degrees." },
                { "Region.Flip", "Mirrors the region across the specified axis line." },
                { "Region.Scale", "Scales the region relative to a center point by the specified factor." },
                { "Region.GetBounds", "Returns the axis-aligned bounding box of the region's outer loop." },
                { "Region.ToString", "Returns a string representation: \"Region(Outer: N curves, Holes: M, Total: T curves)\"." },

                // RegionBooleanOps Methods
                { "RegionBooleanOps.Union", "Computes the union of two or more regions. Returns a single Region if successful, or null if disjoint. Overloads: Union(a, b), Union(params Region[]), Union(IEnumerable<Region>)." },
                { "RegionBooleanOps.Intersect", "Computes the intersection of two regions. Returns a List<Region> of overlapping areas." },
                { "RegionBooleanOps.Difference", "Computes the difference of two regions (a - b). Returns a List<Region>." },
                { "RegionBooleanOps.Xor", "Computes the symmetric difference (XOR) of two regions. Returns a List<Region>." },
                { "RegionBooleanOps.UnionWithHoles", "Computes the union of two regions, returning List<Region> with hole information preserved." },
                { "RegionBooleanOps.IntersectWithHoles", "Computes the intersection of two regions, returning List<Region> with hole information preserved." },
                { "RegionBooleanOps.DifferenceWithHoles", "Computes the difference of two regions, returning List<Region> with hole information preserved." },
                { "RegionBooleanOps.PointInRegion", "Checks if a point is inside a region. Delegates to region.Contains(point)." },
                { "RegionBooleanOps.Area", "Calculates the area of a region. Delegates to region.Area." },

                // RegionBooleanExtensions Methods
                { "RegionBooleanExtensions.Union", "Extension: computes union of this region with another. Returns Region? (null if disjoint)." },
                { "RegionBooleanExtensions.Intersect", "Extension: computes intersection of this region with another. Returns List<Region>. Note: use RegionBooleanOps.Intersect(a, b) to avoid collision with Shape.Intersect." },
                { "RegionBooleanExtensions.Difference", "Extension: computes difference (this - other). Returns List<Region>." },
                { "RegionBooleanExtensions.Xor", "Extension: computes symmetric difference (XOR). Returns List<Region>." },
                { "RegionBooleanExtensions.ContainsPoint", "Extension: checks if a point is inside this region." },
                { "RegionBooleanExtensions.GetArea", "Extension: calculates the area of this region." },

                // JoinType Enum Values
                { "JoinType.Miter", "Sharp corner joins (default). May produce spikes on acute angles; controlled by miter limit." },
                { "JoinType.Round", "Rounded corner joins. Produces smooth rounded corners at offset vertices." },
                { "JoinType.Square", "Squared-off corner joins. Extends corners at right angles." },

                // EndType Enum Values
                { "EndType.Polygon", "Treats the path as a closed polygon (default). Both ends are joined." },
                { "EndType.OpenRound", "Open path with rounded end caps." },
                { "EndType.OpenSquare", "Open path with squared end caps." },
                { "EndType.OpenButt", "Open path with flat (butt) end caps." },

                // VHatch Properties
                { "VHatch.Boundary", "Gets or sets the closed boundary polygon points that define the hatch area." },
                { "VHatch.Pattern", "Gets or sets the HatchType pattern definition used for this hatch." },
                { "VHatch.PatternScale", "Gets or sets the scale factor applied to the pattern. Larger values = less dense. Default 1.0." },
                { "VHatch.PatternAngle", "Gets or sets the additional rotation angle (degrees) applied to the entire pattern. Default 0." },
                { "VHatch.GenerateLines", "Generates the hatch line segments clipped to the boundary. Returns a list of (Start, End) VXYZ pairs." },
                { "VHatch.FromDefinition", "Static. Builds a VHatch from a raw AutoCAD .pat definition string rather than a named pattern — HatchType.Parse does the parsing. Two overloads take the boundary as a VPolygon or as a List<VXYZ>; both then take patDefinition, scale (default 1.0) and angle (default 0.0). The string is the usual .pat form: a header line starting with * (name, description) followed by one line per line-family, each 'angle, originX,originY, deltaX,deltaY [, dash, gap, ...]'. Use it for a pattern you generate or read from a file; use the BuiltInHatch enum for the 72 that ship with the library." },
                { "VHatch.Contains", "Returns true when the point is inside the hatch boundary — an exact even-odd test on the boundary polygon, not the bounding box. The pattern lines themselves are not consulted; a point in the white space between two hatch strokes still counts as inside." },
                { "VHatch.DistanceTo", "Returns the shortest distance from the point to the hatch BOUNDARY, treated as a closed path (the closing edge is included). Zero on the outline, positive both inside and outside — pair it with Contains for the side." },

                // HatchType Properties/Methods
                { "HatchType.Name", "Gets or sets the pattern name." },
                { "HatchType.Description", "Gets or sets the pattern description." },
                { "HatchType.Lines", "Gets or sets the list of HatchPatternLine definitions that make up this pattern." },
                { "HatchType.Parse", "Static method: parses a hatch pattern from an AutoCAD .pat format string. First line should be '*NAME, Description', subsequent lines define line families." },
                { "HatchType.GetBuiltIn", "Static method: retrieves a built-in hatch pattern by name (string, case-insensitive) or by BuiltInHatch enum value. Forwards to BuiltInHatches.Get, so it hands back a fresh copy you are free to modify." },
                { "HatchType.Clone", "Returns a deep copy of this pattern, cloning every line family (and each family's Dashes array) rather than sharing them. Use it before adapting a pattern you did not build yourself, so the original keeps its settings." },

                // HatchPatternLine Properties
                { "HatchPatternLine.Angle", "Angle of the line family in degrees." },
                { "HatchPatternLine.OriginX", "X coordinate of the line origin." },
                { "HatchPatternLine.OriginY", "Y coordinate of the line origin." },
                { "HatchPatternLine.DeltaX", "Delta X offset between successive parallel lines (shift along line direction)." },
                { "HatchPatternLine.DeltaY", "Delta Y offset between successive parallel lines (spacing perpendicular to line direction)." },
                { "HatchPatternLine.Dashes", "Dash pattern array. Positive values = dash length, negative = gap length, 0 = dot, empty = continuous line." },
                { "HatchPatternLine.Clone", "Returns a deep copy of this line family, including a copy of the Dashes array — so editing the copy's dashes cannot reach back into the original. This is what HatchType.Clone calls for each of its families." },

                // BuiltInHatches Methods
                { "BuiltInHatches.Get", "Retrieves a built-in hatch pattern by name (string, case-insensitive; hyphenated forms like 'AR-B816' work) or by BuiltInHatch enum value. An unknown name throws ArgumentException. BOTH overloads return a FRESH COPY on every call, so the pattern you get back is yours to modify — changing its angle, spacing or dashes cannot leak into a later lookup of the same name. The cache holds the parsed template behind the copy, so repeated lookups remain cheap. HatchType.GetBuiltIn forwards here and behaves identically." },
                { "BuiltInHatches.GetAllNames", "Returns all available built-in hatch pattern names." },

                // GeometryDiagnostics
                { "GeometryDiagnostics.Sink", "The Action<string> that receives the geometry library's diagnostic messages, or null. Null is the default and discards everything, so a consumer with no console pays nothing. The host application sets it once at startup — DoodleSharp routes it to the console panel, where messages appear tagged 'Geometry' — and you can replace or wrap it to capture the messages yourself. Restore the previous value when you are done." },
                { "GeometryDiagnostics.Report", "Sends a message to whatever Sink is currently installed; a null sink means the message is dropped. It NEVER throws — an exception raised by the sink is swallowed, so a broken logger cannot break the geometry operation that was reporting. BooleanOps.Union is the most visible caller: it is how a null union result explains itself." },

                // CurveGeometry
                { "CurveGeometry.DistanceToSegment", "Returns the shortest distance from a point to the segment [a, b], by projecting onto the line and clamping the projection to the segment — so a point beyond either end measures to that endpoint. A degenerate (zero-length) segment falls back to the distance to the point itself rather than dividing by zero." },
                { "CurveGeometry.DistanceToPath", "Returns the shortest distance from a point to a polyline through the given vertices, taking the nearest of every segment. Pass closed: true to add the edge from the last vertex back to the first. A null or empty vertex list returns double.PositiveInfinity; a single vertex returns the distance to that point." },
                { "CurveGeometry.DistanceToCurve", "Returns the shortest distance from a point to any ICurve by sampling it into a polyline (samples defaults to 96, and is floored at 2) and measuring against that. This is what VBezier and VSpline use, since they have no practical closed form; raise samples when you need more accuracy than a fraction of a pixel." },
                { "CurveGeometry.IsOnStroke", "Decides whether a measured distance counts as lying ON a stroke of the given size. The tolerance is max(GeometryTolerance.Epsilon, |curveExtent| × 1e-6) — relative, so that a hundred-unit line and a hundred-thousand-unit line behave the same way and the answer does not depend on the units your drawing happens to use. This is the test behind Contains on every open curve." },
            };
        }

        private string GetMemberDescription(string className, string memberName)
        {
            var key = $"{className}.{memberName}";
            if (_memberDescriptions != null && _memberDescriptions.TryGetValue(key, out var desc))
                return desc;
            return "";
        }

        public FlowDocument GenerateWelcomePage()
        {
            var doc = new FlowDocument();
            doc.FontFamily = new FontFamily("Segoe UI");
            doc.PagePadding = new Thickness(20);
            doc.ColumnWidth = double.NaN;

            // Title
            var title = new Paragraph(new Run("Welcome to DoodleSharp"))
            {
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateGray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            doc.Blocks.Add(title);

            // Tagline
            var tagline = new Paragraph(new Run("A Visual Programming Environment for 2D Geometry"))
            {
                FontSize = 16,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Teal,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 30)
            };
            doc.Blocks.Add(tagline);

            // Introduction
            AddWelcomeSectionHeader(doc, "What is DoodleSharp?");
            doc.Blocks.Add(new Paragraph(new Run(
                "DoodleSharp is an interactive application that lets you write C# code to create and visualize 2D geometric shapes. " +
                "Simply write code in the built-in editor, press F5 (or click Run), and see your shapes appear on the canvas instantly. " +
                "It's perfect for learning geometry, creating diagrams, prototyping visualizations, and exploring mathematical concepts."))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 15) });

            // Key Features
            AddWelcomeSectionHeader(doc, "Key Features");
            var featuresList = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 20)
            };
            AddListItem(featuresList, "C# Code Editor", "Roslyn-powered IntelliSense, semantic highlighting, and refactoring");
            AddListItem(featuresList, "Rich Shape Library", "Points, lines, circles, rectangles, ellipses, arcs, polygons, polylines, Bezier curves, splines, text, arrows, and dimensions");
            AddListItem(featuresList, "Drawing Tools", "Draw shapes directly on the canvas with automatic code generation");
            AddListItem(featuresList, "Animation System", "Create timeline-based animations with draw, move, rotate, and flip effects");
            AddListItem(featuresList, "Interactive Canvas", "Zoom with mouse wheel, pan with middle-click, toggle grid display");
            AddListItem(featuresList, "Export Options", "Save visualizations as PNG images, animated GIFs, or MP4 videos");
            AddListItem(featuresList, "Project Management", "Organize multiple code files into projects with tabbed editing");
            AddListItem(featuresList, "NuGet Package Manager", "Search, install, update, and remove NuGet packages via Tools menu");
            doc.Blocks.Add(featuresList);

            // Getting Started
            AddWelcomeSectionHeader(doc, "Getting Started");
            var stepsList = new List
            {
                MarkerStyle = TextMarkerStyle.Decimal,
                Margin = new Thickness(20, 0, 0, 20)
            };
            AddListItem(stepsList, "Create or Open a Project", "Use File > New Project or File > Open to start");
            AddListItem(stepsList, "Write Your Code", "The entry point is StartViz.Viz.Main() in StartViz.cs");
            AddListItem(stepsList, "Create Shapes", "Instantiate shape objects (e.g., new VCircle(0, 0, 50))");
            AddListItem(stepsList, "Keep Shapes Visible", "Shapes appear when constructed; call .Place() on method results");
            AddListItem(stepsList, "Run Your Code", "Press F5 or click the Run button to see results");
            doc.Blocks.Add(stepsList);

            // Quick Example
            AddWelcomeSectionHeader(doc, "Quick Example");
            var exampleCode = @"using C2VGeometry;

namespace StartViz
{
    public class Viz
    {
        public static void Main()
        {
            // Create a circle at origin with radius 50
            var circle = new VCircle(0, 0, 50);
            circle.Color = ""Cyan"";
            circle.FillColor = ""#4000FFFF"";

            // Add crosshairs. These are not assigned to a variable, so the
            // auto-naming pass misses them — Place() keeps them visible.
            new VLine(-60, 0, 60, 0).Place();
            new VLine(0, -60, 0, 60).Place();
        }
    }
}";
            var codeP = new Paragraph(new Run(exampleCode))
            {
                FontFamily = new FontFamily("Consolas"),
                Background = Brushes.WhiteSmoke,
                Padding = new Thickness(15),
                Margin = new Thickness(0, 0, 0, 20)
            };
            doc.Blocks.Add(codeP);

            // Drawing Tools
            AddWelcomeSectionHeader(doc, "Drawing Tools");
            doc.Blocks.Add(new Paragraph(new Run(
                "Draw shapes directly on the canvas using the toolbar below the menu bar. " +
                "Click to place points, and the corresponding C# code is automatically generated and inserted into your Main() method."))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 10) });

            // Drawing tools table
            var drawingTable = new Table();
            drawingTable.CellSpacing = 0;
            drawingTable.BorderBrush = Brushes.LightGray;
            drawingTable.BorderThickness = new Thickness(1);
            drawingTable.Columns.Add(new TableColumn { Width = new GridLength(100) });
            drawingTable.Columns.Add(new TableColumn { Width = new GridLength(250) });
            drawingTable.Columns.Add(new TableColumn { Width = new GridLength(100) });

            var drawingRowGroup = new TableRowGroup();
            // Header
            var drawingHeaderRow = new TableRow();
            drawingHeaderRow.Background = Brushes.AliceBlue;
            drawingHeaderRow.Cells.Add(CreateHelpHeaderCell("Shape"));
            drawingHeaderRow.Cells.Add(CreateHelpHeaderCell("Method"));
            drawingHeaderRow.Cells.Add(CreateHelpHeaderCell("Clicks"));
            drawingRowGroup.Rows.Add(drawingHeaderRow);

            AddDrawingToolRow(drawingRowGroup, "Point", "Single click", "1", false);
            AddDrawingToolRow(drawingRowGroup, "Line", "Click start, click end", "2", true);
            AddDrawingToolRow(drawingRowGroup, "Circle", "Click center, click radius", "2", false);
            AddDrawingToolRow(drawingRowGroup, "Rectangle", "Click corner, click opposite", "2", true);
            AddDrawingToolRow(drawingRowGroup, "Arc", "Click center, start, end", "3", false);
            AddDrawingToolRow(drawingRowGroup, "Polygon", "Click vertices, double-click", "N", true);
            AddDrawingToolRow(drawingRowGroup, "Polyline", "Click points, double-click", "N", false);
            AddDrawingToolRow(drawingRowGroup, "Bezier", "Click start, ctrl1, ctrl2, end", "4", true);

            drawingTable.RowGroups.Add(drawingRowGroup);
            doc.Blocks.Add(drawingTable);

            // Snap support
            doc.Blocks.Add(new Paragraph(new Run("\nSnap Support: ") { FontWeight = FontWeights.SemiBold })
            { FontSize = 14, Margin = new Thickness(0, 10, 0, 5) });
            var snapList = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 15)
            };
            AddListItem(snapList, "Endpoints", "Start/end points of lines, arcs, polylines");
            AddListItem(snapList, "Midpoints", "Middle point of lines and curves");
            AddListItem(snapList, "Centers", "Center of circles, arcs, ellipses");
            AddListItem(snapList, "Intersections", "Where two shapes cross");
            AddListItem(snapList, "Nearest", "Closest point on any curve");
            doc.Blocks.Add(snapList);

            // Keyboard Shortcuts
            AddWelcomeSectionHeader(doc, "Keyboard Shortcuts");
            var shortcutsTable = new Table();
            shortcutsTable.CellSpacing = 0;
            shortcutsTable.BorderBrush = Brushes.LightGray;
            shortcutsTable.BorderThickness = new Thickness(1);
            shortcutsTable.Columns.Add(new TableColumn { Width = new GridLength(150) });
            shortcutsTable.Columns.Add(new TableColumn { Width = new GridLength(300) });

            var rowGroup = new TableRowGroup();
            // File operations
            AddShortcutRow(rowGroup, "F5 / Ctrl+Enter", "Run code", true);
            AddShortcutRow(rowGroup, "Ctrl+S", "Save all files", false);
            AddShortcutRow(rowGroup, "Ctrl+N", "New file", true);
            AddShortcutRow(rowGroup, "Ctrl+Shift+N", "New project", false);
            AddShortcutRow(rowGroup, "Ctrl+O", "Open project", true);
            // Editor operations
            AddShortcutRow(rowGroup, "Ctrl+Shift+F", "Format code", false);
            AddShortcutRow(rowGroup, "Ctrl+/", "Toggle comment", true);
            // Find and Replace
            AddShortcutRow(rowGroup, "Ctrl+F", "Find", false);
            AddShortcutRow(rowGroup, "Ctrl+H", "Find and Replace", true);
            AddShortcutRow(rowGroup, "F3", "Find Next", false);
            AddShortcutRow(rowGroup, "Shift+F3", "Find Previous", true);
            // Line operations
            AddShortcutRow(rowGroup, "Alt+Up/Down", "Move line up/down", false);
            AddShortcutRow(rowGroup, "Shift+Alt+Up", "Copy line up", true);
            AddShortcutRow(rowGroup, "Shift+Alt+Down", "Copy line down", false);
            AddShortcutRow(rowGroup, "Ctrl+Shift+D", "Delete line", true);
            // Selection operations
            AddShortcutRow(rowGroup, "Shift+Alt+Right", "Expand selection", false);
            AddShortcutRow(rowGroup, "Shift+Alt+Left", "Shrink selection", true);
            AddShortcutRow(rowGroup, "Ctrl+D", "Add next occurrence", false);
            AddShortcutRow(rowGroup, "Ctrl+Shift+L", "Select all occurrences", true);
            AddShortcutRow(rowGroup, "Ctrl+Alt+Up", "Add cursor above", false);
            AddShortcutRow(rowGroup, "Ctrl+Alt+Down", "Add cursor below", true);
            // Canvas & Tools
            AddShortcutRow(rowGroup, "Mouse Wheel", "Zoom canvas", false);
            AddShortcutRow(rowGroup, "Middle Click", "Pan canvas", true);
            AddShortcutRow(rowGroup, "Ctrl+G", "Zoom to shape by ID", false);
            AddShortcutRow(rowGroup, "Ctrl+M", "Toggle Measuring Tape tool", true);
            // Drawing Tools
            AddShortcutRow(rowGroup, "P", "Point drawing tool", false);
            AddShortcutRow(rowGroup, "L", "Line drawing tool", true);
            AddShortcutRow(rowGroup, "C", "Circle drawing tool", false);
            AddShortcutRow(rowGroup, "R", "Rectangle drawing tool", true);
            AddShortcutRow(rowGroup, "Esc", "Cancel drawing / Return to select", false);
            // Code Navigation & Intellisense
            AddShortcutRow(rowGroup, "F12", "Go to Definition", true);
            AddShortcutRow(rowGroup, "Shift+F12", "Find All References", false);
            AddShortcutRow(rowGroup, "Alt+F12", "Peek Definition", true);
            AddShortcutRow(rowGroup, "Ctrl+.", "Quick Fix (add using)", false);
            AddShortcutRow(rowGroup, "Ctrl+Shift+O", "Document Symbols", true);
            AddShortcutRow(rowGroup, "Ctrl+T", "Workspace Symbols", false);
            AddShortcutRow(rowGroup, "Ctrl+Shift+H", "Call Hierarchy", true);
            AddShortcutRow(rowGroup, "Ctrl+Shift+T", "Type Hierarchy", false);
            AddShortcutRow(rowGroup, "F2", "Rename Symbol", true);
            shortcutsTable.RowGroups.Add(rowGroup);
            doc.Blocks.Add(shortcutsTable);

            // Coordinate System
            AddWelcomeSectionHeader(doc, "Coordinate System");
            doc.Blocks.Add(new Paragraph(new Run(
                "DoodleSharp uses a standard mathematical coordinate system with the origin (0, 0) at the center of the canvas. " +
                "The X-axis points right and the Y-axis points up (not down like typical screen coordinates). " +
                "Positive angles are measured counter-clockwise from the positive X-axis."))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 20) });

            // Tips
            AddWelcomeSectionHeader(doc, "Tips");
            var tipsList = new List
            {
                MarkerStyle = TextMarkerStyle.Circle,
                Margin = new Thickness(20, 0, 0, 20)
            };
            AddListItem(tipsList, "Colors", "Use color names (\"Red\", \"Cyan\") or hex codes (\"#FF0000\", \"#80FFFFFF\" for semi-transparent)");
            AddListItem(tipsList, "VizConsole", "Use VizConsole.Log() to output debug messages to the console panel");
            AddListItem(tipsList, "Auto-update Canvas", "Canvas updates automatically as you type (500ms delay). Disable in Settings > Application Settings if you prefer manual Run");
            AddListItem(tipsList, "No Placement Call Needed", "Shapes appear automatically when created - Place() is only for shapes that did not come from a plain `new`");
            AddListItem(tipsList, "Show/Hide Shapes", "Use shape.Hide() and shape.Show() to control visibility without removing from canvas");
            AddListItem(tipsList, "ShapeDefaults", "Set ShapeDefaults.GlobalColor to apply colors to all new shapes");
            AddListItem(tipsList, "Animation", "Create a Timeline, add animations, and call .Play() to animate shapes");
            AddListItem(tipsList, "Drawing Tools", "Use the toolbar or press P/L/C/R to draw shapes directly on canvas with auto-generated code");
            AddListItem(tipsList, "Help Browser", "Select any class from the tree on the left to see its documentation");
            AddListItem(tipsList, "Find and Replace", "Press Ctrl+F to find, Ctrl+H to find and replace. Supports regex and project-wide search");
            AddListItem(tipsList, "NuGet Packages", "Use Tools > NuGet Package Manager to add external libraries like Newtonsoft.Json");
            AddListItem(tipsList, "Shape IDs", "Every shape has a unique Id property. Use Ctrl+G to zoom to a shape by its ID");
            AddListItem(tipsList, "Outliner", "The Outliner panel shows all shapes grouped by type. Click an ID to zoom to that shape");
            AddListItem(tipsList, "Outliner Hover", "Hover over shapes in the Outliner to highlight them on the canvas");
            AddListItem(tipsList, "Measuring Tool", "Press Ctrl+M to activate the Measuring Tape with AutoCAD-style snap points");
            AddListItem(tipsList, "Snap Settings", "Configure snap types (Endpoint, Midpoint, Center, etc.) in Settings > Application Settings");
            AddListItem(tipsList, "Highlight Settings", "Customize Outliner hover highlight color and opacity in Settings > Application Settings");
            AddListItem(tipsList, "Circumcircle", "Create a circle through 3 points: new VCircle(p1, p2, p3)");
            doc.Blocks.Add(tipsList);

            // Footer
            var footer = new Paragraph(new Run("Select a class from the tree on the left to view its documentation."))
            {
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            };
            doc.Blocks.Add(footer);

            return doc;
        }

        private void AddWelcomeSectionHeader(FlowDocument doc, string text)
        {
            doc.Blocks.Add(new Paragraph(new Run(text))
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Teal,
                Margin = new Thickness(0, 15, 0, 8),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 5)
            });
        }

        private void AddListItem(List list, string title, string description)
        {
            var para = new Paragraph();
            para.Inlines.Add(new Run(title + ": ") { FontWeight = FontWeights.SemiBold });
            para.Inlines.Add(new Run(description));
            para.Margin = new Thickness(0, 2, 0, 2);
            list.ListItems.Add(new ListItem(para));
        }

        private void AddShortcutRow(TableRowGroup group, string shortcut, string description, bool isAlt)
        {
            var row = new TableRow();
            if (isAlt) row.Background = Brushes.WhiteSmoke;

            var keyCell = new TableCell(new Paragraph(new Run(shortcut) { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold }))
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var descCell = new TableCell(new Paragraph(new Run(description)))
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            row.Cells.Add(keyCell);
            row.Cells.Add(descCell);
            group.Rows.Add(row);
        }

        private TableCell CreateHelpHeaderCell(string text)
        {
            return new TableCell(new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold })
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4)
            };
        }

        private void AddDrawingToolRow(TableRowGroup group, string shape, string method, string clicks, bool isAlt)
        {
            var row = new TableRow();
            if (isAlt) row.Background = Brushes.WhiteSmoke;

            var shapeCell = new TableCell(new Paragraph(new Run(shape) { FontWeight = FontWeights.SemiBold }))
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var methodCell = new TableCell(new Paragraph(new Run(method)))
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            var clicksCell = new TableCell(new Paragraph(new Run(clicks)) { TextAlignment = TextAlignment.Center })
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 0, 0, 1)
            };

            row.Cells.Add(shapeCell);
            row.Cells.Add(methodCell);
            row.Cells.Add(clicksCell);
            group.Rows.Add(row);
        }
    }
}
