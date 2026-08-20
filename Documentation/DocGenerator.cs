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
        /// <summary>
        /// The members a documentation page lists. Static is included deliberately: the whole
        /// public surface of a static class (VColor, BooleanOps, Chart, GlobalParameters, Frame,
        /// GeometryHelper, ...) and every static factory on the shapes lives behind it, and
        /// leaving it out rendered those pages with no members even though descriptions existed.
        /// Shared with HelpWindow's search index so the two cannot list different members.
        /// </summary>
        public const BindingFlags MemberFlags =
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

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

                // Base classes
                { "Shape", "Abstract base class for all drawable shapes; implements IDrawable. Every shape auto-registers on construction (Shape.DefaultRegistry is wired to the canvas), so nothing extra is needed to make one visible. Place() is the call for everything else: it puts a shape on the canvas and keeps it there (registering it and setting IsExplicitlyDrawn, which exempts it from the post-Main() sweep that hides unnamed shapes). Reach for it on method results (booleans, ArrayOps, Chart), on the query results that deliberately do not draw their answer (GeometryHelper.IntersectLineLine and friends, VRay.ToFiniteLine, VRay.ToXLine, VXLine.ToFiniteLine), and on anything built while AutoRegister was false. It is idempotent, and Remove() is its inverse. Place(viewport) is the second overload: it does everything Place() does AND assigns the shape to one cell of the viewport grid — new VCircle(new VXYZ(0, 0), 10).Place(Viewports[0][1]) — so on a divided canvas it is a move rather than a first registration, since construction already put the shape on the root. Draw() is the historical name for Place() and is exactly equivalent; existing files that call it keep working unchanged, and there is nothing to migrate. The drawing tools and editor snippets now write Place(). Identity: Id (long, assigned automatically, reset to 1 at the start of each run) and Name (string, default \"\"). Styling: Color, FillColor (both color-name or hex strings), LineWeight, LineType, LineTypeScale. Draw order: ZIndex (int, default 0, higher on top, ties keep creation order). State: IsVisible, IsSelected, IsPlaced, IsExplicitlyDrawn. Animation: DrawFactor (0-1 progressive drawing), OffsetX, OffsetY, RotationAngle (virtual — VRectangle overrides it with real geometry, so RotateAnimation works on a rectangle too), RotationPivot, FlipProgress, FlipAxis, Opacity. Static configuration: DefaultRegistry, AutoRegister, DefaultColor (\"Cyan\"), DefaultFillColor (\"Transparent\"), DefaultLineWeight (2.0), DefaultLineType (Continuous), DefaultLineTypeScale (1.0), ResetDefaults(), ResetIdCounter(). Methods: Place() (and its historical alias Draw()), Remove(), Show(), Hide(), Clone() (returns the same type via covariant return), CopyStyleTo(target) (copies the five styling members plus ZIndex onto another shape and returns it), Move(), Rotate(), Flip(), Scale(), GetBounds() (returns BoundingBox), Contains(), DistanceTo(), Intersect(), DoesIntersect(), GetControlPoints(), MoveControlPoint(). Draw order is the ZIndex property, not a method: the BringAbove(other)/SendBehind(other) pair was removed, because reordering the list pairwise was undone by the next shape constructed. Contains() and DistanceTo() are bounding-box fallbacks on the base class, but every shape with a real outline overrides them with true geometry: VLine, VPolyline, VArc, VBezier, VSpline, VXLine and VRay test/measure against the stroke; VPolygon, VRectangle, VCircle, VEllipse, VGroup, VHatch and Region do a genuine interior Contains and measure to the outline, which means zero on it and positive on both sides — not a signed depth, so pair DistanceTo with Contains. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension keep the bounding-box answer, because for those the box is the shape or there is no outline to test; a reflection test (ShapeOverrideConsistencyTests) fails the build if a new shape is added without both overrides. Visibility note: after Main() returns, shapes with empty Name and IsExplicitlyDrawn=false are auto-hidden. The auto-naming pass only fills Name for `var x = new VShape(...)` and field declarations — for List.Add, array-slot assignments, and helper-returned shapes, set Name explicitly in the initializer or call .Place(). The console logs a warning when shapes get hidden." },
                { "BoundingBox", "Represents an axis-aligned bounding box with Min (lower-left) and Max (upper-right) corner points, both VXYZ. Returned by Shape.GetBounds() on every shape. Read-only properties: Min, Max, Width (Max.X - Min.X), Height (Max.Y - Min.Y), Center, Area (Width × Height). Methods: Contains(point) — inclusive of the boundary and ignoring Z; Intersects(other) — true when the boxes overlap or merely touch; Union(other) — the smallest box containing both; Expand(distance) — grown by the distance on all four sides (negative values contract, and may invert the box). Constructible directly: new BoundingBox(min, max). Supports tuple deconstruction: var (min, max) = bounds. Infinite shapes (VRay, VXLine) return boxes with non-finite corners." },
                { "IDrawable", "Interface for any object that can be drawn on the canvas. Defines Draw() plus the five styling properties every drawable exposes: Color, FillColor, LineWeight, LineType and LineTypeScale. Shape implements it, and ICurve extends it. Both Place() and Draw() are declared here, so either reaches the same behaviour through an IDrawable or ICurve reference exactly as it does through Shape." },
                { "ICurve", "Interface for geometric shapes that can be treated as curves. Implemented by VLine, VCircle, VArc, VEllipse, VPolyline, VPolygon, VBezier, VSpline, VRay and VXLine (VRectangle and VCell inherit it through VPolygon). Extends IDrawable, so all curves have Draw(), Color, FillColor, LineWeight, LineType and LineTypeScale. Properties: StartPoint, EndPoint (VXYZ; equal for closed curves), Vertices (List<VXYZ> of the defining points), SelfIntersecting. Methods: GetLength(), Divide(n), Measure(segmentLength), Project(point), PointAtSegmentLength(len), Offset(distance), Offset(List<double>), PointsAtChordLengthFromPoint(point, chordLength), SplitAtPoint(point), NormalAtPoint(point), Intersect(otherCurve), PointAtParameter(t), ParameterAtPoint(point), SetBounds(startParam, endParam). All coordinate results are VXYZ. PointAtParameter() takes a normalized 0-1 position; ParameterAtPoint() is its inverse for the closest point on the curve. SetBounds() trims a curve in place and throws NotSupportedException on VCircle, VPolygon, VRay and VXLine, whose trimmed form would be a different shape type." },
                { "Canvas", "The drawing surface as your code sees it. Shapes register themselves when you construct them, so most sketches never touch this - it exists for the case that had no answer before: a callback that redraws, and therefore has to take the previous frame's shapes back off. Canvas.Clear() removes every shape; Canvas.Remove(a, b) or Canvas.Remove(list) removes the ones you name, skipping nulls and shapes that are not on the canvas. Both are geometry only: they do NOT rewind shape ids, stop a running timeline, reset the view or undo the viewport layout, because none of that is implied by \"clear the canvas\" and firing it from inside a mouse handler would be a nasty surprise. The Viewports grid in particular survives Canvas.Clear() — it is reset once per run, not once per clear, so a handler that wipes and redraws keeps its cells. Note that Frame.Clear() is NOT this - it drops queued per-frame callbacks and leaves the drawing untouched. Both are null-safe with no canvas attached, so they work in a unit test. One name clash to know about: DoodleSharp.Canvas is also a NAMESPACE, and in a file that says \"using DoodleSharp.Canvas;\" the namespace wins, so a bare Canvas.Clear() there will not compile — the project templates never import it, but the SnapEngine, SnapType, SnapResult and DrawingTool samples do, so write C2VGeometry.Canvas.Clear() in such a file. The clash runs the other way too: a project, class, field or local OF YOUR OWN called Canvas hides this type for the whole of that scope, because C# searches the enclosing declaration before any using. DoodleSharp reports that on your declaration, reading \"Canvas is a keyword. try another name\"; a new project named Canvas is given the namespace CanvasProject automatically, so only an existing project or a hand-written declaration can hit it." },
                { "IShapeRegistry", "The hook that connects the geometry library to a canvas. Shape.DefaultRegistry holds the active implementation; when it is non-null and Shape.AutoRegister is true, every shape constructor calls Register(this) — which is why shapes appear without any explicit call. Five members: Register(shape), Unregister(shape) (what Shape.Remove() calls), Clear() (remove EVERY shape — what C2VGeometry.Canvas.Clear() calls), NotifyOrderChanged(shape) (what assigning Shape.ZIndex calls, so the host knows its draw order is stale), and Place(shape, viewport) (what shape.Place(viewport) calls, which registers the shape if it is not already and moves it onto that cell of the viewport grid). NotifyOrderChanged replaced a MoveAbove/MoveBehind pair that reordered the host's list directly. Clear() is geometry only: it must not rewind shape ids, stop a timeline or touch anything else in the host's run lifecycle, so DoodleSharp implements it EXPLICITLY and keeps a separate CanvasRenderer.Clear() for the between-runs reset that does all of that. DoodleSharp supplies CanvasRenderer as the implementation. You rarely implement this yourself — it exists so C2VGeometry stays free of any UI dependency." },
                { "IGlyphOutlineProvider", "Supplies vector outlines for the characters of a VText. C2VGeometry has no font engine of its own, so the host application implements this and assigns it to VText.GlyphOutlineProvider at startup (the same injection pattern as Shape.DefaultRegistry). Single member: GetCharContours(text, charIndex) returning List<List<VXYZ>>? — one inner list per closed contour, in world coordinates that match where the character is rendered (honouring font, height, anchor and rotation), or null for whitespace. With no provider set, VText.ToCharShape/LiftChar/LiftChars all return null." },
                { "ControlPoint", "One draggable handle exposed by a shape for interactive editing on the canvas. Returned by Shape.GetControlPoints() and consumed by Shape.MoveControlPoint(index, newPosition). Read-only Type (ControlPointType) and Label; settable X and Y; ToVXYZ() converts the position to a VXYZ. Constructor: new ControlPoint(type, x, y, label = \"\"). Index 0 is by convention the whole-shape Move handle." },
                { "ControlPointType", "The role of a ControlPoint: Move (drag the whole shape), Vertex (an endpoint or polygon vertex), Radius (resize a circle or arc), Rotation, or CurveControl (a Bezier/spline handle)." },
                { "GeometryTolerance", "Static class holding the library's floating-point tolerances and the comparison helpers built on them. Constants: Epsilon (1e-9, the general comparison tolerance and the default for VXYZ equality), VisualEpsilon (1e-6, for on-screen coincidence), AngleEpsilon (1e-5 radians). Helpers, all taking an optional epsilon: AreEqual, IsZero, IsLessThan, IsGreaterThan, IsLessOrEqual, IsGreaterOrEqual, IsInRange, PointsAreEqual, VectorsAreEqual, AnglesAreEqual, Sign. Plus NormalizeAngle (radians into [0, 2π)), NormalizeAngleDegrees ([0, 360)), ClampParametric (clamp to [0,1]), Clamp, Distance / DistanceSquared, PointOnSegment, PointToLineDistance, Orientation (sign of the cross product) and AreCollinear." },
                { "IntersectionResult", "Represents the result of an intersection operation between curves. Contains Points (List<VXYZ> of crossings) and Curves (List<ICurve> of overlapping segments, produced when two curves share a stretch rather than crossing). Properties: HasIntersection (true if any intersection), IsSinglePoint (exactly one point and no curves), HasOverlap (curves share a segment), Count (total elements). Methods: Merge(other), RemoveDuplicatePoints(tolerance = 1e-6). Static builders: None, FromPoint, FromPoints, FromCurve, FromCurves. Call Intersect() on any ICurve to get one. Nothing in it is drawn — the points are plain VXYZ coordinates, so construct a VPoint from one if you want a marker. Shape.Intersect(Shape) is the lossy sibling: it materialises this into a single VPoint or a VGroup." },
                { "CurveIntersection", "Static utility class providing curve intersection algorithms. Intersect(a, b) dispatches on the pair of runtime types: Line-Line, Line-Circle, Line-Arc, Line-Ellipse, Circle-Circle, Circle-Arc and Arc-Arc have exact closed-form routines (argument order does not matter). A VRay or VXLine operand is converted once to the finite segment spanning its RenderExtent and re-dispatched, so construction lines reach those exact routines too — which also means their reach is RenderExtent (10000 by default), not infinity. Everything else falls through to IntersectGeneric, which decomposes both curves into segments; a polyline or polygon contributes its real edges, so only genuinely curved operands are approximated. Also provides IsSelfIntersecting() for detecting self-intersections, and GetSegments() for the decomposition itself. Every method returns an IntersectionResult and none of them draw anything." },
                { "CurveGeometry", "Static helper class holding the point-to-curve measurement the curve shapes share — it is what VLine, VPolyline, VPolygon, VBezier and VSpline call from their Contains and DistanceTo overrides, and it is public so you can use it on your own vertex lists. Methods: DistanceToSegment(point, a, b) — shortest distance to the segment [a, b], falling back to the distance to the point itself for a degenerate zero-length segment; DistanceToPath(point, IReadOnlyList<VXYZ> vertices, bool closed = false) — the nearest of every segment through the vertices, adding the closing edge when closed is true, and returning double.PositiveInfinity for a null or empty list; DistanceToCurve(point, ICurve curve, int samples = 96) — samples any ICurve into a polyline and measures to that, for curves with no practical closed form; IsOnStroke(distance, curveExtent) — whether a distance counts as lying on a stroke of that size, using a tolerance of max(GeometryTolerance.Epsilon, |curveExtent| × 1e-6) so the answer does not depend on the units the drawing happens to use." },
                { "GeometryDiagnostics", "Static class where the geometry library reports something you should know about but that is not exceptional — most visibly, why a BooleanOps.Union returned null. C2VGeometry has no user interface of its own, so the host application plugs a sink into it at startup (the same injection pattern as Shape.DefaultRegistry and VText.GlyphOutlineProvider); DoodleSharp routes it to the console panel, where the messages appear tagged \"Geometry\". Members: Sink (Action<string>?, null by default — a null sink discards messages, so a library consumer with no console pays nothing) and Report(string message), which forwards to the sink and never throws (an exception from a broken sink is swallowed rather than breaking the geometry operation). Set Sink yourself to capture the messages, for instance into a List<string> for assertion or logging." },

                // Shapes
                { "VArc", "Represents a 2D arc defined by a center point (VXYZ), radius, start angle, and end angle (in degrees, counter-clockwise from the positive X axis). THE SWEEP DIRECTION IS THE SIGN OF EndAngle - StartAngle, and nothing normalises the pair: 0 to 90 is a counter-clockwise quarter, 90 to 0 is a CLOCKWISE quarter, and 0 to 450 is a full turn and a quarter. GetLength uses the absolute difference, and Evaluate(t) walks linearly between the two, so a negative sweep parameterises backwards rather than the long way round. Also constructible through three points, and via ten static factories (FromStartCenterEnd, FromCenterStartEnd, FromStartCenterAngle, FromCenterStartAngle, FromStartCenterLength, FromCenterStartLength, FromStartEndRadius, FromStartEndAngle, Continue). Default stroke color is Orange. DistanceTo(point) is computed exactly (not by sampling) and honours the sweep: a point outside the swept sector measures to the nearer endpoint, not to the full circle; a point at the centre returns Radius. Contains(point) means \"lies on the arc\". GetBounds() is the box of the ARC, not of its circle: the endpoints widened only by the compass extremes the sweep reaches. Rotate(pivot, degrees) shifts both ends by the same amount and leaves the sweep alone; Flip(mirrorLine) mirrors about the line you pass, at any angle, and swaps the ends so the copy travels the other way. Anything that has to ask \"does this arc reach that angle?\" — bounds, hit testing, ray casting, SplitAtPoint, ParameterAtPoint — goes through GeometryHelper.SweepContains/SweepOffset, which honour the direction and sweeps written past the wrap. Implements ICurve, so Divide/Measure/Project/Offset/SplitAtPoint/SetBounds all apply." },
                { "VCircle", "Represents a 2D circle defined by a center point (VXYZ) and a radius. Constructors: (center, radius), (centerX, centerY, radius), and (p1, p2, p3) for the circumcircle through three points — which throws ArgumentException when the points are collinear. Static factories: FromCenterDiameter(center, diameter), FromCenterDiameter(cx, cy, diameter), FromTwoPoints(p1, p2) where the two points are the ends of a diameter. Computed properties: Area (πr²), Circumference (2πr). Default stroke color is Yellow. Implements ICurve; the parameter domain runs counter-clockwise from angle 0 (the point at (Center.X + Radius, Center.Y)), and SetBounds throws NotSupportedException because a trimmed circle is an arc." },
                { "VRectangle", "Represents a 2D rectangle defined by a corner point (bottom-left), width, and height. Inherits from VPolygon, so all polygon members (Points, Area, SignedArea, Slice, Offset, boolean ops) are available. Constructors: (VXYZ corner, width, height), (x, y, width, height), (VXYZ bottomLeft, VXYZ topRight). Setting Corner, Width, Height or RotationAngle rebuilds the four corner points in place. RotationAngle OVERRIDES Shape.RotationAngle (it no longer shadows it with `new`): there is one property, so it means the same thing whether you reach the rectangle through a VRectangle or a Shape variable, and RotateAnimation on a rectangle works — the animation's writes rebuild the corners. Rotation is in degrees counter-clockwise about the rectangle's own centre. Rotate(pivot, degrees) and Flip(mirrorLine) both transform the CENTRE rather than Corner — Corner is the unrotated bottom-left, an artefact of the parameterisation rather than a point on the shape — and Flip mirrors RotationAngle too, so a turned rectangle comes back as its mirror image. Negative Width/Height are allowed and simply mirror the rectangle. Contains(point) is an exact interior test that honours the rotation; DistanceTo(point) is inherited from VPolygon and measures to the boundary. Default stroke color is Magenta." },
                { "VPolygon", "Represents a closed 2D polygon defined by a list of VXYZ vertices. The closing edge from the last point back to the first is implicit — do not repeat the first point. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>), and (List<ICurve> curves) which orders open curves into one continuous closed loop and throws ArgumentException on a closed curve, a gap, a branch, or a self-intersection. Properties: Points (mutable list), Curves (the internal edge representation, non-registering VLines), Area (shoelace, always positive), SignedArea (positive for counter-clockwise winding, negative for clockwise), SelfIntersecting (computed once at construction). Methods: AddPoint(point), AddPoint(x, y), and Slice(linePoint1, linePoint2) which cuts the polygon along the infinite line through two points and returns List<VPolygon> (there is no Slice overload taking a VXLine or VRay — pass VXLine.GetTwoPoints() or a ray's Origin and GetPointAtDistance instead). Slice is area-preserving: the pieces sum back to Area, and a concave polygon crossed more than twice comes back as three or more pieces, so never assume exactly two. A line that misses, or merely grazes a vertex or an edge, returns a single piece copying the original, and the pieces inherit the source's styling. Contains(point) is a genuine interior test (even-odd ray cast), not a bounding-box check; DistanceTo(point) measures to the BOUNDARY, so it is zero on an edge and positive both inside and outside — it is not a signed depth. Default stroke color is LightBlue. Implements ICurve; SetBounds throws NotSupportedException because a trimmed polygon is a polyline." },
                { "VPolyline", "Represents an open sequence of connected line segments through a list of VXYZ points. Unlike VPolygon it does not close automatically — repeat the first point as the last to close it manually. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>). Properties: Points (mutable), SelfIntersecting. Methods: AddPoint(point), AddPoint(x, y). DistanceTo(point) is the exact distance to the nearest segment (no closing edge is added — a closed polyline repeats its first point as the last, so the closing segment is already in the list); Contains(point) means \"lies on the path\". Implements ICurve; parameterisation is arc-length based across all segments, and SetBounds trims the point list in place." },
                { "VLine", "Represents a straight line segment between two points. The most basic geometric primitive. Endpoints are the settable VXYZ properties Start and End — there are no StartPoint/EndPoint properties on a concrete VLine (those exist only as explicit ICurve implementations, so generic ICurve code still works). Constructors: (VXYZ start, VXYZ end), (x1, y1, x2, y2), (VXYZ startPoint, angleInDegrees, length). Properties: Start, End, MidPoint, Direction (unit vector), Vertices, SelfIntersecting (always false). DistanceTo(point) is the exact point-to-segment distance, clamped to the endpoints so a point beyond the end measures to that endpoint rather than to the infinite line. Contains(point) means \"lies on the segment\" — a line encloses no area — judged with a tolerance scaled to the line's own length." },
                { "VXLine", "Represents an infinite construction line (like AutoCAD's XLine). Extends infinitely in both directions through a base point along a direction. Useful for construction geometry and slicing polygons. Constructors: new VXLine(VXYZ basePoint, VXYZ direction) — the second argument is a DIRECTION, not a second point — and new VXLine(x1, y1, x2, y2), which is the through-two-points form. Watch that distinction: passing a second point to the two-VXYZ overload compiles and silently builds a differently-aimed line; write new VXLine(p1, p2 - p1) if you hold two VXYZ. Static helpers: Horizontal(y), Vertical(x). Its point property is BasePoint (VRay's is Origin). DistanceTo(point) is the perpendicular distance to the infinite line — nothing is clamped, because the line has no ends; Contains(point) is true anywhere on it. Intersect(other), however, is bounded: the line is converted to the finite segment from -RenderExtent to +RenderExtent about BasePoint (10000 each way by default) and tested with the exact routines, so anything further out is missed until you raise it. GetBounds() comes from the same span, so the box is finite even though GetLength() is infinite." },
                { "VRay", "Represents a semi-infinite ray (like AutoCAD's Ray). Starts at an origin point and extends infinitely in one direction. Constructors: new VRay(VXYZ origin, VXYZ direction) — the second argument is a DIRECTION, not a point the ray passes through — and new VRay(originX, originY, throughX, throughY), which IS the through-point form. Watch that distinction: passing a target point to the two-VXYZ overload compiles and silently aims the ray elsewhere; write new VRay(origin, target - origin) if you hold two VXYZ. Static helpers: HorizontalRight, HorizontalLeft, VerticalUp, VerticalDown, AtAngle(origin, angleDegrees). Its point property is Origin (VXLine's is BasePoint); RenderExtent (default 10000) is how far it is actually drawn and what its bounds are computed from, since the ray itself has no end. Also: GetPointAtDistance(d), ContainsPoint(p), ToFiniteLine() and ToXLine(). The last two return a real VLine/VXLine you can measure and intersect, but it is deliberately NOT drawn — converting a ray for a calculation should not add a second line to the drawing. Call .Place() on the result if you do want to see it (VXLine.ToFiniteLine() behaves the same way). DistanceTo(point) is perpendicular where the point projects onto the ray and measured to Origin for anything behind the start; Contains(point) is false behind the origin. Intersect(other) reaches exactly as far as RenderExtent too — the ray is converted to that finite span and tested with the exact closed-form routines, so an obstacle further out than 10000 is missed until you raise it. GetBounds() is taken from the same span, so the box is finite even though GetLength() is infinite." },
                { "VEllipse", "Represents a 2D ellipse defined by a center point (VXYZ), X radius (horizontal) and Y radius (vertical). Constructors: (center, radiusX, radiusY), (centerX, centerY, radiusX, radiusY), and (center, radiusX, radiusY, startAngle, endAngle) for an elliptical arc — angles in degrees, defaults 0 and 360. Rotation (degrees CCW, default 0) is the orientation of the ellipse itself — the direction its RadiusX axis points — and there is no constructor for it, so set it in an initializer: new VEllipse(new VXYZ(0, 0), 80, 40) { Rotation = 30 }. StartAngle and EndAngle are measured in the ellipse's OWN frame, so turning a half ellipse turns the half with it rather than re-cutting a different half, and PointAtAngle(deg) is the world point at an angle in that frame. Rotate(pivot, degrees) writes Rotation as well as moving the centre, so it genuinely turns the shape; GetBounds() is exact for a partial sweep and for a turned one. Computed properties: Area (π·rx·ry), Circumference (Ramanujan approximation; exact only for a circle). Implements ICurve and is ARC-LENGTH parameterised like every other curve: Evaluate(t) and PointAtParameter(t) walk the parameter along the length of the curve, so Divide(n) returns evenly spaced points and SetBounds(s, e) trims to that stretch of curve rather than that stretch of sweep angle. EvaluateByAngle(t) gives the angle-linear reading instead (t interpolated from StartAngle to EndAngle) — use it for radial spokes and sector boundaries. On a circle the two agree; they diverge as the ellipse becomes more eccentric. Contains(point) is an exact interior test for a FULL ellipse and an on-the-curve test for a partial sweep (which encloses no area); DistanceTo(point) is the sampled distance to the curve and honours the sweep." },
                { "VPoint", "Represents a visible point marker on the canvas — a drawn dot, not a coordinate. For coordinates and vectors use VXYZ; constructing a VPoint auto-registers a shape. Constructors: (x, y) and (VXYZ position). X and Y are settable. Converts to VXYZ implicitly, or explicitly via AsVXYZ(). Full arithmetic operator set (+, -, *, /) against VPoint, VXYZ and scalars — every overload returns a plain VXYZ so intermediates never pollute the canvas. Default Color and FillColor are both White, and unusually they are assigned OUTRIGHT rather than through ShapeDefaults: VPoint is the one shape that does NOT honour ShapeDefaults.GlobalColor / GlobalFillColor, so set point.Color yourself if you are styling globally." },
                { "VBezier", "Represents a 2D cubic Bezier curve defined by four VXYZ control points: P0 (start), P1 and P2 (control handles), P3 (end). Constructors: (p0, p1, p2, p3) and (x0, y0, x1, y1, x2, y2, x3, y3). The Segments property (default 32) controls how finely the curve is tessellated for rendering and for length/parameter queries. Evaluate(t) gives the exact point at the Bernstein parameter t. DistanceTo(point) is the shortest distance to the curve, found by sampling it (96 samples), and Contains(point) means \"lies on the curve\". Implements ICurve; SetBounds performs an exact De Casteljau trim in place." },
                { "VSpline", "Represents a smooth Catmull-Rom spline passing through every one of its control points. Constructors: (params VXYZ[]), (IEnumerable<VXYZ>). Properties: ControlPoints, SegmentsPerSpan (default 16 — tessellation density between adjacent control points), Tension (default 0.5; 0 is angular, 1 is loose). DistanceTo(point) is the shortest distance to the curve, found by sampling it (96 samples), and Contains(point) means \"lies on the curve\". Implements ICurve; SetBounds resamples the trimmed range rather than dropping control points, because Catmull-Rom tangents depend on the neighbouring points." },
                { "VText", "Represents text drawn at a specific position. Supports font size via Height property or constructor parameter. Constructors: VText(point, text), VText(point, text, height), VText(x, y, text), VText(x, y, text, height). Supports Font, FontWeight, Anchor, Justify and Angle properties for styling, alignment, and rotation. Anchor places the whole text block against Location; Justify (VTextJustify) lines the rows of a MULTI-LINE label up with each other inside that block and does nothing to single-line text — the two compose. Content may be multi-line: lines come from newline characters (there is no wrapping), and GetBounds() measures the block — the widest line by the stacked height of them all, gaps counted at 1.2 x Height — which for VText is also the hit test, since a glyph run has no other outline. Mask draws a solid rectangle behind the glyphs so a label stays readable where it crosses other geometry; it is ON by default, with MaskColor defaulting to null, which means \"the canvas background\", and MaskOffset (padding as a fraction of the text height, 0 to 1, default 0.15). Static CanvasBackgroundColor is the host-published colour a null MaskColor resolves against away from a canvas. Individual characters can be converted to vector outline shapes: ToCharShape(i) (non-mutating), LiftChar(i) and the indexer text[i] (extract the glyph as a shape AND replace the character with a space), and LiftChars(start, count) for a selection. These let you morph a letter into another shape, e.g. new TransformAnimation(text[0], circle, 2)." },
                { "VTextAnchor", "Enum specifying the anchor (alignment) point for VText. Values: BottomLeft (default), BottomCenter, BottomRight, MiddleLeft, MiddleCenter, MiddleRight, TopLeft, TopCenter, TopRight. Controls which point of the text bounding box is placed at the text's position." },
                { "VTextJustify", "Enum controlling how the LINES OF A MULTI-LINE VText line up with each other inside the text block. Values: Left (default), Center, Right. Lines come from newline characters (\\n) in VText.Content. This is NOT VTextAnchor, and the two compose rather than compete: the anchor decides where the block as a whole sits against the text's Location, the justification decides what the ragged edge inside the block looks like once it is there. A four-line label with Anchor = MiddleCenter is centred on its point either way; adding Justify = Center also centres its short lines against its long ones instead of letting them hang off to the left. It has no visible effect on single-line text, where the block is exactly as wide as its one line, and it never moves or resizes the block — GetBounds() returns the same box for all three values. EXPORT: SVG and PDF lay the lines out and honour Justify (and Anchor), so a justified multi-line label survives the trip; DXF keeps the line breaks (one TEXT entity per line, stacked 1.2 x Height apart) but starts every line at the same point, because R12 TEXT has no block width to justify inside." },
                { "VGroup", "Represents a collection of shapes treated as a single unit. Supports multiple constructors (empty, params, IEnumerable, List), group transformations (Move, Rotate, Scale, Flip), style application (ApplyStyle, ApplyColor, ApplyFillColor), and utility methods (Flatten, ForEach, Where, GetShapesOfType). When drawn, the group is rendered and selected as a single entity on the canvas." },
                { "VGrid", "Represents a rectangular grid of VPoint markers. Constructors: VGrid(location, xcount, ycount, xSpacing = 1.0, ySpacing = null, centered = true) — ySpacing is double? and null means \"same as xSpacing\", so VGrid(loc, 5, 5, 10) is a square grid with spacing 10 on both axes; VGrid(location, xcount, ycount, spacing, centered) for uniform spacing with an explicit centered (it deliberately has no default, which is what keeps the four-argument call unambiguous); VGrid(location, xcount, ycount, centered) for spacing 1.0. If centered=true, grid is centered at location; if false, location is bottom-left corner. Access points via Points property, indexers [index] or [col, row], or GetRow()/GetColumn() methods. Supports all Shape transformations (Move, Rotate, Scale, Flip) and ApplyStyle() to set colors on all points." },
                { "VCell", "Represents a square cell with a VPolygon boundary. Extends VPolygon. Properties: UniqueId (int), Neighbours (List<VCell>), Center (VXYZ), CellSize (double), Column (int), Row (int), Blocked (bool). Used as a building block for VSpatialGrid. Neighbours are set by the parent grid (4-connectivity: left, right, below, above)." },
                { "VSpatialGrid", "Represents a grid of square VCell instances with neighbour connectivity and A* pathfinding. Constructor: VSpatialGrid(location, xCount, yCount, cellSize). Location is the center of the bottom-left cell. Each cell knows its adjacent neighbours (4-connectivity). Access cells via Cells property, indexers [index] or [col, row], or GetRow()/GetColumn(). Use FindPath(start, end) for A* shortest path, GetClosestCell(point) for O(log n) nearest-cell lookup via KD-tree." },
                { "VArrow", "Represents an arrow: a straight shaft from Start to End with a V-shaped head. Constructors: (VXYZ start, VXYZ end), (x1, y1, x2, y2), (VXYZ startPoint, VXYZ direction, double length). Properties: Start, End (settable VXYZ — there are no StartPoint/EndPoint aliases), MidPoint, HeadLength (default 15 world units — the length of each wing), HeadAngle (default 30 degrees — half-angle of each wing off the shaft, so a 60-degree head), DoubleEnded (default false; when true a head is drawn at Start as well). GetEndArrowhead() and GetStartArrowhead() return the two wing tip coordinates; GetArrowheadPoints(tip, from) computes them for an arbitrary tip and shaft direction, and the static VArrow.ArrowheadWings(tip, from, headLength, headAngleDegrees) does the same for a caller supplying its own size — the dimension shapes use it, at their ArrowSize and VDimension.DimensionArrowAngleDegrees. That one method is where every arrowhead in the application comes from, so the head's shape and size are identical on the vector, raster and GPU backends and in every export — only its fill still differs (solid under the vector renderer, outlined under raster and GPU; see HeadAngle). VArrow is a plain Shape, not an ICurve." },
                { "RayCaster", "Accelerated 2D ray-casting against an explicit collection of shapes. Constructor `new RayCaster(IEnumerable<Shape> shapes, int leafSize = 8)` — you pass the shapes to index; there is no canvas-snapshot constructor (the geometry library has no canvas). To cast against everything currently drawn, pass `CanvasRenderer.Instance.GetShapes().OfType<Shape>()` (add `using DoodleSharp.Canvas;` and `using System.Linq;`). It builds an axis-aligned BVH with Surface Area Heuristic splitting, so each subsequent ray query runs in O(log N) average time and scales to millions of shapes. Only shapes with IsVisible == true are indexed; VPoint markers are always excluded (zero area, not a useful ray target), as are shapes with null or non-finite bounds. VRay and VXLine are excluded too, by an explicit type test rather than by the bounds filter — both report a FINITE box derived from RenderExtent, so the filter does not catch them, and because neither is among the exactly-tested types a hit on one was a hit on its bounding box, which for a diagonal guide can be nowhere near the line and still beat the real geometry behind it to the nearest-hit answer. To find where a ray truly crosses a construction guide, intersect them pairwise: ray.Intersect(other). The collection is snapshotted at construction — shapes added or removed afterwards are not reflected, but Refit() refreshes cached AABBs in O(N) when indexed shapes move. Query methods: FindIntersection(location, direction, exclusionList = null) returns RayHit? for the closest hit, with an optional List<Shape> of shapes to skip (useful for casting off a known source shape or finding the next hit past a set of shapes); FindIntersection(location, direction, maxDistance, exclusionList = null) also caps the search distance and prunes BVH sub-trees beyond the cap; HasIntersection(location, direction, maxDistance) returns true on the first hit (faster shadow-ray query); FindIntersections(queries, parallel = true) batches over IReadOnlyList<RayQuery>. Queries run on the XY plane (Z ignored); direction need not be normalised. Inline ray-vs-shape math handles VLine, VCircle, VArc, VEllipse, VPolygon (and VRectangle), VPolyline with zero allocation; other shape types fall back to AABB hit. Partial arcs and ellipses are tested against their real sweep through GeometryHelper.SweepContains, so a clockwise sweep and one written past the wrap are both read correctly, and an ellipse's Rotation is honoured (the ray is taken into the ellipse's own frame and the hit brought back out). Queries are thread-safe after construction." },
                { "RayHit", "Readonly record struct returned by RayCaster.FindIntersection. Fields: Shape (the hit shape), Point (VXYZ world-space hit location), Distance (Euclidean distance from ray origin to the hit point)." },
                { "RayQuery", "Readonly record struct used by RayCaster.FindIntersections to describe a single ray. Fields: Origin (VXYZ), Direction (VXYZ, need not be normalised)." },
                { "VDimension", "Represents a dimension line showing the distance between two points with text annotation. AutoCAD-style properties: Offset, ArrowSize, TextHeight, DecimalPlaces, ExtendBeyondDimLines, OffsetFromOrigin, SuppressExtLine1/2, SuppressDimensionLine, Prefix, Suffix, TextBackgroundOpaque. Per-element colors: ExtensionLineColor, DimensionLineColor, TextColor (null = use base Color). The dimension line is always split around the text for readability. Renders arrowheads at both ends of the dimension line." },
                { "VRadialDimension", "Represents a radial or diameter dimension for circles and arcs. Draws a leader line from center to circumference with an arrowhead and text label (R for radius, \u2300 for diameter). Constructors: VRadialDimension(circle), VRadialDimension(arc), VRadialDimension(center, radius). Properties: LeaderAngle (direction of leader), ShowDiameter (diameter mode), ArrowSize, TextHeight, DecimalPlaces, Prefix, Suffix, CustomText, TextBackgroundOpaque. Per-element colors: DimensionLineColor, TextColor." },

                // Support classes
                { "VXYZ", "3D coordinate type (X, Y, Z) used for every position, vector and direction parameter in the library — the counterpart to Revit's XYZ. Its components are read-only: every operation returns a new instance, so a VXYZ can be shared without aliasing bugs. Constructors: (x, y, z), (x, y) with Z = 0, and () for the origin. Never registers on the canvas — use it freely for intermediate maths, and reach for VPoint only when you want a dot drawn. Vector operations: Add, Subtract, Multiply, Divide, Negate, Normalize (returns Zero for a zero-length vector rather than throwing), GetLength, DistanceTo, DotProduct, CrossProduct, TripleProduct, AngleToDegrees (unsigned, 0 to 180 - the library's convention) and AngleToRadians (0 to π), Rotate(degrees) about the Z axis, Clone, AsVPoint. The unit-less AngleTo is OBSOLETE: it returns radians, which is the one place this library does not work in degrees, and handing its answer to a degrees-taking API is silently wrong rather than obviously wrong. Tests: IsZeroLength, IsUnitLength, IsAlmostEqualTo(other, tolerance = 1e-9), static IsWithinLengthLimits. Indexer [0]/[1]/[2] reads X/Y/Z and throws IndexOutOfRangeException otherwise. Operators +, -, * and / work with scalars and with VPoint (mixed operations return a plain VXYZ, never a drawable point); == and != are fuzzy comparisons using IsAlmostEqualTo, so GetHashCode rounds to 8 decimals to match. Static properties: Zero, BasisX, BasisY, BasisZ." },
                { "VFont", "Font family for VText. Values: Arial (default), TimesNewRoman, CourierNew, Verdana, Georgia, Tahoma, TrebuchetMS, Consolas, Calibri, Cambria, SegoeUI, ComicSansMS, Impact, LucidaConsole." },
                { "VFontWeight", "Font weight for VText: Normal (default) or Bold." },
                { "VPlane", "An infinite plane in 3D, used as the mirror for VTransform.CreateReflection and as the source for VCoordinateSystem.ByPlane. It has no public constructor — build one with the static factories CreateByNormalAndOrigin(normal, origin), CreateByOriginAndBasis(origin, xVec, yVec) or CreateByThreePoints(p1, p2, p3). Read-only properties: Origin, Normal, XVec, YVec — all four are normalised on construction, and the two basis vectors are derived automatically when you supply only a normal. There is no ProjectPoint or DistanceTo on VPlane; project a point yourself with VCoordinateSystem.ByPlane(plane).ToLocal(point), whose Z component is the signed distance to the plane. The drawing canvas is the XY plane, so VPlane matters only for 3D vector maths — nothing on it renders." },
                { "VTransform", "An affine 3D transform stored as three basis vectors plus an origin (not a 4x4 matrix — there is no Matrix property). Members: BasisX, BasisY, BasisZ, Origin (all settable VXYZ), the static Identity, the static factories CreateRotationDegrees(axis, angleDegrees), CreateRotationRadians(axis, angleRadians) and CreateReflection(plane), and the two application methods OfPoint(point) (applies the basis AND the origin translation) and OfVector(vec) (basis only, translation ignored). There is no Multiply, Inverse or CreateTranslation — compose by hand, or set Origin directly for a translation. Rotation is the one place this type departs from the library's degrees convention, so there are two explicitly-named factories: CreateRotationDegrees(axis, 90) matches Shape.Rotate, VXYZ.Rotate, VCoordinateSystem.Rotate and GeometryHelper.RotatePoint, and is the one to prefer; CreateRotationRadians(axis, Math.PI / 2) is for when you already hold radians. The original name, CreateRotation, is the radians overload and is now [Obsolete] — it compiles and behaves exactly as before, but it never said which unit it took. Nothing here registers on the canvas." },
                { "VCoordinateSystem", "An origin plus three orthonormal axes, for converting between world coordinates and a local frame — Dynamo-style, so it is built through factories rather than a constructor: ByOrigin(origin), ByOrigin(x, y, z), ByOriginVectors(origin, x, y, z), ByOriginXY(origin, x, y) (Z from the cross product, Y re-orthogonalised), ByOriginZAxis(origin, z) (X and Y chosen arbitrarily but consistently), ByPlane(plane), and the static Identity. Read-only properties: Origin, XAxis, YAxis, ZAxis — the axis names are XAxis/YAxis/ZAxis, not BasisX/BasisY/BasisZ (those are VTransform's). Methods: ToLocal(worldPoint) and ToWorld(localPoint) / ToWorld(x, y, z) convert in both directions; Translate(vector) and Rotate(axis, angleDegrees) each return a NEW system, leaving this one unchanged. Rotate takes DEGREES, like every other rotation in the library — Rotate(VXYZ.BasisZ, 90) is a genuine quarter turn, and agrees with VXYZ.Rotate(90). Purely computational: nothing draws." },
                { "GeometryHelper", "Static point-and-shape maths used by the shapes themselves and available to you. Point transforms, all returning a plain VXYZ: RotatePoint(point, pivot, angleDegrees), FlipPoint(point, mirrorLine), MovePoint(point, vector), ScalePoint(point, center, factor). Angles in degrees: NormalizeAngle(deg) folds into [0, 360); AngleDifference(target, source) gives the smallest signed turn in [-180, 180], which is what you want for shortest-path rotation. Sweeps, also in degrees: SweepContains(start, end, angle) says whether the sweep from start to end passes through angle, and SweepOffset(start, end, angle) says how far along that sweep the angle lies — signed, so negative on a clockwise sweep, and clamped to the sweep. Both work on the OFFSET from the start rather than on normalised absolute angles, so they honour the direction of travel (90 to 0 is a clockwise quarter, not a three-quarter turn) and sweeps written past the wrap (350 to 370 is a 20-degree sweep, not a 340-degree one). VArc, VEllipse and RayCaster all defer to these two, so a sweep test you write yourself agrees with what the shapes draw. Analysis: IntersectCircleCircle(c1, r1, c2, r2) returns a List<VXYZ> of 0, 1 (tangent) or 2 points; GetPolylineNormalAtPoint(points, p, isClosed) returns the unit normal of the segment nearest p. The three Intersect* methods return Shape? — IntersectLineLine(l1, l2), IntersectLineRect(line, rect) and IntersectRectRect(r1, r2) — because the answer carries its own type: a crossing is a VPoint, a collinear overlap is a VLine, a rectangle overlap is a VRectangle. That shape is NOT drawn: asking where two lines meet should not add anything to the canvas. Read the coordinates off the result and let it go, or call .Place() on it if you want it placed. IntersectLineLine returns a VPoint for a crossing, a VLine for a collinear overlap, or null; IntersectLineRect returns a VPoint when the line only grazes a corner; the two rectangle methods assume axis-aligned rectangles. When you would rather have plain coordinates than a shape, use curve.Intersect(other) (see CurveIntersection), which returns an IntersectionResult of VXYZ points." },
                { "DoubleExtensions", "Two extension methods on double, ToRadians() and ToDegrees(), for the boundary between this library and System.Math. Every angle in C2VGeometry is in DEGREES — Shape.Rotate, VXYZ.Rotate, VCoordinateSystem.Rotate, GeometryHelper.RotatePoint, and the VArc/VEllipse angle properties — while System.Math works in RADIANS. These exist so that crossing is written as what it is (30.0.ToRadians()) rather than an unexplained * Math.PI / 180.0. Use them only at that boundary: an angle you hand to a shape is already in the units it wants, and needs no conversion. They are plain arithmetic — no clamping or normalisation, so a value outside [0, 360) converts literally; fold it first with GeometryHelper.NormalizeAngle if that matters. Available wherever you have `using C2VGeometry;`. The one library API that takes radians, VTransform.CreateRotationRadians, reads well as CreateRotationRadians(axis, 90.0.ToRadians()) — though VTransform.CreateRotationDegrees(axis, 90) is the more direct answer there." },
                { "ShapeDefaults", "Static class holding the global style defaults applied to every shape as it is constructed. Each property is nullable and null means \"leave the shape's own default alone\": GlobalColor, GlobalFillColor, GlobalLineWeight, GlobalLineType, GlobalLineTypeScale. One exception to know about: VPoint assigns Color and FillColor to \"White\" outright, so GlobalColor and GlobalFillColor do NOT reach a VPoint; every other shape honours them. Dimension defaults: DimOffset, DimArrowSize, DimTextHeight, DimDecimalPlaces, DimExtendBeyondDimLines, DimOffsetFromOrigin, DimPrefix, DimSuffix, DimTextBgOpaque, DimExtensionLineColor, DimDimensionLineColor, DimTextColor, DimSuppressDimensionLine. Reset() sets them all back to null. Setting a default affects only shapes created afterwards. These values are also populated from Project Settings." },
                { "LineType", "Enum defining the stroke style (line pattern) for shape outlines. Eight values: Continuous (solid, default), Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden. The dash and gap lengths are defined once, in DEVICE PIXELS, by C2VGeometry.Rendering.LineTypePatterns, and every backend and exporter draws from that one table — so a dashed line looks the same however the frame was rendered. shape.LineTypeScale multiplies those lengths. Two consequences: dash length does NOT change with zoom (it is a fixed on-screen size, unlike AutoCAD's LTSCALE, which is in drawing units), and it does NOT change with LineWeight — a hairline and a heavy line of the same type dash identically." },
                { "VColor", "Static class of colour STRINGS — every member returns the string that Color and FillColor expect, not a colour object, so shape.Color = VColor.Tomato is the same as shape.Color = \"Tomato\". It exposes 82 named colours as read-only properties: Red, Green, Blue, Yellow, Orange, Purple, Pink, Cyan, Magenta, White, Black, Gray, Brown, Coral, Crimson, DarkBlue, DarkGreen, DarkRed, DarkOrange, DarkViolet, DeepPink, DeepSkyBlue, DodgerBlue, ForestGreen, Fuchsia, Gold, GreenYellow, HotPink, IndianRed, Indigo, Khaki, Lavender, LawnGreen, LightBlue, LightCoral, LightGreen, LightPink, LightSalmon, LightSeaGreen, LightSkyBlue, LightYellow, Lime, LimeGreen, Maroon, MediumBlue, MediumOrchid, MediumPurple, MediumSeaGreen, MediumSlateBlue, MediumSpringGreen, MediumTurquoise, MediumVioletRed, MidnightBlue, Navy, Olive, OliveDrab, OrangeRed, Orchid, PaleGreen, PaleTurquoise, PaleVioletRed, Peru, Plum, RoyalBlue, Salmon, SandyBrown, SeaGreen, Sienna, Silver, SkyBlue, SlateBlue, SlateGray, SpringGreen, SteelBlue, Tan, Teal, Thistle, Tomato, Turquoise, Violet, Wheat, YellowGreen. Construction: FromRgb(r, g, b) and FromArgb(a, r, g, b) return hex strings, WithOpacity(r, g, b, opacity) takes opacity as 0.0-1.0 and returns #AARRGGBB, FromEnum(ColorName) converts the enum. Randomisation: GetRandomColor(returnPastelColor = true), GetRandomPastelColor(), GetRandomVibrantColor(), and the palettes behind them, GetPastelColors() and GetVibrantColors(), both string[] — handy as a ChartOptions.Palette. Any WPF colour name or #RRGGBB / #AARRGGBB string works too; VColor exists so the names are discoverable and typo-proof." },
                { "ColorName", "Enum of 82 colour names, for when you want a colour as a value you can store, compare or switch on rather than as a string. Shape.Color and Shape.FillColor take STRINGS, not this enum, so convert on the way in: shape.Color = VColor.FromEnum(ColorName.Crimson). Every member's name is exactly the string it converts to, and exactly the WPF/CSS colour of the same name — so ColorName.Crimson, VColor.Crimson and the literal \"Crimson\" are three spellings of one colour, and the table below needs no per-value commentary. Rough grouping: the twelve basics (Red, Green, Blue, Yellow, Orange, Purple, Pink, Cyan, Magenta, White, Black, Gray) followed by 70 extended names in alphabetical order, Brown through YellowGreen — Dark*/Light*/Medium*/Pale* variants of the basics, plus the descriptive names (Coral, Crimson, Gold, Khaki, Salmon, Teal, Tomato, Wheat and so on). One duplicate to be aware of: Magenta and Fuchsia are two names for the same colour (#FF00FF), and both are in the list. Note this enum is NOT the full palette available to you — Color and FillColor accept any WPF colour name and any #RRGGBB or #AARRGGBB string, so the enum is a discoverability aid rather than a limit. For a colour you did not have a name for, use VColor.FromRgb, VColor.WithOpacity, or one of the random-colour helpers." },

                // Viewports - the canvas pane is a recursive grid of drawing surfaces
                { "Viewport", "One region of the drawing surface, and a node in the viewport tree. The canvas pane starts as a single undivided viewport - the one the bare name Viewports refers to - and setting Rows or Columns above 1 splits it into cells, each of which is a Viewport in its own right and can be split again, to any depth. That is what makes an uneven layout expressible: one large view beside a column of small ones is just a subdivided cell. INDICES ARE 0-BASED AND ROW FIRST, so Viewports[1][2] is the third cell of the second row. A viewport whose Rows and Columns are both 1 is a LEAF: it owns a canvas of its own, with its own pan and zoom, and shapes go on it with shape.Place(viewport). A leaf's only cell is ITSELF, which is why on the default 1x1 layout Viewports[0][0] IS the root, and why a bare Place() and Place(Viewports[0][0]) mean the same thing with no special case anywhere. Sizing uses XAML's grid-length spelling, as a string: \"*\" for a share of the space, \"3*\" for three shares, or a number for fixed device pixels. Height addresses the ROW this viewport sits in and Width addresses its COLUMN, exactly as a XAML RowDefinition is shared by the cells sitting in it - so every cell in a row reports the same Height. Reading or setting either ON THE ROOT throws InvalidOperationException: the root always fills the pane and has no parent to be sized within. Indexing past the current size throws ArgumentOutOfRangeException, with a message naming the size you actually have. Rows and Columns must be between 1 and MaxDimension (8). THE LAYOUT IS RESET TO A SINGLE UNDIVIDED VIEWPORT AT THE START OF EVERY RUN, like shape ids - so the source always says what is on screen, and deleting a Viewports.Rows line takes effect on the next F5 rather than lingering until restart. Put the layout lines in Main() and let them re-run: re-stating a value it already has changes nothing and raises nothing, so a re-run does not throw away each cell's pan and zoom. Statics: Root, Leaves(), Reset() and the LayoutChanged event. Instance: Rows, Columns, IsLeaf, Parent, Depth, RowIndex, ColumnIndex, Path, IsAttached, Height, Width, the [row] indexer, RowHeightAt(row), ColumnWidthAt(column), ResolveVisible() and FirstLeaf(). One naming note: Viewport, ViewportRow, ViewportLength and ViewportRoot are library type names and Viewports is reachable unqualified, so none of the five can be the name of your project, class, field, parameter or local - C# searches your own declarations before any using directive." },
                { "ViewportRoot", "Static holder whose single member, Viewports, is the whole drawing surface. It exists for one reason: to make Viewports usable as a BARE NAME. The intended spelling - Viewports.Rows = 2, Place(Viewports[0][1]) - needs Viewports to be an expression, and C# has no static indexers (CS0720) and no namespace-level members, so a bare type name could never be indexed; a static property can. The compiler injects `global using static C2VGeometry.ViewportRoot;` into every compilation as its own syntax tree, so a hand-written second file gets it as well as a generated one, and no character offsets shift in your own files. There is nothing to call here and no reason to name this type in your code: write Viewports." },
                { "ViewportRow", "One row of a Viewport - what Viewports[row] returns, so that Viewports[row][column] reads the way a grid index should. Two members: the [column] indexer, which gives you the cell, and Height, which sizes the WHOLE ROW in XAML's grid-length spelling (\"*\", \"3*\", \"240\"). Viewports[0].Height = \"3*\" and Viewports[0][0].Height = \"3*\" are the same act, because a height belongs to the row rather than to one cell. Indexing past the last column throws ArgumentOutOfRangeException naming the viewport's current size. A class rather than a struct, and not by preference: C# refuses to assign to a property of a value returned by an indexer (CS1612), so Viewports[1].Height = \"3*\" would not compile if this were a value type. You never construct one - index a Viewport and use the result immediately." },
                { "ViewportLength", "The parsed form of a viewport row height or column width - the value behind the Height and Width strings. \"*\" is one share of whatever is left, \"3*\" is three shares, and a plain number is a fixed size in DEVICE PIXELS; shares are relative, so \"3*\" beside \"*\" takes three quarters of the space, the same arithmetic a XAML Grid does. A readonly struct with two properties, Value (the share count when IsStar, otherwise the pixel size) and IsStar, plus value equality and the == and != operators. The constructor is private: Parse(text) and the static Star field are the only ways to get one. Parse throws ArgumentException on anything it does not recognise - an empty string, a zero or negative share, a negative pixel count, and \"Auto\", which is rejected BY NAME because a canvas has no natural size of its own, so an auto-sized viewport would collapse to nothing and look like the drawing had vanished. ToString() returns the canonical spelling, which is what Height and Width read back. Most code never names this type: set Height and Width with strings and let them parse. It is what a host reads through Viewport.RowHeightAt / ColumnWidthAt to lay the grid out." },

                // Animation
                { "DoodleSharp.Animation", "Contains the classes that make a drawing move and respond. Two motion models: Frame for per-frame callbacks that reschedule themselves (the requestAnimationFrame pattern), and Animator for a finite timeline that can be scrubbed and exported to GIF or video. Alongside them, Mouse is the input seam — JavaScript-style mouse callbacks (Mouse.OnMove, OnDown, OnClick, OnDrag, OnWheel and friends) that hand your code a MouseInfo for every gesture on the canvas, so a drawing can be interactive rather than only animated." },
                { "Frame", "Per-frame callbacks, in the shape JavaScript uses: a function that asks for the next frame. Frame.Request(callback) queues it and returns a handle; call Frame.Request again from inside the callback to keep going, and simply stop asking to end. The callback receives elapsed seconds since the loop started - write motion as a function of that rather than accumulating state and it stays frame-rate independent. Frame.Cancel(handle) removes a queued callback. Requesting during a callback runs on the NEXT frame, never the current one. Use this for open-ended, interactive or procedural motion; use Animator when you need a finite sequence you can scrub or export, which a self-rescheduling callback cannot provide." },
                { "Mouse", "Static class holding the mouse callbacks for the canvas, in the shape JavaScript uses: assign one function per event — Mouse.OnMove(e => cursor.Center = e.Position). The methods are OnMove, OnDown, OnUp, OnClick, OnDoubleClick, OnDrag, OnWheel, OnEnter and OnLeave, each taking an Action<MouseInfo> and each handing your code a MouseInfo describing the gesture. ASSIGNING REPLACES; IT DOES NOT ADD — calling OnMove twice leaves one handler, the second, and passing null detaches it. That is deliberately unlike Frame.Request, which queues each request separately: Main() is re-invoked on every tick of a Global Parameters slider drag, so an additive API would silently stack hundreds of live handlers during one drag. HANDLERS ARE DROPPED AT THE START OF EVERY RUN and by the Stop button, so register them from Main() (or a sketch's Setup()) and let them be re-registered each time you press Run; a handler is a delegate into the collectible assembly your code was compiled into and cannot outlive the run that created it. REGISTERING ANY HANDLER PUTS THE CANVAS INTO INTERACTIVE MODE: it stops competing for the mouse, so click-to-select and double-click-zoom-to-fit are suppressed and your handlers see every gesture; middle-button drag still pans, and the F4 properties panel is hidden while interactive mode lasts (it edits the selected shape, and there is no selection). THE WHEEL IS THE ONE EXCEPTION, and is not part of that bargain: the canvas goes on zooming on the wheel until you register OnWheel specifically, so a sketch that merely watches clicks or moves does not lose the main way to navigate a drawing larger than the viewport. HasWheelHandler reports that separately from HasHandlers, and Mouse.OnWheel(null) hands the wheel straight back. Zoom controls (-, +, zoom-to-fit and a live zoom percentage) fade in at the top-right of whichever viewport cell the pointer is over, in either mode, so there is a way to zoom even once your code owns the wheel. The drawing tools (P/L/C/R) and the measuring tape keep priority while armed — your handlers do not fire until you leave the tool with Esc. A project that registers nothing behaves exactly as it always has. Synthesis rules worth knowing: OnClick is manufactured from a down/up pair on the same button within about 3 pixels, so a drag produces no click; OnDoubleClick fires INSTEAD OF OnDown on the second click; and OnDrag fires INSTEAD OF OnMove while a button is held, with no fallback to OnMove. Handlers run on the UI thread, one at a time, so they can freely create and modify shapes, and the canvas repaints once per frame rather than once per event. A handler that throws detaches ALL handlers and is reported once through CallbackFailed (the console shows it tagged \"Mouse\") rather than throwing a hundred times a second. Polled state: X, Y and IsDown are tracked even with no handler registered, so a Frame callback or a sketch's Draw() can read them without registering anything. HasHandlers says whether interactive mode is on, HasWheelHandler says whether your code has taken the wheel, and Clear() detaches everything. ONE NAMING TRAP: a project's name becomes the namespace of its code, and C# searches the enclosing namespace before any using — so a project called Mouse makes this type unreachable by its short name, as does a class, field, local or parameter of your own called Mouse. DoodleSharp reports it on your own declaration rather than on the call that failed, reading \"Mouse is a keyword. try another name\"; a new project named Mouse is given the namespace MouseProject automatically." },
                { "MouseInfo", "The event object handed to every Mouse callback — the equivalent of the e in a JavaScript onmousemove(e) handler. A fresh instance is created for each dispatched event, so it is safe to keep one, stash it in a field or compare it with the next; it is deliberately not pooled or reused. Position and geometry: Position (VXYZ in world coordinates, grid-snapped while Snap to Grid (F9) is on), RawPosition (the same point never snapped), X and Y (shorthand for Position.X/Y), ScreenX and ScreenY (device-independent pixels from the canvas's top-left, Y increasing downwards), Scale (canvas zoom, screen pixels per world unit — use 8 / e.Scale for \"within 8 pixels\"). Remember the world is Y-UP with (0, 0) at the centre of the canvas, so e.Position drops straight into a shape constructor with no conversion. Buttons and modifiers: Kind (MouseEventKind — which event this is), Button (MouseButtonKind — the button this event is ABOUT, None for a move, wheel, enter or leave), LeftDown, RightDown, MiddleDown (what is held right now, which is what you want during a drag), Shift, Ctrl, Alt, ClickCount (1 single, 2 double, 0 when not a button event), WheelDelta (raw WPF units, 120 per notch) and WheelNotches (the friendly form, 1.0 per detent). Target is the topmost shape under the cursor or null over empty space, computed on first read and cached, so a handler that never asks pays nothing. Viewport is which cell of the viewport grid the pointer was in, and is the root on an undivided canvas; handlers are registered once for the WHOLE drawing rather than per cell, so this is how one handler tells the cells apart — if (e.Viewport == Viewports[0][1]) ... — and it compares by reference, a viewport keeping its identity across every resize that does not remove it. There is NO Handled property — the canvas's competing gestures are suppressed wholesale rather than arbitrated per event (selection and double-click-zoom-to-fit by any handler at all, wheel zoom by registering Mouse.OnWheel), so by the time your handler runs there is nothing left to cancel. No WPF type appears anywhere on this class: coordinates are VXYZ and double, buttons and modifiers are its own enums and bools. The constructor is public — MouseInfo(kind, position, rawPosition, screenX, screenY, button = None, leftDown = false, rightDown = false, middleDown = false, shift = false, ctrl = false, alt = false, clickCount = 0, wheelDelta = 0, scale = 1, hitTest = null), where hitTest is the Func<VXYZ, Shape> that Target is computed from — but the canvas is what calls it, so you only need it to drive a handler yourself from a test." },
                { "MouseButtonKind", "Which mouse button a MouseInfo is about: None (a plain move, a wheel turn, or an enter/leave), Left, Right, Middle (the wheel pressed as a button), XButton1 or XButton2 (the extra side buttons, if the mouse has them). Read it from MouseInfo.Button on a down, up or click event. To ask what is HELD rather than what this event is about — during a drag, say — use MouseInfo.LeftDown / RightDown / MiddleDown instead. Note Middle is reported to your handlers, but a middle-button DRAG stays the canvas's own pan gesture." },
                { "MouseEventKind", "What kind of event a MouseInfo describes, so one method can serve several callbacks and switch on MouseInfo.Kind. Values: Move (pointer moved with no button held), Down, Up, Click (synthesised from a down/up pair in the same place — see Mouse.OnClick), DoubleClick (a second click inside the system double-click time, delivered instead of Down), Drag (pointer moved with a button held, delivered instead of Move), Wheel, Enter and Leave. The kind always matches the callback the event arrived through, so it is informational rather than something you have to test." },
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
                { "RegionBooleanOps", "Static class providing boolean operations on Regions. Operations approximate region boundaries to high-resolution polygons, clip them with the Clipper2 library, then wrap the results back as Regions. Methods: Union(a, b), Intersect(a, b), Difference(a, b), Xor(a, b). All four also accept a whole collection — Union/Intersect/Difference/Xor(IEnumerable<Region>, int segmentsPerCurve = 32) and (params Region[]) — folding across every region: Union = merged area, Intersect = area common to all, Difference = first minus the rest, Xor = running symmetric difference. Every method takes segmentsPerCurve (default 32) to control how finely curved boundaries are sampled before clipping, EXCEPT the params Region[] forms, where C# will not allow an optional parameter after params — pass a List when you need to raise the precision. WithHoles variants: UnionWithHoles, IntersectWithHoles, DifferenceWithHoles. Analysis: PointInRegion(region, point), Area(region). The BooleanOps class also exposes region overloads that forward here." },
                { "VPolygonBooleanExtensions", "Extension methods that put BooleanOps on the polygon itself: polygon.Union(other) (VPolygon? — null when the two stay disjoint), polygon.Difference(other), polygon.Xor(other) (each List<VPolygon>), polygon.OffsetPolygon(distance), polygon.OffsetPolygonSafe(distance), polygon.MaxSafeInwardOffset(), polygon.MakeSimple(), polygon.HasSelfIntersections(), polygon.Contains(point) and polygon.GetArea() (unsigned). ONE OF THEM IS UNREACHABLE: the Intersect extension is shadowed, because VPolygon already declares IntersectionResult Intersect(ICurve) and an instance method always beats an extension method — so polygon.Intersect(other) returns the points where the two OUTLINES cross, not the overlapping area. Call BooleanOps.Intersect(a, b) for the boolean; the other three are fine in dotted form. The extension overloads take no JoinType/EndType — call BooleanOps.OffsetPolygon for those. Results are unnamed shapes, so name them or call Place() to keep them visible." },
                { "RegionBooleanExtensions", "Extension methods for Region boolean operations, giving instance-method syntax: region.Union(other), region.Difference(other), region.Xor(other), region.ContainsPoint(point), region.GetArea(). ONE OF THEM IS UNREACHABLE: the Intersect extension is shadowed by the inherited Shape.Intersect(Shape), because an instance method always beats an extension method — and Region does not override it, so region.Intersect(other) compiles and ALWAYS RETURNS NULL. Always call RegionBooleanOps.Intersect(a, b) instead. The other five have no instance counterpart and work as written." },
                { "JoinType", "Enum for polygon offset join style. Values: Miter (sharp corners, default), Round (rounded corners), Square (squared-off corners). Used with BooleanOps.OffsetPolygon." },
                { "EndType", "Enum for polygon offset end style. Values: Polygon (closed polygon, default), OpenRound (rounded open ends), OpenSquare (squared open ends), OpenButt (flat cut open ends). Used with BooleanOps.OffsetPolygon." },

                // Hatch Patterns
                { "VHatch", "Fills a closed polygon boundary with a repeating line pattern. Supports 72 built-in AutoCAD-standard patterns (via BuiltInHatch enum or name string). Note that VHatch is NOT in the auto-naming rewriter's type list, so `var h = new VHatch(...)` still ends up unnamed and is hidden after Main() returns — set Name in the initializer or call Place(); and custom patterns defined using the .pat format. Constructors: new VHatch(polygon, BuiltInHatch.ANSI31, scale, angle), new VHatch(polygon, \"BRICK\", scale, angle), new VHatch(polygon, hatchType, scale, angle), new VHatch(boundaryPoints, pattern, scale, angle). Static factory: VHatch.FromDefinition(polygon, patString, scale, angle). Properties: Boundary (List<VXYZ>), Pattern (HatchType), PatternScale (double), PatternAngle (double), Color, LineWeight, Opacity. Methods: GenerateLines() returns clipped line segments, Clone(), Move(), Rotate(), Flip(), Scale(), GetBounds(), Contains(point) (an exact test against the boundary, not the bounding box), DistanceTo(point) (to the boundary treated as a closed path)." },
                { "HatchType", "Defines a hatch pattern composed of one or more line families following the AutoCAD .pat format. Properties: Name, Description, Lines (List<HatchPatternLine>) — all settable. Constructors: new HatchType() for an empty pattern, new HatchType(name, description, lines). Static methods: Parse(string patDefinition) parses from .pat format string, GetBuiltIn(string name) or GetBuiltIn(BuiltInHatch enum) retrieves a built-in pattern (forwarding to BuiltInHatches.Get, so it too hands back a fresh copy). Instance method: Clone() returns a deep copy, cloning every line family, so you can adapt a pattern without touching the one you copied it from." },
                { "HatchPatternLine", "A single line definition within a hatch pattern. Properties: Angle (degrees), OriginX, OriginY, DeltaX (shift along line between rows), DeltaY (spacing between parallel lines), Dashes (double[] - positive=dash, negative=gap, 0=dot, empty=continuous). All are settable. Constructors: new HatchPatternLine() and new HatchPatternLine(angle, originX, originY, deltaX, deltaY, params double[] dashes). Clone() returns a deep copy, including a copy of the Dashes array." },
                { "BuiltInHatch", "Enum of the 72 built-in hatch patterns, taken from the standard AutoCAD pattern library — pass one to a VHatch constructor: new VHatch(boundary, BuiltInHatch.ANSI31, scale: 1, angle: 0). The names are the AutoCAD names, so a drawing hatched here reads the same to anyone who knows that library; where a pattern name contains a hyphen the enum member uses an underscore (BuiltInHatch.AR_BRSTD is the \"AR-BRSTD\" pattern), and the string form accepts either spelling. The names are opaque on purpose — every value in the table below carries the pattern's official description. The families, so you know roughly where to look: SOLID is a filled area (approximated by very close 45° lines, not a true flood fill). LINE, ANGLE, NET, NET3, CROSS, DASH, DOTS, SQUARE, BOX, HEX, HONEY, TRIANG, STARS, ZIGZAG, GRATE, HOUND, ESCHER are plain geometric patterns with no material meaning. ANSI31-ANSI38 are the ANSI section-hatch materials an engineer expects — iron/brick/stone, steel, bronze, plastic, fire brick, marble, lead/insulation, aluminium. BRASS, BRICK, BRSTONE, CLAY, CORK, DOLMIT, EARTH, FLEX, GRASS, GRAVEL, INSUL, MUDST, PLAST, PLASTI, SACNCR, STEEL, SWAMP, TRANS are named materials and terrain. AR_* are the architectural patterns, drawn at building scale in inches rather than at unit scale, so they need a much larger PatternScale (or a much bigger boundary) than the rest before they look like anything. GOST_* are the Russian GOST standard's glass, wood and ground. ACAD_ISO02W100 through ACAD_ISO15W100 are the ISO dashed/dotted line families, useful as directional line fills rather than as textures. If none fit, build your own with HatchType.Parse or VHatch.FromDefinition using .pat syntax." },
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
                { "DxfExporter", "Exports shapes to AutoCAD DXF (R12 ASCII). Construct one with new DxfExporter(), then Export(shapes, filePath) to write a file or ExportToString(shapes) to get the text. Shapes with a native DXF equivalent keep it — a VCircle becomes a CIRCLE entity, not sixty-four chords — and everything else (dimensions, hatches, splines, groups) is decomposed into polylines rather than being silently dropped. A multi-line VText is written as one TEXT entity per line, stacked 1.2 text heights apart along the label's own down direction (R12 TEXT has no multi-line form), and neither VText.Justify nor VText.Anchor is applied — every line starts at the same point, at Location. What the geometry says is what is written: a VRectangle goes out through its four corner points, so its RotationAngle survives, and a VEllipse is sampled over its ACTUAL sweep through PointAtAngle, so a partial or turned ellipse comes out as it was drawn (R12 has no ELLIPSE entity, so it is a polyline either way — closed only when the sweep is a whole turn). Coordinates are passed straight through: one drawing unit is one DXF unit, Y up." },
                { "PdfExporter", "Exports shapes to vector PDF (via PdfSharp), preserving colours, line weights and dash patterns — real vector output, suitable for printing rather than a screenshot. What it draws follows the geometry: a rotated VRectangle goes out through its corner points, a VEllipse with a Rotation or a partial sweep is sampled through PointAtAngle rather than squeezed into PDFsharp's axis-aligned ellipse, and a multi-line VText is laid out line by line — PDF has no line break inside a run — honouring VText.Anchor and VText.Justify (lines are stacked one text height apart here, where GetBounds and the DXF writer use 1.2). Construct one with new PdfExporter() and call Export: the short overload Export(shapes, filePath) auto-sizes the page to the drawing, and Export(shapes, filePath, pageWidthMm, pageHeightMm, scaleMmPerUnit, marginMm) gives you the sheet — page size in millimetres (0 for either dimension auto-sizes to content), the plot scale as millimetres of paper per drawing unit, and the margin. Everything is an argument; there are no PageSize or Margin properties to set. For a DIVIDED canvas there is ExportTiled(tiles, filePath, containerWidth, containerHeight, marginMm = 10), which takes an IReadOnlyList<PdfExporter.PdfTile> and tiles every cell onto one page exactly as it appears on screen, each at its own pan and zoom, the page keeping the container's aspect ratio. There is no scaleMmPerUnit there: it has no meaning across cells sitting at different zooms, so it is deliberately not offered." },
                { "SvgExporter", "STATIC class (namespace DoodleSharp.Canvas) that turns shapes into SVG — a web-compatible vector format that opens in any browser or vector editor. Four methods, and all of them take everything as arguments; there is nothing to construct and no Width/Height properties: Export(shapes, width = 800, height = 600, padding = 20) returns the SVG document as a string, and SaveToFile(filePath, shapes, width = 800, height = 600) writes it to disk (path first). width and height become the <svg> element's size in PIXELS; padding is in WORLD units and is added around the shapes' own bounds before the viewBox is computed, so it is not a pixel margin and does not scale with width/height. World Y-up is flipped to SVG Y-down by a scale(1, -1) group, so the output matches the canvas. A shape type with a native SVG element gets one (VLine to <line>, VCircle to <circle>, VText to <text>, and so on); anything else — a hatch, a region, a grid — is flattened to <path> polylines rather than being dropped. Where an SVG element cannot say what the geometry says, the geometry wins: a VRectangle is written as a polygon through its corner points so its RotationAngle survives, a VEllipse with a partial sweep becomes a sampled <path> (and its Rotation an SVG rotate about the centre), and a multi-line VText becomes one <text> element per line, positioned against the label's own layout box so that Anchor and Justify both survive — SVG treats a newline inside a text element as ordinary whitespace, so writing Content as one run collapsed the label onto a single line. Lines are stacked one text height apart here, where GetBounds and the DXF writer use 1.2. Styling survives the trip: LineWeight is written as a stroke-width pinned to DEVICE pixels by vector-effect=\"non-scaling-stroke\" (the viewBox is world coordinates at 1:1, so a bare stroke-width would be read as world units and LineWeight = 2 would come out two world units thick — invisible on a large drawing), and LineType is written as a stroke-dasharray taken from the same LineTypePatterns table the screen uses, so a dashed line exports dashed. An empty shape list still produces a valid document, sized from width and height. For a DIVIDED canvas there are two more: ExportTiled(tiles, width, height) and SaveTiledToFile(filePath, tiles, width, height), which take an IReadOnlyList<SvgExporter.SvgTile> and lay every cell out on one page exactly as it appears on screen, each at its own pan and zoom, with a thin separator rectangle round each cell when there is more than one. That is deliberately NOT what Export does for an undivided drawing — Export fits the SHAPES with padding and ignores the screen entirely, which is a different picture." },
                { "PdfTile", "One cell of a divided drawing, as PdfExporter.ExportTiled wants it. A readonly record struct with five positional members: PageRect (System.Windows.Rect - where the cell sits inside the on-screen container, in device pixels), Scale (screen pixels per world unit in that cell - that cell's zoom, the same quantity as MouseInfo.Scale), PanX and PanY (that cell's pan, in pixels) and Shapes (an IReadOnlyList<IDrawable> of what is placed on it). Nested inside PdfExporter, so it is written PdfExporter.PdfTile. Construct it positionally: new PdfExporter.PdfTile(rect, scale, panX, panY, shapes). Being a record struct it also carries Deconstruct, value Equals, GetHashCode and a generated ToString. You rarely build one by hand - File > Export > PDF on a divided canvas fills these in from each cell's live view - but the type is public so the exporter can be driven from code." },
                { "SvgTile", "One cell of a divided drawing, as SvgExporter.ExportTiled and SaveTiledToFile want it. A readonly record struct with five positional members: PageRect (System.Windows.Rect - the cell's rectangle on the page, in device pixels), Scale (screen pixels per world unit in that cell - that cell's zoom, the same quantity as MouseInfo.Scale), PanX and PanY (that cell's pan, in pixels) and Shapes (an IEnumerable<IDrawable> of what is placed on it - note this one is IEnumerable where PdfTile's is IReadOnlyList). Nested inside SvgExporter, so it is written SvgExporter.SvgTile. Construct it positionally: new SvgExporter.SvgTile(rect, scale, panX, panY, shapes). Being a record struct it also carries Deconstruct, value Equals, GetHashCode and a generated ToString. In the app File > Export > SVG fills these in from each cell's live view; build them yourself only if you are driving the exporter from code." },
                { "VideoExporter", "Exports an animation to MP4 using the Windows Media Foundation H.264 encoder — no external tools to install. Construct it with the output path and frame size (new VideoExporter(path, width, height, fps = 30, bitrateMbps = 5)), call AddFrame(RenderTargetBitmap) once per frame in order, then Dispose() to finalise the file. Implements IDisposable, so a using statement is the safe form. In practice you reach this through File > Export > Video, which offers resolution presets (Canvas Size, 720p, 1080p, 4K, Custom), 15-60 FPS and 1-20 Mbps." },
                { "GifEncoder", "Writes an animated GIF, one frame at a time, to any Stream. Construct it as new GifEncoder(stream, width, height, frameDelayMs = 100, repeat = true) — frame delay and looping are CONSTRUCTOR ARGUMENTS, not properties — then call AddFrame(BitmapSource) per frame and Dispose() to write the trailer. There is no Save(): the file only becomes a valid GIF when Dispose runs, so wrap it in a using statement. Every frame must match the width and height given to the constructor. Good for short loops and web sharing; use VideoExporter when you want quality or length." },

                // Canvas and Snap System
                { "DoodleSharp.Canvas", "Contains classes for the interactive canvas, drawing tools, and snap detection system." },
                { "SnapType", "Which kind of geometry a snap point was found on. It is what SnapResult.Type carries, and what each of SnapEngine's eight toggles switches. Nine values: None (the \"no snap\" value — SnapEngine never returns a result carrying it, it returns null instead), Endpoint (the start or end of a line, arc, polyline or polygon edge), Midpoint (the middle of a segment or curve), Center (the centre of a circle, ellipse or arc), Intersection (where two shapes cross), Nearest (the closest point anywhere on a curve), Perpendicular (the point that makes a right angle back to SnapEngine.ReferencePoint — your first click), Extension (a point on the invisible continuation of an existing edge, past its endpoint), and Tangent (the point on a circle or arc where a line from ReferencePoint would just touch it). When several candidates are within tolerance the TYPE decides which wins, in exactly this order — Endpoint, Midpoint, Center, Intersection, Perpendicular, Tangent, Extension, Nearest — and distance only breaks ties within one type. So a slightly more distant endpoint beats a nearer point-on-curve, which is what makes snapping feel deliberate rather than twitchy." },
                { "SnapResult", "One snap candidate: a world position, the SnapType it came from, and how far it was from the cursor. You normally get one back from SnapEngine.FindSnapPoint rather than building one, but it is plain data with a public constructor — new SnapResult(point, type, distance) — and every property is settable. Always populated: Point (VXYZ, the snapped position, and the point a drawing tool actually places), Type, and Distance (world units from the cursor). The rest are filled in only for the types that need them and are null otherwise: ExtensionSource and ExtensionAngle for Extension (the endpoint the continuation runs from, and its direction in DEGREES); ReferenceSource for Perpendicular and Tangent (the first-click point the relationship was measured from, i.e. whatever SnapEngine.ReferencePoint held); and TangentCenter for Tangent (the centre of the circle or arc being touched, which is what lets an overlay draw the radius). ConstraintPoint is OBSOLETE and there is nothing to migrate: it was always exactly Point, and inherently so — the foot of a perpendicular IS the perpendicular snap point and the touch point IS the tangent snap point — so read Point instead. Coordinates are world coordinates — Y up, origin at the canvas centre." },
                { "SnapEngine", "Finds the snap point nearest the cursor over a set of shapes — the engine behind the drawing tools and the measuring tape. Construct one with new SnapEngine() and call FindSnapPoint(cursorWorld, shapes, scale); it returns the winning SnapResult, or null when nothing is within tolerance. Two overloads: one takes an IReadOnlyList<IDrawable> and considers every shape, the other takes a Rendering.SceneIndex so the cull index narrows the search first (that is what the canvas uses on large drawings, and it THROWS ArgumentNullException on a null index — it holds no shapes of its own, so it cannot fall back to a full scan, and quietly answering \"no snap\" would have disabled snapping on every mouse move with nothing to notice). scale is the canvas zoom: the tolerance is a fixed 15 SCREEN pixels internally, divided by scale to get a world tolerance, so snapping feels the same at every zoom level. Each of the eight types has its own settable toggle — EndpointSnapEnabled, MidpointSnapEnabled, CenterSnapEnabled, IntersectionSnapEnabled, NearestSnapEnabled, PerpendicularSnapEnabled, ExtensionSnapEnabled, TangentSnapEnabled — all true by default. They are plain properties you can set directly; SyncFromSettings() is the separate call that overwrites all eight from the application's Snap Settings, and nothing calls it for you except DrawingTool's constructor and RefreshSnapSettings(). ReferencePoint (VXYZ?, null by default) is the first-click point that Perpendicular and Tangent measure from — leave it null and neither type can produce a candidate at all. When several candidates are in range, SnapType decides before distance does (see SnapType for the order)." },
                { "DrawingInputMode", "Which value the drawing tool is currently accepting from the keyboard; read it from DrawingTool.InputMode. Three values: None (the next point follows the mouse and its snaps — the default), Distance (typed digits set the distance from the last placed point) and Angle (typed digits set the direction, in DEGREES, counter-clockwise from +X). Tab cycles None to Distance to Angle to None via DrawingTool.CycleInputMode(), typing a digit while drawing jumps straight into Distance, Enter commits, and Escape leaves without committing. None of it engages until at least one point has been clicked. The characters typed so far are in DrawingTool.InputBuffer; the committed values land in DrawingTool.OverrideDistance and OverrideAngle, and GetEffectiveEndPoint() is where they are turned back into a position." },
                { "DrawingMode", "Which shape DrawingTool is drawing: set with SetMode, reported by Mode, and announced by the ModeChanged event. Sixteen values — None (idle, nothing in progress) plus Point, Line, Circle, CircleDiameter, CircleTwoPoints, CircleThreePoints, Rectangle, Ellipse, Arc, Polygon, Polyline, Bezier, Spline, Arrow and Text. Click counts: Point takes one; Line, Circle, CircleDiameter, CircleTwoPoints, Rectangle, Ellipse and Arrow take two; Arc and CircleThreePoints take three; Bezier takes four; Polygon, Polyline and Spline collect clicks until a double-click finishes them (OnDoubleClick, not OnLeftClick); Text collects one click and then raises TextPlacementRequested so the host can ask for the string, which comes back through CompleteText. The three circle variants differ only in what the second click means — see the individual values below." },
                { "DrawingTool", "The interactive drawing tool's state machine: which shape is being drawn, the points clicked so far, the live snap, and any distance or angle typed at the keyboard. This is the object behind the Draw menu and the P/L/C/R canvas shortcuts. The canvas owns one (RenderCanvas.DrawingTool) and feeds it mouse and key events, so project code does not normally drive it — it is documented because everything the drawing tools do is defined here, and because it is public and self-contained enough to drive directly if you want to. new DrawingTool() also builds its SnapEngine and syncs the snap toggles from application settings. The flow: SetMode(DrawingMode.Line) arms it and clears any points; OnMouseMove(worldPos, shapes, scale) updates CurrentPoint and CurrentSnap; OnLeftClick(worldPos) appends to Points and, once enough have been collected, constructs the real shape (which auto-registers on the canvas like any other) and raises ShapeCompleted. Polygon, Polyline and Spline are finished by OnDoubleClick instead. OnRightClick() discards the points in progress, or leaves the mode if there are none; Cancel() (Esc) does both. Read-only state: Mode, Points, CurrentPoint, CurrentSnap, InputMode, InputBuffer, IsBufferSelected, OverrideDistance, OverrideAngle, StatusMessage, and GetPreviewShape() for the grey rubber-band shape. Settable: IsOrthoMode, which is what holding Shift does. Events: ShapeCompleted, ModeChanged, InputChanged, TextPlacementRequested. Keyboard entry: CycleInputMode() (Tab), StartDistanceInput(), HandleCharInput(c), HandleBackspace(), HandleEnterInput(), HandleEscapeInput(), ResetInputMode() — each Handle* returns false when no input mode is active, so a host can fall through to its own shortcut handling. RefreshSnapSettings() re-reads the snap toggles after the user changes them in Settings." },

                // Console
                { "DoodleSharp.Console", "Console output for project code. VizConsole.Log(...) writes to the console panel below the canvas." },
                { "VizConsole", "Static class providing console output. Log(value, itemize = true) is the only method - there is no Write() or WriteLine(). It prints value.ToString() (an empty line for null) to the console panel, prefixed with the calling file name and line number, both captured automatically. When itemize is true (the default) and value is a collection - any IEnumerable other than a string - each item is printed on its own line and an empty collection prints \"(empty)\"; pass false to print the collection's own ToString() instead." },
                { "ConsoleOutput", "The singleton behind the console panel — the collector VizConsole.Log writes into, reached as ConsoleOutput.Instance. You almost never need it: VizConsole.Log is the API for scripting, and it captures the calling file and line for you, which this does not. Use it directly only to read the console back (GetEntries, GetFormattedOutput — handy for asserting on your own output, or copying a run's log somewhere), to Clear it, or to add an entry that carries a clickable source location (AddEntry with filePath and lineNumber). Thread-safe: every method locks, so logging from a Task is safe. Updates to the panel are throttled, so a tight loop of Log calls does not repaint per line; Flush() forces the panel to catch up immediately, and the host calls it when your code finishes." },
                { "ConsoleEntry", "One line in the console panel: the message, where it came from, and whether it is an error. Plain data with settable properties — you get these back from ConsoleOutput.GetEntries() rather than constructing them. Properties: Message, ModuleName (the source file name), LineNumber, Column, FilePath (the full path, when known), IsError (rendered in the error colour), IsNewLine, and IsClickable, which is computed rather than set — true when there is both a FilePath and a LineNumber > 0, which is what makes a console line jump to the code when you click it." },

                // Rendering (library plumbing)
                { "C2VGeometry.Rendering", "Renderer and exporter plumbing: the one place a shape is turned into drawable primitives, and the sink interface it emits into. This namespace exists so the canvas renderer, the drawing-tool preview, zoom-to-extents and the SVG/PDF/DXF exporters all share a single shape-to-primitives translation instead of each keeping its own type switch — which is how exporters used to drift and silently drop shape types. NOTHING HERE IS NEEDED TO DRAW. A sketch never touches it; use it only if you are writing your own consumer of the geometry — your own exporter, your own renderer, or a measurement pass that wants to see exactly what the renderer sees." },
                { "IPrimitiveSink", "Where ShapeTessellator sends the primitives it produces — implement it to consume the geometry library's output in your own format. Members: Hints (a TessellationHints controlling flattening fineness), BeginShape(shape, pen) called before each shape's primitives (return false to decline the shape entirely), EndShape(), EmitPolyline(points, closed) for a stroked run of points, EmitFilledLoops(loops, rule) where the first loop is the outer boundary and the rest are holes, EmitPoint(point) for a zero-area mark, EmitText(text) for text left unflattened, and TryEmitNative(shape, pen) — a default interface method returning false — which is offered before flattening when Hints.PreferNative is set, so a sink that can express a circle AS a circle claims it and suppresses tessellation. Renderer plumbing, not scripting API: to draw, just construct shapes." },
                { "BoundsPrimitiveSink", "An IPrimitiveSink that measures instead of drawing: feed shapes through ShapeTessellator into one of these and it accumulates the bounding box of everything it is given. It measures through the tessellator, so it sees exactly what the renderer draws — a private type switch would leave an unrecognised shape out of the extents and let it sit off screen after a zoom-to-fit. (The app's own zoom-to-extents no longer goes through here: it asks each shape's GetBounds() directly, now that GetBounds is exact for a partial arc, a turned ellipse and a multi-line label.) Members: MinX/MinY/MaxX/MaxY, HasBounds (false until something has been added), Reset() to reuse the instance, and IncludeBounds(shape) to fold in a shape the tessellator declined using the shape's own GetBounds(). For a single shape prefer shape.GetBounds()." },
                { "PolylineFallbackSink", "An IPrimitiveSink that reduces any shape to plain polylines and filled loops, for a consumer with no native form for it. Set the callbacks you care about — OnPolyline(points, closed, pen), OnFilled(loops, pen), OnPoint(point, pen), OnText(text) — and any you leave null are simply dropped. Unhandled is a list for recording shapes the pass could not reduce, so that an incomplete export can be reported rather than silently truncated — note the sink does NOT fill it in for you: its BeginShape accepts everything, so it is the caller that appends whenever ShapeTessellator.Tessellate returns false. Reset() clears it between runs. This is the floor under each exporter's own native mapping, not a replacement for it: flattening a circle to sixty-four chords is right for a rasterizer and wrong for a DXF." },
                { "ShapeTessellator", "The one place a shape is turned into drawable primitives — every V* type, decomposed into polylines, filled loops, points and text, and pushed into an IPrimitiveSink. Construct one and call Tessellate(shape, sink); the static SegmentsForRadius(radiusPixels) is the curve-flattening rule it uses, exposed so a caller can match it. Two things to get right. Tessellate RETURNS BOOL and the value is not optional: false means the sink declined the shape (BeginShape returned false) and the caller must do something else with it — ignoring the result is how dimensions and construction lines vanish from an export. And the instance holds scratch buffers and is deliberately NOT thread-safe, because reusing those buffers is the entire point; give each thread its own." },
                { "TessellationHints", "How finely curves should be flattened, carried on an IPrimitiveSink. Scale is SCREEN PIXELS PER WORLD UNIT — the view's zoom, the same quantity as MouseInfo.Scale, so a world size MULTIPLIED by it gives a size on screen. (The property's own XML comment in the library says the reciprocal; the tessellator computes radiusPixels = radius * Scale, so the multiply is what is true.) Segment counts are chosen from a shape's size in PIXELS, not world units, because a circle of radius 1 needs a different number of segments depending entirely on how far you have zoomed in. PreferNative is set by a sink that can express a circle as a circle (DXF, SVG, PDF): when true the tessellator offers each shape to TryEmitNative first and only flattens what the sink declines. Defaults: Scale = 1.0, PreferNative = false." },
                { "PenSpec", "Everything a renderer needs to know about how one shape is painted, lifted out of the shape so a sink does not have to reach back into it. A readonly struct with six fields — Color, FillColor, LineWeight, LineType, LineTypeScale, Opacity — mirroring the shape's styling members. Build one with PenSpec.From(shape) rather than the constructor. HasFill is the useful part: it reports whether there is a genuine fill, treating an empty string, \"Transparent\" and \"None\" (case-insensitively) all as no fill, which is the check a sink should make before filling anything." },
                { "LineTypePatterns", "STATIC class (namespace C2VGeometry.Rendering) holding the ONE definition of what each LineType looks like — the dash and gap run lengths every backend and exporter draws from. There used to be two tables and they disagreed: the WPF path expressed patterns as multiples of the pen thickness while the software rasterizer used device pixels and quietly rendered Center, Phantom and Hidden as SOLID lines, so the same dashed line looked different, or was not dashed at all, depending only on which backend happened to draw the frame. The canonical unit is DEVICE PIXELS at a LineTypeScale of 1. Members: DevicePixels(lineType) returns the alternating dash/gap runs as a ReadOnlySpan<double> over a SHARED array — it is called per shape per frame so it must not allocate, and you must not write to it; IsSolid(lineType, scale) is the one check to make before building a pattern (true for Continuous, and for a scale that is zero, negative or non-finite, because zero-length runs rasterise as nothing at all rather than as a line); ClampScale(scale) folds a caller-supplied scale into [MinScale, MaxScale] and returns 1.0 for a non-finite or non-positive value; MinScale (0.01) and MaxScale (1000.0) are the clamps. You do not need this to draw — set shape.LineType and shape.LineTypeScale. It is public so a custom exporter or sink emits exactly the same dashes the screen does. Consequence worth knowing: because the pattern is fixed in pixels, dash lengths no longer vary with LineWeight — a hairline and a heavy line of the same type dash identically, which is what a CAD package does." },
                { "FillRule", "How a filled outline decides what is inside it, for IPrimitiveSink.EmitFilledLoops. EvenOdd (the default, 0) counts crossings, so a loop inside a loop punches a hole regardless of its direction — the right choice for outer-plus-holes geometry, which is how the library emits filled areas. NonZero (1) counts crossing direction, so an inner loop only becomes a hole if it winds the opposite way to the outer one. Distinct from PolygonClipper's internal fill rule, which boolean operations pick for themselves; this one only affects how a sink paints what it is given." },
                { "GlyphOutlineProvider", "The application's implementation of IGlyphOutlineProvider — the WPF font code that turns a VText's characters into vector contours, wired into VText.GlyphOutlineProvider at startup because C2VGeometry itself is WPF-free and cannot rasterise fonts. Nothing to call: work with VText.ToCharShape(i), LiftChar(i), the indexer text[i], or LiftChars(start, count), all of which route through whichever provider is installed and return null when none is." },
                { "Sketch", "Abstract base class for p5.js-style animation sketches: subclass it, override Setup() (called once) and Draw() (called every frame), and DoodleSharp runs the frame loop for you. It is the alternative to writing Main() — a sketch project's own class derives from this. THE REGISTERED SHAPES ARE CLEARED BETWEEN FRAMES, so anything that must stay visible is either re-created in Draw() or held in a field on your subclass; persistent state lives in your own fields, because Draw() is called afresh each frame with no arguments. Read-only per-frame state, filled in by the runtime before each call: FrameCount (0 on the first Draw), ElapsedSeconds (since Setup returned), DeltaSeconds (since the previous frame), Width and Height (the logical drawing area, 800x600 until Size() says otherwise), and the polled input MouseX, MouseY and MousePressed. Protected methods you call from inside the sketch: Size(width, height) to declare the drawing area and zoom the canvas to fit it, Background(color) to set the canvas colour, NoLoop() to pause the frame loop and Loop() to resume it. Geometry is ordinary C2VGeometry — the same shapes, the same Y-up world with (0, 0) at the centre. KNOWN GAP: KeyPressed and LastKey are declared but nothing ever writes them, so they sit at false and the empty string for every sketch; use them at your own risk, and reach for the Mouse callbacks if you need real input. Note the runtime stops the sketch and reports to the console if Setup() or Draw() throws, rather than letting the exception reach WPF sixty times a second." },
            };
        }

        public string GetSummary(string name)
        {
            if (_summaries.TryGetValue(name, out var summary))
                return summary;
            return "No description available.";
        }

        /// <summary>
        /// Types documented individually, from namespaces that are otherwise app internals.
        ///
        /// <para>
        /// <c>DoodleSharp.Canvas</c> cannot go in <see cref="_namespacePrefixes"/>: it would drag in
        /// a dozen genuine internals (<c>RenderCanvas</c>, <c>QuadTree</c>, <c>CodeSyncManager</c>,
        /// <c>ViewportTransform</c>, <c>SelectionTool</c>, …) that no user calls. But seven public
        /// types living there are part of the user-facing surface, and <c>SvgExporter</c> is named
        /// in CLAUDE.md as explicitly in scope. All seven had hand-written summaries that no reader
        /// could reach, because a type absent from <see cref="GetDocumentableTypes"/> has no page
        /// and no search entry — rendering a page correctly is worth nothing if the type is not in
        /// the tree, which is the reachability-versus-rendering split note 91 records.
        /// </para>
        ///
        /// <para>
        /// Full names, so a same-named type in a documented namespace cannot be admitted by accident.
        /// </para>
        /// </summary>
        public static readonly string[] AllowedInternalTypes =
        {
            "DoodleSharp.Canvas.SvgExporter",
            "DoodleSharp.Canvas.SnapEngine",
            "DoodleSharp.Canvas.SnapType",
            "DoodleSharp.Canvas.SnapResult",
            "DoodleSharp.Canvas.DrawingTool",
            "DoodleSharp.Canvas.DrawingMode",
            "DoodleSharp.Canvas.DrawingInputMode",
            "DoodleSharp.Canvas.GlyphOutlineProvider",

            // Nested, and therefore reached by full name with a '+'. SvgExporter.ExportTiled takes a
            // list of these, so a caller needs the field meanings; the outer type is already
            // allowlisted above, but nesting is a separate reachability question from namespace.
            "DoodleSharp.Canvas.SvgExporter+SvgTile",

            // A sketch project's own code DERIVES from this, so it is about as user-facing as an
            // API gets — and it had no page at all, because DoodleSharp.Sketching is not one of the
            // namespace prefixes. It cannot become one either: SketchRuntime lives beside it and is
            // the host's frame pump, not something a sketch calls.
            "DoodleSharp.Sketching.Sketch",
        };

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

            // Enums and structs must be listed. `IsClass || IsAbstract` covers classes and
            // interfaces (an interface is abstract in metadata) but silently excluded every enum
            // and every value type — so ColorName's 83 colours, BuiltInHatch's 73 patterns,
            // LineType, VTextAnchor, ParamValue, RayHit and RayQuery had no reachable page at all,
            // even after the member tables were taught to render enum values.
            return types
                .Where(t => IsPubliclyVisible(t) && !t.IsGenericParameter &&
                    (t.IsClass || t.IsAbstract || t.IsEnum || t.IsValueType) &&
                    t.Namespace != null &&
                    (_namespacePrefixes.Any(p => t.Namespace == p || t.Namespace.StartsWith(p + ".") || t.Namespace.StartsWith(p)) ||
                     AllowedInternalTypes.Contains(t.FullName)))
                .OrderBy(t => t.Namespace)
                .ThenBy(t => t.Name)
                .ToList();
        }

        /// <summary>
        /// True when the type can be named from outside the assembly. <c>Type.IsPublic</c> is false
        /// for a NESTED type however public it is, so the plain check silently excluded
        /// <c>PdfExporter.PdfTile</c> and <c>SvgExporter.SvgTile</c> — the argument types of
        /// <c>ExportTiled</c>, which a caller cannot use without knowing their fields. This walks the
        /// whole declaring chain rather than trusting <c>IsNestedPublic</c> alone, because a public
        /// type nested inside an internal one is still unreachable and <c>GetTypes()</c> (unlike
        /// <c>GetExportedTypes()</c>) hands those out.
        /// </summary>
        private static bool IsPubliclyVisible(Type type)
        {
            while (type.IsNested)
            {
                if (!type.IsNestedPublic) return false;
                type = type.DeclaringType!;
            }
            return type.IsPublic;
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

            var title = new Paragraph(new Run(displayName + " " + GetTypeKindNoun(type)))
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

            // Enum values. An enum declares no properties or methods of its own, so without
            // this an enum page listed nothing at all.
            if (type.IsEnum)
            {
                AddSectionHeader(doc, "Values");
                doc.Blocks.Add(GenerateMemberTable(
                    type.GetFields(BindingFlags.Public | BindingFlags.Static), cleanName));
                return doc;
            }

            // Properties. Static must be included: without it every static class (VColor,
            // BooleanOps, Chart, GlobalParameters, ...) rendered an empty page, and the
            // static factories on the shapes (VCircle.FromCenterDiameter, VArc.From*,
            // VXYZ.BasisX, ...) were invisible.
            var props = type.GetProperties(MemberFlags);
            if (props.Length > 0)
            {
                AddSectionHeader(doc, "Properties");
                doc.Blocks.Add(GenerateMemberTable(props, cleanName));
            }

            // Constants and static fields (GeometryTolerance.Epsilon and friends).
            var fields = type.GetFields(MemberFlags)
                .Where(f => !f.IsSpecialName)
                .ToArray();
            if (fields.Length > 0)
            {
                AddSectionHeader(doc, "Fields");
                doc.Blocks.Add(GenerateMemberTable(fields, cleanName));
            }

            // Events. Nothing listed these before, so every public event was unreachable in Help: the
            // methods query drops add_*/remove_* as IsSpecialName and there was no section of their
            // own. Descriptions had already been written for four of them, which is exactly note 91's
            // shape — prose that exists for members no reader can get to, so a spot-check of the
            // dictionaries looks healthy while the rendered page is missing them.
            var events = type.GetEvents(MemberFlags);
            if (events.Length > 0)
            {
                AddSectionHeader(doc, "Events");
                doc.Blocks.Add(GenerateMemberTable(events, cleanName));
            }

            // Methods
            var methods = type.GetMethods(MemberFlags)
                .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)) // Exclude getter/setter internal methods and Object methods
                .ToArray();

            // DeclaredOnly on an INTERFACE means exactly that: an interface has no BaseType chain
            // to walk, so a member declared only on an extended interface (IDrawable.Draw/Place,
            // reached through ICurve) never appeared here even though ICurve.Draw/ICurve.Place
            // already had descriptions written — the page simply never showed the members those
            // keys were for. Fold in each extended interface's own declared methods so a reference
            // typed as this interface shows the whole contract it actually exposes.
            if (type.IsInterface)
            {
                var declaredNames = methods.Select(m => m.Name).ToHashSet();
                var inheritedInterfaceMethods = type.GetInterfaces()
                    .SelectMany(i => i.GetMethods(MemberFlags))
                    .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object) && !declaredNames.Contains(m.Name));
                methods = methods.Concat(inheritedInterfaceMethods).ToArray();
            }

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

        /// <summary>
        /// "Class", "Enum", "Struct" or "Interface" — the page title used to say "Class" for
        /// everything, which read as a mistake on an enum page like ColorName.
        /// </summary>
        private static string GetTypeKindNoun(Type type)
        {
            if (type.IsEnum) return "Enum";
            if (type.IsInterface) return "Interface";
            if (type.IsValueType) return "Struct";
            return "Class";
        }

        private static string GetTypeKindKeyword(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (type.IsValueType) return "struct";
            // A C# static class is abstract + sealed in metadata; saying so is worth a word,
            // because it is what tells the reader there is nothing to `new`.
            if (type.IsAbstract && type.IsSealed) return "static class";
            if (type.IsAbstract) return "abstract class";
            return "class";
        }

        private Paragraph GenerateSyntax(Type type)
        {
            var syntax = $"public {GetTypeKindKeyword(type)} {GetDisplayTypeName(type)}";

            // Enums list Enum as their base and structs list ValueType; neither is information.
            var baseType = type.IsEnum || type.IsValueType ? null : type.BaseType;
            if (baseType != null && baseType != typeof(object))
                syntax += $" : {GetDisplayTypeName(baseType)}";

            // An enum's only interfaces are the BCL's IComparable/IFormattable/IConvertible, which
            // is noise on a page about colour names.
            var interfaces = type.IsEnum ? Array.Empty<Type>() : type.GetInterfaces();
            if (interfaces.Length > 0)
            {
                syntax += (baseType != null && baseType != typeof(object) ? ", " : " : ");
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
                else if (member is FieldInfo fi)
                {
                    // Enum values carry their own constant; a plain const shows its value too,
                    // which is the useful thing to know about GeometryTolerance.Epsilon.
                    sig = fi.FieldType.IsEnum && fi.DeclaringType == fi.FieldType
                        ? Convert.ToInt64(fi.GetRawConstantValue()).ToString()
                        : GetFriendlyTypeName(fi.FieldType)
                          + (fi.IsLiteral ? $" = {fi.GetRawConstantValue()}" : "");
                }
                else if (member is EventInfo ei)
                {
                    // The handler type is the whole point of an event row: it tells the reader what
                    // signature to write. Without this branch an event rendered with a blank cell.
                    sig = "event " + GetFriendlyTypeName(ei.EventHandlerType!);
                }

                // Flag staticness — it changes how the member is called, so it must be visible.
                if (IsStaticMember(member))
                    sig = string.IsNullOrEmpty(sig) ? "static" : "static " + sig;

                var sigPara = new Paragraph(new Run(sig));
                sigPara.FontFamily = new FontFamily("Consolas");
                sigPara.FontSize = 11;
                sigPara.Foreground = Brushes.DarkSlateGray;
                sigPara.TextAlignment = TextAlignment.Left;
                var sigCell = new TableCell(sigPara) { Padding = new Thickness(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0,0,0,1), TextAlignment = TextAlignment.Left };
                row.Cells.Add(sigCell);

                // Description column. A constructor reflects as ".ctor", so it cannot be keyed by
                // member name like everything else — see GetConstructorDescription.
                var description = member is ConstructorInfo ctorMember
                    ? GetConstructorDescription(className, ctorMember)
                    : GetInheritedMemberDescription(className, member);
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
VXLine full = ray.ToXLine();               // extend backwards too

// A ray is an ICurve, so ray.Intersect(curve) gives every crossing as an
// IntersectionResult. It is EXACT: the ray is converted to its RenderExtent
// span and the closed-form circle routine runs, not chord sampling.
var rock  = new VCircle(120, 60, 40);
var probe = VRay.AtAngle(new VXYZ(0, 0), 27);   // degrees CCW from +X
probe.Remove();                                 // a query, not part of the drawing

double best = 400;                              // beam length if nothing is hit
VXYZ end = probe.GetPointAtDistance(best);
foreach (var p in probe.Intersect(rock).Points)
{
    double d = new VXYZ(0, 0).DistanceTo(p);    // Points are not sorted for you
    if (d < best) { best = d; end = p; }
}
new VLine(new VXYZ(0, 0), end) { Name = ""beam"", Color = ""Gold"" };

// The reach is RenderExtent, not infinity.
var eastward = VRay.HorizontalRight(new VXYZ(0, 0));
eastward.Remove();
var faraway  = new VCircle(30000, 0, 100);
bool seen    = eastward.DoesIntersect(faraway);   // false at the default 10000
eastward.RenderExtent = 40000;
bool nowSeen = eastward.DoesIntersect(faraway);   // true" },

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

// Rotation turns the ellipse itself. StartAngle/EndAngle are measured in the
// ellipse's own frame, so a half ellipse keeps its half and turns with it.
var tilted = new VEllipse(new VXYZ(150, 0), 60, 30) { Rotation = 30 };
var tiltedHalf = new VEllipse(new VXYZ(150, -120), 60, 30, 0, 180) { Rotation = 30 };

// Rotate() writes Rotation as well as moving the centre, so this really turns:
tilted.Rotate(tilted.Center, 45);            // now at 75 degrees

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

                { "VArc", @"// Create an arc (center, radius, startAngle, endAngle) - degrees from +X.
// The SWEEP DIRECTION IS THE SIGN of endAngle - startAngle: nothing is
// normalised, so (0, 270) sweeps counter-clockwise three quarters of the way
// round, and (270, 0) is the same shape drawn CLOCKWISE.
var arc = new VArc(new VXYZ(50, 50), 40, 0, 270);
arc.Color = ""Orange"";
arc.LineWeight = 3;

var clockwise = new VArc(new VXYZ(-120, 50), 40, 270, 0);   // same span, other way

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
arc2.SetBounds(0.0, 0.5);   // keep the first half, in place

// Bounds are the ARC's box, not its circle's: the endpoints, widened only by
// the compass extremes (0/90/180/270) the sweep actually reaches.
var quarter = new VArc(new VXYZ(0, -150), 50, 0, 90);
BoundingBox box = quarter.GetBounds();      // ~50 x 50, not 100 x 100

// Rotate shifts BOTH ends by the same amount, so the sweep survives intact --
// including one written past the wrap, like this 20-degree sliver.
var sliver = new VArc(new VXYZ(150, -150), 50, 350, 370);
sliver.Rotate(sliver.Center, 30);           // now 380 to 400, still 20 degrees

// Flip mirrors about the LINE you pass, at any angle, and swaps the ends --
// so the mirrored arc travels the other way round, as a mirror image does.
var vertical = new VLine(0, -250, 0, -50) { Name = ""mirror"" };
sliver.Flip(vertical);" },

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

// Justify lines the ROWS of a multi-line label up with each other. It composes with
// Anchor (which places the whole block) and does nothing to single-line text.
var totals = new VText(-120, 80, ""Total 42\nSubtotal 40\nTax 2"", 14);
totals.Justify = VTextJustify.Right;   // every line ends at the same x

// Rotate the entire text block (CCW degrees around Location)
var tilted = new VText(0, -100, ""45 degrees"", 18);
tilted.Angle = 45;

var vertical = new VText(80, 0, ""Vertical"", 16);
vertical.Angle = 90; // reads bottom-to-top

// Width is 0 by default, meaning ""estimate it from the string"" (0.6 x Height per
// character of the longest line); set it to override the box width used by
// GetBounds and anchoring. GetBounds() is multi-line aware -- widest line by the
// stacked height of them all -- so picking and zooming frame the whole label.
BoundingBox labelBox = totals.GetBounds();   // three lines tall, not one

// Mask: a solid background so a label stays readable over the geometry it crosses.
// It is ON by default and painted in the CANVAS BACKGROUND colour, so a label
// looks untouched over empty canvas and cleanly interrupts whatever it crosses.
var dim = new VText(0, 40, ""433.5"", 14);
dim.Anchor = VTextAnchor.MiddleCenter;

// MaskOffset is padding as a FRACTION of the text height (0 = hug the glyphs,
// 1 = a full text height of padding on every side), so a label keeps the same
// breathing room whatever its size.
dim.MaskOffset = 0.25;           // clamped to [0, 1]

// Null MaskColor = follow the canvas background, re-read every time it is drawn.
// Set one to override it, or turn the mask off to let the drawing show through.
dim.MaskColor = ""Red"";          // any colour name or hex — VColor.Red, ""#202020"", …
var plain = new VText(0, 20, ""no plate"", 14) { Mask = false };

// The mask is part of the text, never a separate shape: it draws immediately under
// its own glyphs and does not change GetBounds(). Use ZIndex to say what the whole
// masked label sits above.
dim.ZIndex = 10;

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

                { "VTextJustify", @"// VTextJustify lines the ROWS of a multi-line label up with each other.
// Lines come from newline characters inside Content. Single-line text is unaffected.
// Anchor places the BLOCK against Location; Justify shapes the ragged edge INSIDE
// the block -- the two compose, they do not compete.
// Coordinates are Y-up with (0, 0) at the canvas centre.
string body = ""Area 1200.0\nPerimeter 140.0\nOffset 12.5"";

// Left (the default): every line starts at the same x, ragged on the right
var left = new VText(-260, 0, body, 12);

// Center: the short lines are centred against the longest one.
// Anchor centres the BLOCK on (0, 0); Justify centres the LINES inside it.
var centred = new VText(0, 0, body, 12);
centred.Anchor = VTextAnchor.MiddleCenter;
centred.Justify = VTextJustify.Center;

// Right: every line ends at the same x -- what lines a column of values up
var right = new VText(260, 0, body, 12);
right.Justify = VTextJustify.Right;

// Justify never moves or resizes the block, so all three report the same GetBounds()
// -- which is multi-line aware: widest line by the stacked height of them all.
// EXPORT: SVG and PDF lay the lines out and honour Justify (and Anchor), so a
// justified label survives the trip. DXF keeps the lines (one TEXT entity each,
// stacked 1.2 x Height apart) but starts them all at the same point -- R12 TEXT
// has no block width to justify inside." },

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
arrow.HeadLength = 20;   // length of each wing, world units (default 15)
arrow.HeadAngle = 20;    // half-angle off the shaft in degrees (default 30),
                         // so 20 gives a narrower dart than the default

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

// Angle between two vectors. UNSIGNED (0-180), and mind the unit: this library
// works in degrees, so AngleToDegrees is what feeds Rotate / VText.Angle.
double deg = new VXYZ(-1, 0).AngleToDegrees(VXYZ.BasisX);   // 180
double rad = new VXYZ(-1, 0).AngleToRadians(VXYZ.BasisX);   // 3.14159...
// The obsolete AngleTo returns RADIANS. text.Angle = dir.AngleTo(VXYZ.BasisX)
// on a reversed direction assigns 3.14 DEGREES - a label that looks very
// slightly crooked instead of turned right round.

// Unsigned means a direction 45 degrees up and one 45 degrees down both answer
// 45. To orient something ALONG a 2D direction, use Atan2 and keep the sign:
var dir = new VXYZ(3, -4);
double heading = System.Math.Atan2(dir.Y, dir.X).ToDegrees();   // -53.13

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

                { "LineTypePatterns", @"// LineTypePatterns is the ONE definition of what each LineType looks like.
// You do not need it to draw -- set shape.LineType and shape.LineTypeScale.
// Reach for it when you are writing your own exporter or sink and want dashes
// that match exactly what the canvas shows.
// Add `using C2VGeometry.Rendering;` at the top of the file.

foreach (LineType t in System.Enum.GetValues<LineType>())
{
    // Always ask this first: Continuous has no pattern, and a zero, negative or
    // non-finite scale is treated as solid (zero-length runs draw as nothing).
    if (LineTypePatterns.IsSolid(t, 1.0))
    {
        VizConsole.Log($""{t}: solid"");
        continue;
    }

    // Alternating dash, gap, dash, gap ... in DEVICE PIXELS at scale 1.
    // The span is over a SHARED array -- read it, never write to it.
    var runs = LineTypePatterns.DevicePixels(t);

    var parts = new System.Collections.Generic.List<string>();
    foreach (var run in runs) parts.Add(run.ToString());

    VizConsole.Log($""{t}: {string.Join("", "", parts)}"");
}

// Scale into your own buffer; ClampScale folds a caller's value into
// [MinScale, MaxScale] and returns 1.0 for a non-finite or non-positive one.
var scale = LineTypePatterns.ClampScale(2.5);
VizConsole.Log($""scale {scale}, clamps are {LineTypePatterns.MinScale} .. {LineTypePatterns.MaxScale}"");

var dashed = LineTypePatterns.DevicePixels(LineType.Dashed);   // 8, 4
var scaled = new double[dashed.Length];
for (int i = 0; i < dashed.Length; i++) scaled[i] = dashed[i] * scale;   // 20, 10" },

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
a.CopyStyleTo(merged);           // copy Color/FillColor/LineWeight/LineType/LineTypeScale/ZIndex
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

// Draw order — ZIndex is global: higher draws on top, ties keep creation order
var backdrop = new VRectangle(new VXYZ(-50, -50), 100, 100) { ZIndex = -1 };
var label    = new VText(new VXYZ(0, 0), ""on top"") { ZIndex = 10 };
var ordinary = new VCircle(0, 0, 20);   // ZIndex 0, so between the two — even
                                        // though it was created last

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

                { "Canvas", @"// Canvas.Clear() is for when the SET of shapes changes, not merely their positions.
// Here the number of rings depends on the cursor, so the scene has to be rebuilt.
Mouse.OnMove(e =>
{
    Canvas.Clear();

    var rings = (int)(e.X / 40);
    for (var i = 1; i <= rings; i++)
        new VCircle(new VXYZ(0, 0), i * 20) { Color = ""Cyan"" };
});

// When only POSITIONS change, do not clear. Build once and assign - it allocates
// nothing per event and is much faster:
//
//   var dot = new VCircle(new VXYZ(0, 0), 10);
//   Mouse.OnMove(e => dot.Center = e.Position);

// Remove named shapes instead of everything:
var a = new VCircle(new VXYZ(0, 0), 5);
var b = new VText(new VXYZ(0, 20), ""label"");
Canvas.Remove(a, b);" },
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
keeper.ZIndex = 1;                  // calls DefaultRegistry.NotifyOrderChanged,
                                    // and the host re-sorts before the next paint" },

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

// Sweeps — degrees again. Both honour the DIRECTION of travel and sweeps
// written past the wrap, so they agree with VArc/VEllipse StartAngle–EndAngle.
bool on   = GeometryHelper.SweepContains(0, 90, 45);              // true
bool back = GeometryHelper.SweepContains(90, 0, 45);              // true — clockwise
bool wrap = GeometryHelper.SweepContains(350, 370, 5);            // true — 5 is 365 here
bool off  = GeometryHelper.SweepContains(350, 370, 180);          // false — 20° sweep

double along = GeometryHelper.SweepOffset(0, 90, 45);             // 45
double cw    = GeometryHelper.SweepOffset(90, 0, 45);             // -45, signed
double past  = GeometryHelper.SweepOffset(0, 90, 200);            // 90 — clamped

// Which is how a world angle becomes a parameter on a PARTIAL curve:
var sliver = new VArc(new VXYZ(0, 0), 60, 350, 370) { Name = ""sliver"" };
double t = GeometryHelper.SweepOffset(sliver.StartAngle, sliver.EndAngle, 5)
         / (sliver.EndAngle - sliver.StartAngle);                 // 0.75
VXYZ atFive = sliver.Evaluate(t);

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
bool rawX  = CurveIntersection.IsPolylineSelfIntersecting(polyline.Points);

// VRay and VXLine are converted to the finite segment spanning their
// RenderExtent and re-dispatched, so they reach the exact routines above
// instead of being sampled — and their reach is RenderExtent, not infinity.
var ray = VRay.AtAngle(new VXYZ(0, 0), 30);
ray.Remove();                                   // a query, not part of the drawing
IntersectionResult rayHits = CurveIntersection.Intersect(ray, circle);
VizConsole.Log($""ray crossings: {rayHits.Points.Count}"");

// The Shape-typed pair asks the same engine one level up. Note the cast:
// on a concrete curve, line.Intersect(circle) binds to Intersect(ICurve).
bool touching = line.DoesIntersect(circle);            // no cast needed
Shape materialised = ((Shape)line).Intersect(circle);  // VPoint, or VGroup of VPoints
materialised?.Place();                                 // queries do not draw their answer" },

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

                { "Mouse", @"// One function per event - the JavaScript idiom. Register from Main():
// handlers are dropped at the start of every run, so they are simply
// re-registered each time you press Run.
var cursor  = new VCircle(new VXYZ(0, 0), 8) { Color = ""Yellow"", Name = ""cursor"" };
var readout = new VText(-380, 260, """", 14) { Name = ""readout"" };
var box     = new VRectangle(new VXYZ(-60, -40), 120, 80) { Name = ""box"" };

// The world is Y-up with (0, 0) at the centre of the canvas, so e.Position goes
// straight into geometry. It is grid-snapped while Snap to Grid (F9) is on;
// e.RawPosition is the true cursor position either way.
Mouse.OnMove(e =>
{
    cursor.Center = e.Position;
    readout.Content = $""{e.X:F1}, {e.Y:F1}  over {e.Target?.Name ?? ""empty space""}"";
});

// OnClick is synthesised from a down/up pair within a few pixels, so a drag
// produces no click. OnDoubleClick arrives INSTEAD OF OnDown on the second click.
Mouse.OnDown(e => VizConsole.Log($""{e.Button} down at {e.Position}""));
Mouse.OnClick(e => new VCircle(e.Position, 20) { FillColor = e.Shift ? ""Red"" : ""Cyan"" }.Place());
Mouse.OnDoubleClick(e => VizConsole.Log(""double click - OnDown did not fire""));

// OnDrag replaces OnMove while a button is held; it does NOT fall back to OnMove.
Mouse.OnDrag(e => new VPoint(e.Position) { Color = ""Lime"" });
Mouse.OnUp(e => VizConsole.Log(""gesture finished""));

// REGISTERING THIS is what takes the wheel from the canvas. Without a wheel
// handler the canvas goes on zooming, however many other handlers are attached.
Mouse.OnWheel(e => cursor.Radius = Math.Max(2, cursor.Radius + e.WheelNotches));
// Mouse.OnWheel(null);           // hands wheel zoom straight back to the canvas

Mouse.OnEnter(e => cursor.IsVisible = true);
Mouse.OnLeave(e => cursor.IsVisible = false);

// ASSIGNING REPLACES, IT DOES NOT ADD - this leaves one move handler, the second.
Mouse.OnMove(e => cursor.Center = e.RawPosition);
Mouse.OnUp(null);                 // and null detaches one

VizConsole.Log($""interactive: {Mouse.HasHandlers}"");        // true - any handler does it
VizConsole.Log($""wheel is mine: {Mouse.HasWheelHandler}"");  // true only after OnWheel

// X / Y / IsDown are tracked even with no handler registered, so a Frame loop
// (or a sketch's Draw()) can just poll them.
void Trail(double t)
{
    if (Mouse.IsDown) new VPoint(new VXYZ(Mouse.X, Mouse.Y)) { Color = ""Orange"" };
    if (t < 20.0) Frame.Request(Trail);
}
Frame.Request(Trail);

// Registering anything puts the canvas into interactive mode: selection and
// double-click-zoom-to-fit are suppressed and the F4 properties panel is hidden.
// WHEEL ZOOM IS NOT PART OF THAT - only OnWheel takes the wheel. Middle-drag
// still pans, zoom controls fade in at the top-right of the viewport cell under
// the pointer in either mode, and the P/L/C/R tools and the measuring tape keep
// priority while armed.
// A handler that throws detaches them all and reports once via CallbackFailed.
// Mouse.Clear();                 // detach everything by hand" },

                { "MouseInfo", @"// MouseInfo is the event object your handler receives - a fresh instance per
// event, so it is safe to keep one and compare it with the next.
Mouse.OnDown(e =>
{
    VizConsole.Log($""{e.Kind}: {e.Button}, ClickCount {e.ClickCount}"");

    // Position is grid-snapped while Snap to Grid (F9) is on; RawPosition never is
    VizConsole.Log($""world {e.Position}   raw {e.RawPosition}"");
    VizConsole.Log($""screen {e.ScreenX:F0}, {e.ScreenY:F0} (pixels, Y down)"");

    // Modifiers and held buttons are plain bools - no WPF types on this class
    if (e.Shift && e.Ctrl)                 VizConsole.Log(""shift+ctrl"");
    if (e.LeftDown || e.RightDown)         VizConsole.Log(""a button is still held"");
    if (e.MiddleDown)                      VizConsole.Log(""middle held - the canvas pans"");
    if (e.Alt)                             VizConsole.Log(""alt"");
});

// Target answers ""what would clicking here have picked?"" - it uses the selection
// tool's few-pixel tolerance, is computed on first read and cached, and can lag a
// fast-moving shape during animation. Use Shape.Contains for a strict interior test.
var box = new VRectangle(new VXYZ(-60, -40), 120, 80) { Name = ""box"" };

Mouse.OnMove(e =>
{
    box.FillColor = e.Target == box ? ""SteelBlue"" : ""Transparent"";

    bool strictlyInside = box.Contains(e.RawPosition);

    // Scale is screen pixels per world unit, so this reads ""within 8 pixels""
    bool nearEdge = box.DistanceTo(e.RawPosition) < 8 / e.Scale;

    VizConsole.Log($""inside {strictlyInside}, near edge {nearEdge}"");
});

// WheelDelta is raw WPF units (120 per notch); WheelNotches is the friendly form.
// Registering this handler is also what stops the canvas zooming on the wheel.
Mouse.OnWheel(e => VizConsole.Log($""{e.WheelNotches} notches (delta {e.WheelDelta})""));

// There is no Handled property: the canvas's competing gestures are suppressed
// wholesale rather than per event - selection and double-click-zoom-to-fit by any
// handler, wheel zoom by OnWheel - so there is nothing left to cancel." },

                { "MouseButtonKind", @"// MouseButtonKind is which button a MouseInfo is ABOUT - read it from e.Button.
Mouse.OnDown(e =>
{
    string what = e.Button switch
    {
        MouseButtonKind.Left     => ""left"",
        MouseButtonKind.Right    => ""right"",
        MouseButtonKind.Middle   => ""middle (a middle DRAG still pans the canvas)"",
        MouseButtonKind.XButton1 => ""side button 1"",
        MouseButtonKind.XButton2 => ""side button 2"",
        _                        => ""no button""     // MouseButtonKind.None
    };
    VizConsole.Log($""{what} at {e.Position}"");
});

// On a move, wheel, enter or leave the button is None - ask what is HELD instead
Mouse.OnDrag(e =>
{
    if (e.Button != MouseButtonKind.None) VizConsole.Log(""not reached on a drag"");
    if (e.LeftDown)  new VPoint(e.Position) { Color = ""Cyan"" };
    if (e.RightDown) new VPoint(e.Position) { Color = ""Red"" };
});" },

                { "MouseEventKind", @"// MouseEventKind lets one method serve several callbacks: switch on e.Kind.
void Report(MouseInfo e)
{
    string note = e.Kind switch
    {
        MouseEventKind.Move        => ""moved, no button held"",
        MouseEventKind.Down        => ""button pressed"",
        MouseEventKind.Up          => ""button released"",
        MouseEventKind.Click       => ""synthesised from a down/up in the same place"",
        MouseEventKind.DoubleClick => ""second click - delivered instead of Down"",
        MouseEventKind.Drag        => ""moved with a button held - instead of Move"",
        MouseEventKind.Wheel       => $""wheel, {e.WheelNotches} notches"",
        MouseEventKind.Enter       => ""pointer entered the canvas"",
        MouseEventKind.Leave       => ""pointer left the canvas"",
        _                          => ""unknown""
    };
    VizConsole.Log($""{e.Kind}: {note}"");
}

// The same method registered for every event - assignment replaces, so each of
// these is the one and only handler for its own event.
Mouse.OnMove(Report);
Mouse.OnDown(Report);
Mouse.OnUp(Report);
Mouse.OnClick(Report);
Mouse.OnDoubleClick(Report);
Mouse.OnDrag(Report);
Mouse.OnWheel(Report);
Mouse.OnEnter(Report);
Mouse.OnLeave(Report);" },

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

                { "ObjectPropertyAnimation", @"// Animates a numeric property on an ARBITRARY object - T : class, not just Shape.
// Nothing is auto-drawn, because Animation.Target is null here: your property
// setter is what actually moves the geometry.

// Declare the class at file scope - C# has no local classes, so this goes
// outside Main() (in StartViz.cs or any other .cs file in the project).
class Wheel
{
    public VCircle Hub   = new VCircle(new VXYZ(0, 0), 40);
    public VLine   Spoke = new VLine(0, 0, 40, 0);

    private double _rotation;
    public double Rotation                     // must be a writable double
    {
        get => _rotation;
        set                                    // the setter is what redraws
        {
            _rotation = value;
            Spoke.End = new VXYZ(40 * Math.Cos(value.ToRadians()),
                                 40 * Math.Sin(value.ToRadians()));
        }
    }
}

// ... and in Main():
var wheel = new Wheel();
var anim  = new Animator();

// Rotation runs 0 -> 360 over one second, looping. The start value is applied
// immediately at construction, so the wheel is already at 0 before playback.
anim.AddToAnimations(new ObjectPropertyAnimation<Wheel>(wheel, w => w.Rotation, 0.0, 360.0, 1.0));
anim.Repeat = true;
anim.Animate();

// The selector must be a plain property access on T: w => w.Rotation.
// A field, a method call, or w => w.Rotation * 2 all throw ArgumentException." },

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
//   RegionBooleanOps.Union(r1, r2, r3)" },
                // ---- DoodleSharp.Canvas types that are reachable in this tree via
                // DocGenerator.AllowedInternalTypes.

                { "SvgExporter", @"// SvgExporter turns whatever is on the canvas into an SVG document.
// It is a STATIC class in DoodleSharp.Canvas -- nothing to construct, and no
// Width/Height properties; everything is an argument.
// Add `using DoodleSharp.Canvas;` at the top of the file.

new VCircle(0, 0, 60) { Name = ""disc"", FillColor = ""#4000FFFF"" };
new VRectangle(-90, -70, 180, 140) { Name = ""frame"" };
new VText(new VXYZ(0, 90), ""hello"") { Name = ""label"" };

// GetShapes() hands back IReadOnlyList<IDrawable>, which is exactly what it takes.
var shapes = CanvasRenderer.Instance.GetShapes();

// width and height are the <svg> element's size in PIXELS.
// padding is in WORLD units: it widens the shapes' own bounds before the viewBox
// is computed, so it does not scale with width/height.
string svg  = SvgExporter.Export(shapes);                          // 800 x 600, padding 20
string wide = SvgExporter.Export(shapes, 1600, 1200, padding: 40);

VizConsole.Log($""{svg.Length} characters of SVG for {shapes.Count} shapes"");

// SaveToFile takes the PATH FIRST, and always uses the default padding.
SvgExporter.SaveToFile(@""C:\temp\drawing.svg"", shapes, 1200, 900);" },

                { "SnapEngine", @"// SnapEngine answers 'what would the cursor snap to here?' over a set of shapes.
// Add `using DoodleSharp.Canvas;` at the top of the file.

new VLine(new VXYZ(-100, 0), new VXYZ(100, 0)) { Name = ""baseline"" };
new VCircle(new VXYZ(60, 40), 30) { Name = ""hole"" };

var engine = new SnapEngine();

// All eight toggles start true. They are plain properties -- SyncFromSettings()
// is the separate call that overwrites them from the app's Snap Settings.
engine.NearestSnapEnabled = false;   // stop 'anywhere on the curve' winning ties

// Perpendicular and Tangent measure from here. Leave it null and neither can fire.
engine.ReferencePoint = new VXYZ(-100, 80);

// The third argument is the canvas zoom. Tolerance is a fixed 15 SCREEN pixels
// internally, divided by scale, so snapping feels the same at any zoom.
var hit = engine.FindSnapPoint(
    new VXYZ(98, 3), CanvasRenderer.Instance.GetShapes(), 1.0);

if (hit != null)
    VizConsole.Log($""{hit.Type} at {hit.Point}, {hit.Distance:F2} away"");
else
    VizConsole.Log(""nothing within tolerance"");" },

                { "SnapType", @"// SnapType names the kind of geometry a snap point sits on.
// Add `using DoodleSharp.Canvas;` at the top of the file.

// The eight real types, listed in the order SnapEngine prefers them when more
// than one candidate is in range. None is the 'no snap' value and is never
// returned -- FindSnapPoint returns null instead.
var order = new[]
{
    SnapType.Endpoint, SnapType.Midpoint, SnapType.Center, SnapType.Intersection,
    SnapType.Perpendicular, SnapType.Tangent, SnapType.Extension, SnapType.Nearest
};

foreach (var t in order)
    VizConsole.Log(t.ToString());

// Each type has its own toggle on the engine.
var engine = new SnapEngine();
engine.IntersectionSnapEnabled = false;
engine.ExtensionSnapEnabled = false;

new VLine(new VXYZ(-50, -50), new VXYZ(50, 50)) { Name = ""diagonal"" };

var hit = engine.FindSnapPoint(new VXYZ(49, 51), CanvasRenderer.Instance.GetShapes(), 1.0);
VizConsole.Log(hit?.Type.ToString() ?? ""no snap"");   // Endpoint" },

                { "SnapResult", @"// A SnapResult is one snap candidate: where, what kind, and how far from the cursor.
// Add `using DoodleSharp.Canvas;` at the top of the file.

// It is plain data with a public constructor, though you normally get one back
// from SnapEngine.FindSnapPoint rather than building it.
var manual = new SnapResult(new VXYZ(10, 20), SnapType.Endpoint, 0.0);
VizConsole.Log($""{manual.Type} at {manual.Point}"");

new VLine(new VXYZ(0, 0), new VXYZ(100, 0)) { Name = ""edge"" };
new VCircle(new VXYZ(0, 80), 25) { Name = ""boss"" };

var engine = new SnapEngine { ReferencePoint = new VXYZ(120, 80) };
var hit = engine.FindSnapPoint(new VXYZ(99, 1), CanvasRenderer.Instance.GetShapes(), 1.0);

if (hit != null)
{
    // Always populated.
    VizConsole.Log($""{hit.Type} at {hit.Point} ({hit.Distance:F2} away)"");

    // The rest are filled in only for the types that need them, null otherwise.
    if (hit.Type == SnapType.Extension)
        VizConsole.Log($""continues from {hit.ExtensionSource} at {hit.ExtensionAngle:F0} deg"");

    // ConstraintPoint is obsolete and was always exactly Point -- read Point.
    if (hit.Type == SnapType.Perpendicular || hit.Type == SnapType.Tangent)
        VizConsole.Log($""measured from {hit.ReferenceSource}, meets at {hit.Point}"");

    if (hit.Type == SnapType.Tangent)
        VizConsole.Log($""circle centre {hit.TangentCenter}"");
}" },

                { "DrawingTool", @"// DrawingTool is the state machine behind the Draw menu and the P/L/C/R keys.
// The canvas owns one and feeds it real mouse and key events, so project code
// rarely touches it -- but it is public and self-contained, so it can be driven
// directly, which is the clearest way to see what the tool actually does.
// Add `using DoodleSharp.Canvas;` at the top of the file.

var tool = new DrawingTool();
tool.ShapeCompleted += (sender, shape) => VizConsole.Log($""finished a {shape.GetType().Name}"");

tool.SetMode(DrawingMode.Line);        // arms it and clears any points in progress
tool.IsOrthoMode = true;               // what holding Shift does

tool.OnLeftClick(new VXYZ(-80, -40));  // first point
tool.OnLeftClick(new VXYZ(80, -40));   // second completes the VLine and raises ShapeCompleted
                                       // (the shape auto-registers, so it is on the canvas)

// Polygon, Polyline and Spline collect clicks until a DOUBLE-click ends them.
tool.IsOrthoMode = false;
tool.SetMode(DrawingMode.Polyline);
tool.OnLeftClick(new VXYZ(-60, 20));
tool.OnLeftClick(new VXYZ(0, 60));
tool.OnLeftClick(new VXYZ(60, 20));
tool.OnDoubleClick(new VXYZ(60, 20));

VizConsole.Log(tool.StatusMessage);    // ""Polyline: Click point 1"" -- the points were consumed
tool.Cancel();                         // what Esc does
VizConsole.Log(tool.StatusMessage);    // ""Ready""" },

                { "DrawingMode", @"// DrawingMode says which shape DrawingTool is building.
// Add `using DoodleSharp.Canvas;` at the top of the file.

var tool = new DrawingTool();
tool.ShapeCompleted += (sender, shape) => VizConsole.Log($""drew {shape.GetType().Name}"");

// Centre, then a point at the RADIUS distance.
tool.SetMode(DrawingMode.Circle);
tool.OnLeftClick(new VXYZ(0, 0));
tool.OnLeftClick(new VXYZ(50, 0));

// Same two clicks, but the distance is read as the DIAMETER.
tool.SetMode(DrawingMode.CircleDiameter);
tool.OnLeftClick(new VXYZ(140, 0));
tool.OnLeftClick(new VXYZ(190, 0));

// Here the two clicks are opposite ENDS of a diameter.
tool.SetMode(DrawingMode.CircleTwoPoints);
tool.OnLeftClick(new VXYZ(-160, 0));
tool.OnLeftClick(new VXYZ(-60, 0));

// Three points on the circumference.
tool.SetMode(DrawingMode.CircleThreePoints);
tool.OnLeftClick(new VXYZ(0, 120));
tool.OnLeftClick(new VXYZ(40, 160));
tool.OnLeftClick(new VXYZ(-40, 160));

tool.SetMode(DrawingMode.None);        // idle again" },

                { "DrawingInputMode", @"// DrawingInputMode is the keyboard side of drawing: type an exact distance or
// angle instead of clicking a position.
// Add `using DoodleSharp.Canvas;` at the top of the file.

var tool = new DrawingTool();
tool.SetMode(DrawingMode.Line);
tool.OnLeftClick(new VXYZ(0, 0));   // nothing engages until a point is down

tool.CycleInputMode();              // Tab: None -> Distance
VizConsole.Log(tool.InputMode.ToString());        // Distance

tool.HandleCharInput('1');          // digits, one '.', and a leading '-' are accepted
tool.HandleCharInput('2');
tool.HandleCharInput('0');
VizConsole.Log(tool.GetInputDisplayText());       // ""Distance: 120_""

tool.HandleEnterInput();            // commits, and leaves input mode
VizConsole.Log($""{tool.OverrideDistance}"");       // 120

// The override survives Enter and is consumed by the next click, which lands
// exactly 120 units from the first point along the cursor direction.
tool.HandleEscapeInput();           // ...or throw it away: Esc clears the overrides
tool.Cancel();" },

                { "GlyphOutlineProvider", @"// GlyphOutlineProvider is the app's WPF implementation of IGlyphOutlineProvider.
// C2VGeometry has no font engine, so the host installs one at startup and every
// glyph-to-shape call routes through it. You do not call it directly.

var word = new VText(new VXYZ(-120, 0), ""GEO"") { Height = 60, Name = ""word"" };

// Installed? (null means ToCharShape/LiftChar/LiftChars all return null.)
VizConsole.Log(VText.GlyphOutlineProvider == null ? ""no provider"" : ""provider ready"");

// Non-mutating: a copy of character 0 as a shape, leaving the text alone.
var g = word.ToCharShape(0);
if (g != null) { g.Color = ""Yellow""; g.Place(); }

// LiftChar extracts the glyph AND blanks it in the text, so the character moves
// out of the string and becomes independent geometry you can animate.
var lifted = word.LiftChar(2);
if (lifted != null) { lifted.Move(new VXYZ(0, -80)); lifted.Color = ""Magenta""; }

// A glyph with holes (O, A, 8) comes back as a VGroup of contour polylines;
// a simple glyph comes back as a single closed VPolyline." },
                { "Sketch", @"// A sketch is the alternative to writing Main(): subclass Sketch, override
// Setup() and Draw(), and DoodleSharp runs the frame loop.
// Add `using DoodleSharp.Sketching;` at the top of the file.

public class MySketch : Sketch
{
    // Persistent state lives in FIELDS. The registered shapes are cleared
    // between frames, so anything drawn in Draw() is built fresh each time.
    private double _angle;
    private VCircle _sun;

    public override void Setup()
    {
        Size(800, 600);            // logical drawing area; zooms the canvas to fit
        Background(""#101018"");     // canvas colour for the running sketch

        // A shape kept in a field survives the per-frame clear only because we
        // re-Place() it below - the registry is emptied, not the object.
        _sun = new VCircle(new VXYZ(0, 0), 40) { FillColor = ""Gold"", Color = ""Orange"" };
    }

    public override void Draw()
    {
        _angle += 90 * DeltaSeconds;               // degrees per second

        _sun.Place();                              // put the kept shape back on the canvas

        // Fresh geometry every frame - the usual sketch idiom.
        var x = 200 * Math.Cos(_angle * Math.PI / 180);
        var y = 200 * Math.Sin(_angle * Math.PI / 180);
        new VCircle(new VXYZ(x, y), 18) { FillColor = ""SkyBlue"" };
        new VLine(new VXYZ(0, 0), new VXYZ(x, y)) { Color = ""#404060"" };

        // Per-frame state the runtime fills in before each call.
        new VText(-380, 260, $""frame {FrameCount}  t={ElapsedSeconds:F1}s"", 14);

        // Polled mouse input - no handler to register.
        if (MousePressed)
            new VPoint(new VXYZ(MouseX, MouseY)) { Color = ""Lime"" };

        // KeyPressed and LastKey are declared but nothing writes them: they stay
        // false and """" in every sketch. Use Mouse.OnDown for real input instead.

        if (FrameCount > 600) NoLoop();            // pause; Loop() resumes
    }
}" },

                // ---- C2VGeometry.Rendering: the shape-to-primitive seam. Renderer plumbing --
                // to draw a shape, just construct it -- but public, and the floor a custom
                // exporter or a custom analysis pass (measuring, counting, reducing) is built on.

                { "ShapeTessellator", @"// ShapeTessellator is the one place a Shape becomes primitives -- polylines,
// filled loops, points and text -- pushed into an IPrimitiveSink. Construct
// one and reuse it across shapes; it holds scratch buffers and is
// deliberately NOT thread-safe, so give each thread its own.
// Add `using C2VGeometry.Rendering;` at the top of the file.

var tessellator = new ShapeTessellator();
var bounds = new BoundsPrimitiveSink();      // any IPrimitiveSink will do here

var disc = new VCircle(0, 0, 50) { Name = ""disc"" };

// Tessellate RETURNS BOOL, and the value is not optional: false means the
// sink declined the shape, or this tessellator has no primitives for it at
// all -- ignoring it is how dimensions and construction lines silently
// vanish from a custom export.
bool handled = tessellator.Tessellate(disc, bounds);
VizConsole.Log(handled
    ? $""bounds ({bounds.MinX:F0}, {bounds.MinY:F0}) to ({bounds.MaxX:F0}, {bounds.MaxY:F0})""
    : ""declined"");

// The curve-flattening rule it uses, exposed so a caller can match it.
int segments = ShapeTessellator.SegmentsForRadius(radiusPixels: 40);" },

                { "IPrimitiveSink", @"// IPrimitiveSink is where ShapeTessellator sends the primitives it produces --
// implement it to consume the geometry library's output in your own format.
// Renderer plumbing, not scripting API: to draw a shape, just construct it.
// Add `using C2VGeometry.Rendering;` at the top of the file.

// Declare the class at file scope - C# has no local classes, so this goes
// outside Main() (in StartViz.cs or any other .cs file in the project).
class PrimitiveCounter : IPrimitiveSink
{
    public int Strokes, Fills, Points;
    public TessellationHints Hints { get; } = new TessellationHints { Scale = 1.0 };

    public bool BeginShape(Shape shape, in PenSpec pen) => true;   // accept every shape
    public void EndShape() { }
    public void EmitPolyline(IReadOnlyList<VXYZ> points, bool closed) => Strokes++;
    public void EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule) => Fills++;
    public void EmitPoint(VXYZ point) => Points++;
    public void EmitText(VText text) { }
    // TryEmitNative is a default interface member returning false -- leave it
    // alone unless this sink has a native form of its own for some shape.
}

// ... and in Main():
var counter = new PrimitiveCounter();
new ShapeTessellator().Tessellate(new VCircle(0, 0, 40), counter);
VizConsole.Log($""{counter.Strokes} strokes, {counter.Fills} fills, {counter.Points} points"");" },

                { "PenSpec", @"// PenSpec is everything a renderer needs to paint one shape, lifted out of it
// so a sink never has to reach back into Shape. Build one with
// PenSpec.From(shape), not the constructor.
// Add `using C2VGeometry.Rendering;` at the top of the file.

var ring = new VCircle(0, 0, 50) { Color = ""Cyan"", FillColor = ""Transparent"" };
var pen = PenSpec.From(ring);
VizConsole.Log($""{pen.Color}, weight {pen.LineWeight}, filled={pen.HasFill}"");   // filled=False

// HasFill treats """", ""Transparent"" and ""None"" (case-insensitively) as no
// fill -- the check a sink should make before filling anything.
var disc = new VCircle(120, 0, 50) { FillColor = ""#4000FFFF"" };
VizConsole.Log($""filled={PenSpec.From(disc).HasFill}"");   // filled=True" },

                { "FillRule", @"// FillRule decides what counts as 'inside' a filled outline, for
// IPrimitiveSink.EmitFilledLoops. It only affects how a SINK paints what it
// is given -- not the library's own boolean operations, which pick a fill
// rule for themselves internally (PolygonClipper).
// Add `using C2VGeometry.Rendering;` at the top of the file.

FillRule outerPlusHoles = FillRule.EvenOdd;   // the default (0): a loop inside
                                               // a loop is always a hole
FillRule byWinding      = FillRule.NonZero;   // (1): a hole only if it winds
                                               // opposite to the outer loop

VizConsole.Log($""{(int)outerPlusHoles} {(int)byWinding}"");   // 0 1" },

                { "TessellationHints", @"// TessellationHints controls how finely curves are flattened, carried on
// every IPrimitiveSink.
// Add `using C2VGeometry.Rendering;` at the top of the file.

var hints = new TessellationHints
{
    // SCREEN PIXELS PER WORLD UNIT - the view's zoom, so 2.0 is 200%.
    // Segment counts are chosen from a shape's size in PIXELS, so the same
    // radius-1 circle needs MORE segments zoomed in (large Scale) and fewer
    // zoomed out. Multiply a world size by Scale to get its size on screen.
    Scale = 2.0,

    // Set by a sink that can express a circle AS a circle (DXF, SVG, PDF):
    // when true, the tessellator offers TryEmitNative first and only
    // flattens what the sink declines.
    PreferNative = true,
};

// A world radius of 30, at this zoom, is 60 pixels across the screen.
int segments = ShapeTessellator.SegmentsForRadius(radiusPixels: 30 * hints.Scale);" },

                { "BoundsPrimitiveSink", @"// BoundsPrimitiveSink measures instead of drawing: feed shapes through
// ShapeTessellator and it accumulates a bounding box over everything it
// sees. This is exactly what zoom-to-extents uses -- measuring through the
// tessellator sees precisely what the renderer draws.
// Add `using C2VGeometry.Rendering;` at the top of the file.

var sink = new BoundsPrimitiveSink();
var tessellator = new ShapeTessellator();

Shape[] scene = { new VCircle(-60, 0, 30), new VRectangle(20, -20, 80, 60) };
foreach (var shape in scene)
{
    if (!tessellator.Tessellate(shape, sink))
        sink.IncludeBounds(shape);   // fold in a shape the tessellator declined,
                                      // using the shape's own GetBounds()
}

if (sink.HasBounds)   // false until something has actually been added
    VizConsole.Log($""({sink.MinX:F0}, {sink.MinY:F0}) to ({sink.MaxX:F0}, {sink.MaxY:F0})"");

sink.Reset();   // ready to reuse for the next query

// For a single shape, prefer shape.GetBounds() -- this is for measuring a set." },

                { "PolylineFallbackSink", @"// PolylineFallbackSink reduces any shape to plain polylines and filled loops --
// the floor a custom exporter falls back to for a type it has no native
// form for. It is what each shape is reduced to when nothing more specific
// applies; the exporter's own switch stays responsible for its native forms
// (a circle should stay a CIRCLE entity in DXF, not become sixty-four chords).
// Add `using C2VGeometry.Rendering;` at the top of the file.

var sink = new PolylineFallbackSink();
sink.OnPolyline = (points, closed, pen)
    => VizConsole.Log($""{points.Count} points, closed={closed}, color={pen.Color}"");
sink.OnFilled = (loops, pen)
    => VizConsole.Log($""{loops.Count} loop(s) filled with {pen.FillColor}"");

var tessellator = new ShapeTessellator();
var wedge = new VPolygon(new VXYZ(0, 0), new VXYZ(80, 0), new VXYZ(40, 60))
{
    FillColor = ""#4000FFFF""
};

if (!tessellator.Tessellate(wedge, sink))
    sink.Unhandled.Add(wedge);   // the sink does NOT fill this in for you --
                                  // the caller appends whenever Tessellate returns false

VizConsole.Log(sink.Unhandled.Count == 0 ? ""export complete"" : $""{sink.Unhandled.Count} shape(s) dropped"");
sink.Reset();   // clears Unhandled between runs" },

                // ---- DoodleSharp.Console: the collector behind the console panel.

                { "ConsoleOutput", @"// ConsoleOutput is the singleton behind the console panel -- the collector
// VizConsole.Log writes into. You almost never construct or call this
// directly; VizConsole.Log is the scripting API and captures the calling
// file/line for you, which this does not.
// Add `using DoodleSharp.Console;` at the top of the file.

VizConsole.Log(""first line"");
VizConsole.Log(""second line"");

var log = ConsoleOutput.Instance;
foreach (var entry in log.GetEntries())
    VizConsole.Log($""[{entry.ModuleName}:{entry.LineNumber}] {entry.Message}"");

// A custom entry with a clickable source location:
log.AddEntry(""custom note"", filePath: @""C:\proj\StartViz.cs"", lineNumber: 12);

string dump = log.GetFormattedOutput();   // handy for copying a run's log elsewhere
log.Flush();                              // force the panel to catch up right now
// log.Clear();                           // wipes the panel" },

                { "ConsoleEntry", @"// ConsoleEntry is one line in the console panel -- plain data, returned from
// ConsoleOutput.GetEntries() rather than constructed directly.
// Add `using DoodleSharp.Console;` at the top of the file.

VizConsole.Log(""hello"");
ConsoleOutput.Instance.WriteError(""StartViz"", 42, ""oops"");   // rendered in the error colour

foreach (ConsoleEntry entry in ConsoleOutput.Instance.GetEntries())
{
    string kind = entry.IsError ? ""ERROR"" : ""info"";
    VizConsole.Log($""{kind} {entry.ModuleName}:{entry.LineNumber} {entry.Message}"");

    // IsClickable is COMPUTED, not set -- true only when both a FilePath and
    // a LineNumber > 0 are present, which is what makes a console line jump
    // to the code when you click it.
    if (entry.IsClickable)
        VizConsole.Log($""  -> {entry.FilePath}:{entry.LineNumber}"");
}" },

                // ---- DoodleSharp.Export: file formats, reached in the app through
                // File > Export. Callable directly for a scripted batch export.

                { "DxfExporter", @"// DxfExporter writes AutoCAD-compatible DXF (R12 ASCII). Shapes with a
// native DXF equivalent keep it -- a VCircle becomes a CIRCLE entity, not
// sixty-four chords -- and everything else is decomposed into polylines
// rather than being silently dropped.
// Add `using DoodleSharp.Export;` and `using DoodleSharp.Canvas;`.

new VCircle(0, 0, 50) { Name = ""hole"" };
new VRectangle(-80, -60, 160, 120) { Name = ""frame"" };

var shapes = CanvasRenderer.Instance.GetShapes();   // IReadOnlyList<IDrawable>

var dxf = new DxfExporter();
dxf.Export(shapes, @""C:\temp\drawing.dxf"");         // one drawing unit = one DXF unit, Y up

string text = dxf.ExportToString(shapes);           // ...or get the DXF text itself
VizConsole.Log($""{text.Length} characters of DXF for {shapes.Count} shapes"");" },

                { "PdfExporter", @"// PdfExporter writes real vector PDF (via PdfSharp) -- colours, line weights
// and dash patterns all survive, unlike a screenshot.
// Add `using DoodleSharp.Export;` and `using DoodleSharp.Canvas;`.

new VCircle(0, 0, 50) { Color = ""Tomato"", Name = ""disc"" };
new VRectangle(-80, -60, 160, 120) { Name = ""frame"" };

var shapes = CanvasRenderer.Instance.GetShapes();

var pdf = new PdfExporter();
pdf.Export(shapes, @""C:\temp\drawing.pdf"");   // page auto-sized to the drawing

// Or choose the sheet yourself. Everything is an argument -- there is no
// PageSize or Margin property: page size in mm (0 for either = auto-size to
// content), the plot scale as mm of paper per drawing unit, and the margin.
pdf.Export(shapes, @""C:\temp\a4.pdf"",
    pageWidthMm: 297, pageHeightMm: 210, scaleMmPerUnit: 1.0, marginMm: 10);" },

                { "GifEncoder", @"// GifEncoder writes an animated GIF, one frame at a time, to any Stream.
// Frame delay and looping are CONSTRUCTOR ARGUMENTS, not properties, and the
// file only becomes a valid GIF once Dispose() runs -- always wrap it in a
// using statement. Every frame must match the width/height given here.
// Add `using DoodleSharp.Export;` and
// `using System.Windows.Media.Imaging;` at the top of the file.

using var stream = System.IO.File.Create(@""C:\temp\loop.gif"");
using var gif = new GifEncoder(stream, width: 200, height: 200, frameDelayMs: 80, repeat: true);

for (int i = 0; i < 10; i++)
{
    var frame = new RenderTargetBitmap(200, 200, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    // ... render your own visual into `frame` here, in order, one call per frame ...
    gif.AddFrame(frame);
}
// Dispose() (the `using` above) writes the trailer -- nothing is a valid GIF
// on disk before that runs.

// In practice you reach this through File > Export > GIF in the app, which
// drives the per-frame capture for you." },

                // Viewports
                { "Viewport", @"// The canvas pane is a grid of viewports, and every cell is one in its own
// right. Split the root with Rows and Columns, then place shapes per cell.
// 0-BASED, ROW FIRST. The layout resets to 1x1 at the start of every run, so
// these lines belong in Main() and simply re-run each time you press F5.
Viewports.Rows = 2;
Viewports.Columns = 3;

new VCircle(new VXYZ(0, 0), 40).Place(Viewports[0][0]);
new VLine(new VXYZ(-40, -40), new VXYZ(40, 40)).Place(Viewports[1][2]);

// Any cell subdivides again, to any depth. That is how you get an uneven
// layout: one big view beside a stack of small ones is just a split cell.
Viewport right = Viewports[0][2];
right.Rows = 3;
new VPolygon(new VXYZ(0, 0), new VXYZ(30, 0), new VXYZ(15, 25)).Place(right[1][0]);

// Sizing is XAML's grid-length spelling, as a string. Height belongs to the
// ROW and Width to the COLUMN, so every cell in a row reports the same Height.
Viewports[0].Height = ""3*"";        // top row takes three quarters of the height
Viewports[0][2].Width  = ""4*"";      // last column takes four shares
Viewports[0][0].Width  = ""240"";     // ...and the first is fixed at 240 pixels

// Each leaf carries its own pan and zoom, so re-running does not disturb them.
foreach (var leaf in Viewport.Leaves())
    VizConsole.Log($""{leaf.Path}  depth {leaf.Depth}  row {leaf.RowIndex} col {leaf.ColumnIndex}"");

// Traps worth knowing:
//   Viewports.Height          -> InvalidOperationException. The root always
//                                fills the pane, so it has no size of its own.
//   Viewports[9]              -> ArgumentOutOfRangeException, and the message
//                                names the size you actually have.
//   Viewports[0].Height = ""Auto"" -> ArgumentException. A canvas has no natural
//                                size, so Auto would collapse the cell away.
//   Rows = 0 or Rows = 99     -> ArgumentOutOfRangeException; 1..MaxDimension (8)." },

                { "ViewportRoot", @"// You never write ViewportRoot. It exists so that `Viewports` works as a
// bare name: the compiler injects `global using static C2VGeometry.ViewportRoot;`
// into every file of your project, so this...
Viewports.Columns = 2;
new VCircle(new VXYZ(0, 0), 25).Place(Viewports[0][1]);

// ...is the same as the long form, which there is no reason to write:
C2VGeometry.ViewportRoot.Viewports.Columns = 2;

// A leaf's only cell is itself, so on the DEFAULT 1x1 layout the root and
// Viewports[0][0] are literally the same object - which is why a bare Place()
// and Place(Viewports[0][0]) mean the same thing.
Viewport.Reset();                                   // back to one undivided cell
VizConsole.Log(ReferenceEquals(Viewports, Viewports[0][0]).ToString());   // True" },

                { "ViewportRow", @"// Viewports[row] hands back a ViewportRow: index it again for the cell, or
// set its Height to size the whole row.
Viewports.Rows = 3;
Viewports.Columns = 2;

Viewports[0].Height = ""2*"";       // top row twice as tall as each of the others
Viewports[1].Height = ""*"";
Viewports[2].Height = ""120"";      // ...and the last one fixed at 120 pixels

// A height belongs to the ROW, not to one cell - these two lines are the same
// act, and every cell in the row reads the same value back.
Viewports[0][1].Height = ""2*"";
VizConsole.Log(Viewports[0].Height);        // 2*

// The indexer is what you use most: row first, then column.
new VText(0, 0, ""top left"", 12).Place(Viewports[0][0]);
new VText(0, 0, ""bottom right"", 12).Place(Viewports[2][1]);

// Past the last column throws, naming the size you have:
// Viewports[0][5];   ->  ArgumentOutOfRangeException" },

                { "ViewportLength", @"// The parsed form behind the Height and Width strings. Setting a size parses
// for you, so most code never names this type - but Parse is where a typo is
// caught, and the error says what was rejected.
ViewportLength share = ViewportLength.Parse(""*"");     // 1 share
ViewportLength wide  = ViewportLength.Parse(""3*"");    // 3 shares
ViewportLength fixedPx = ViewportLength.Parse(""240""); // 240 device pixels

VizConsole.Log($""{share.Value} {share.IsStar}"");        // 1 True
VizConsole.Log($""{fixedPx.Value} {fixedPx.IsStar}"");    // 240 False
VizConsole.Log(wide.ToString());                        // 3*   (canonical form)

// ViewportLength.Star is the default every row and column starts at.
VizConsole.Log((ViewportLength.Parse(""1*"") == ViewportLength.Star).ToString());   // True

// Rejected, each with a message naming the spelling and the alternatives:
//   Parse("""")     Parse(""wide"")   Parse(""-40"")   Parse(""0*"")
//   Parse(""Auto"")  - by name, because a canvas has no natural size, so an
//                   auto-sized viewport would collapse to nothing.

// A host reads the parsed form back off the tree to lay the grid out:
Viewports.Rows = 2;
Viewports[0].Height = ""3*"";
VizConsole.Log(Viewports.RowHeightAt(0).Value.ToString());    // 3" },

                { "SvgTile", @"// One cell of a divided canvas, for SvgExporter.ExportTiled. Nested inside
// SvgExporter, so it is written SvgExporter.SvgTile.
// Add `using DoodleSharp.Canvas;` and `using System.Windows;` at the top.

Viewports.Columns = 2;
new VCircle(new VXYZ(0, 0), 40).Place(Viewports[0][0]);
new VRectangle(new VXYZ(-30, -20), 60, 40).Place(Viewports[0][1]);

// One tile per leaf. GetShapes(leaf) is the per-cell shape list.
var renderer = CanvasRenderer.Instance;
var tiles = new List<SvgExporter.SvgTile>();
double x = 0;
foreach (var leaf in Viewport.Leaves())
{
    // PageRect: where this cell sits on the page, in device pixels.
    // Scale: SCREEN PIXELS PER WORLD UNIT in that cell - its zoom, the same
    // quantity as MouseInfo.Scale. PanX/PanY: that cell's pan, in pixels.
    tiles.Add(new SvgExporter.SvgTile(new Rect(x, 0, 400, 600), 1.0, 0, 0, renderer.GetShapes(leaf)));
    x += 400;
}

SvgExporter.SaveTiledToFile(@""C:\temp\layout.svg"", tiles, 800, 600);

// It is a record struct, so it destructures and compares by value:
var (rect, scale, panX, panY, shapes) = tiles[0];
VizConsole.Log($""{rect.Width} x {rect.Height} at {scale} px per world unit"");

// In the app you never build these - File > Export > SVG reads each cell's
// live view for you. Do it by hand only when driving the exporter from code." },

                { "PdfTile", @"// One cell of a divided canvas, for PdfExporter.ExportTiled. Nested inside
// PdfExporter, so it is written PdfExporter.PdfTile.
// Add `using DoodleSharp.Export;` and `using System.Windows;` at the top.

Viewports.Rows = 2;
new VCircle(new VXYZ(0, 0), 40).Place(Viewports[0][0]);
new VRectangle(new VXYZ(-30, -20), 60, 40).Place(Viewports[1][0]);

var renderer = DoodleSharp.Canvas.CanvasRenderer.Instance;
var tiles = new List<PdfExporter.PdfTile>();
double y = 0;
foreach (var leaf in Viewport.Leaves())
{
    // Same five members as SvgTile, except Shapes is IReadOnlyList<IDrawable>
    // rather than IEnumerable<IDrawable>. Scale is screen pixels per world unit.
    tiles.Add(new PdfExporter.PdfTile(new Rect(0, y, 400, 600), 1.0, 0, 0, renderer.GetShapes(leaf)));
    y += 600;
}

var exporter = new PdfExporter();
exporter.ExportTiled(tiles, @""C:\temp\layout.pdf"", containerWidth: 400, containerHeight: 1200);

// The page keeps the container's aspect ratio and the cells keep their relative
// positions, so the result is the screen on paper. There is no scaleMmPerUnit
// here: it has no meaning across cells sitting at different zooms." },

                { "VideoExporter", @"// VideoExporter writes MP4 via the Windows Media Foundation H.264 encoder --
// no external tools to install. Implements IDisposable, so wrap it in a
// using statement to finalise the file.
// Add `using DoodleSharp.Export;` and
// `using System.Windows.Media.Imaging;` at the top of the file.

using var video = new VideoExporter(@""C:\temp\clip.mp4"", width: 640, height: 480, fps: 30, bitrateMbps: 5);

for (int i = 0; i < 60; i++)   // two seconds at 30 fps
{
    var frame = new RenderTargetBitmap(640, 480, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
    // ... render your own visual into `frame` here, in order, one call per frame ...
    video.AddFrame(frame);
}
// Dispose() (the `using` above) finalises the container -- the file is not
// playable before that runs.

// In practice you reach this through File > Export > Video, which offers
// resolution presets (Canvas Size, 720p, 1080p, 4K, Custom), 15-60 FPS and
// 1-20 Mbps, and drives the frame capture for you." },
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
                { "VLine.Intersect", "Computes the intersection with another curve, returning an IntersectionResult whose Points hold every crossing — and, when the two lines are collinear and overlap, whose Curves holds the shared segment (HasOverlap is true). Exact against every straight-edged partner (VLine, VPolyline, VPolygon, VRectangle, VRay, VXLine) and against VCircle, VArc and VEllipse, which all have closed-form routines; only VBezier and VSpline are sampled. VLine also inherits Shape.Intersect(Shape), but on a VLine-typed variable this ICurve overload wins for any curve argument — you reach the Shape one by passing a non-curve such as VText, VGroup, Region or VHatch." },
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
                { "VCircle.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. Exact against VLine, VArc, another VCircle, VRay and VXLine (two coincident circles report the circle itself in Curves rather than points, so HasOverlap is true and Points is empty). Against VEllipse, VPolygon, VRectangle, VPolyline, VBezier or VSpline there is no closed form, so the circle is sampled into up to 1000 chords and the points are accurate to that sampling." },
                { "VCircle.ToString", "Returns a string representation of the circle." },

                // VXLine Properties (infinite construction line)
                { "VXLine.BasePoint", "Gets or sets the base point that the infinite line passes through." },
                { "VXLine.Direction", "Gets or sets the direction vector of the line (normalized)." },
                { "VXLine.RenderExtent", "Gets or sets the extent used for rendering (default: 10000). Points at ±RenderExtent define the visual segment." },
                { "VXLine.StartPoint", "Gets a point far in the negative direction (for rendering)." },
                { "VXLine.EndPoint", "Gets a point far in the positive direction (for rendering)." },
                { "VXLine.SelfIntersecting", "Always returns false (infinite lines cannot self-intersect)." },
                { "VXLine.Vertices", "Gets the base point as the only vertex." },


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
                { "VXLine.Intersect", "Computes the intersection with another curve, returning an IntersectionResult whose Points hold every crossing. The infinite line is treated as the finite segment from -RenderExtent to +RenderExtent about BasePoint (10000 each way by default), which is the same span Evaluate and Divide cover, so raise RenderExtent if the other shape sits further out. Exact, not sampled: `foreach (var p in xline.Intersect(circle).Points) new VPoint(p) { Color = \"Yellow\" };`" },
                { "VXLine.ToString", "Returns a string representation of the infinite line." },

                // VRay Properties (semi-infinite ray)
                { "VRay.Origin", "Gets or sets the origin point where the ray starts." },
                { "VRay.Direction", "Gets or sets the direction vector of the ray (normalized)." },
                { "VRay.RenderExtent", "Gets or sets the extent used for rendering (default: 10000)." },
                { "VRay.StartPoint", "Gets the origin (same as Origin property)." },
                { "VRay.EndPoint", "Gets a point at RenderExtent distance from origin (for rendering)." },
                { "VRay.SelfIntersecting", "Always returns false (rays cannot self-intersect)." },
                { "VRay.Vertices", "Gets the origin as the only vertex." },


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
                { "VRay.Intersect", "Computes the intersection with another curve, returning an IntersectionResult whose Points hold every crossing. The ray is treated as the finite segment from Origin out to RenderExtent (10000 by default), which is the same span Evaluate and Divide cover, so raise RenderExtent if your obstacles sit further out. Exact, not sampled: `foreach (var p in ray.Intersect(circle).Points) new VPoint(p) { Color = \"Yellow\" };`" },
                { "VRay.ToString", "Returns a string representation of the ray." },

                // VRectangle Properties (inherits from VPolygon)
                { "VRectangle.Corner", "Gets or sets the bottom-left corner point of the rectangle. Setting this updates the underlying polygon points." },
                { "VRectangle.Width", "Gets or sets the width of the rectangle (along X axis). Setting this updates the underlying polygon points." },
                { "VRectangle.Height", "Gets or sets the height of the rectangle (along Y axis). Setting this updates the underlying polygon points." },
                { "VRectangle.RotationAngle", "Gets or sets the rotation angle in degrees (counter-clockwise) of the rectangle about its own centre. Setting it rebuilds the four corner points. It OVERRIDES Shape.RotationAngle rather than shadowing it, so there is only one property: it means the same thing whether you reach the rectangle through a VRectangle-typed or a Shape-typed variable, and RotateAnimation — which writes through a Shape reference — drives the real geometry. While this was a `new` member the writer and the reader resolved to two different properties, so rotation animations on rectangles silently did nothing." },
                { "VRectangle.Area", "Inherited from VPolygon. The shoelace area, always positive regardless of vertex winding. Use SignedArea when you need the winding direction." },
                { "VRectangle.SignedArea", "Inherited from VPolygon. The shoelace area with sign: positive for counter-clockwise vertices, negative for clockwise." },
                { "VRectangle.Points", "Inherited from VPolygon. Gets the four corner vertices as a list of VXYZ." },


                // VRectangle Methods
                { "VRectangle.Draw", "Renders the rectangle to the canvas." },
                { "VRectangle.Clone", "Creates a deep copy of this rectangle with all properties duplicated." },
                { "VRectangle.Move", "Translates the rectangle by the specified displacement vector." },
                { "VRectangle.Rotate", "Rotates the rectangle about the given pivot by an angle in degrees: the CENTRE travels around the pivot, RotationAngle accumulates, and the four corner points are rebuilt — so this is real geometry, and Contains/DistanceTo follow it. It used to transform Corner instead, the UNROTATED bottom-left, which is an artefact of how a rectangle is parameterised rather than a point that stays put as the shape turns; the box was then grown from the moved corner in unrotated axes, so the rectangle came out correctly oriented and in the wrong place. A 10x4 rectangle at (2, 1) turned a quarter turn about the origin landed at (6, -1)..(2, 9) instead of (-1, 2)..(-5, 12), and rotating about the rectangle's own centre was wrong for the same reason." },
                { "VRectangle.Flip", "Mirrors the rectangle across the line you pass — the infinite line through the given VLine's Start and End, at any angle. The CENTRE is reflected (not Corner), and RotationAngle becomes 2*(the mirror line's angle) - RotationAngle, folded into [0, 180) because a rectangle is symmetric about its centre and t and t + 180 name the same four corners. Both halves used to be wrong: the rotation was left untouched, so a rectangle drawn at 30 degrees came back still at 30 rather than at its mirror image; and reflecting Corner and then growing the box right and up from it put an unrotated rectangle spanning x from 2 to 12, mirrored about the Y axis, at -2 to 8 instead of -12 to -2." },
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
                { "VArc.EndAngle", "Gets or sets the end angle in degrees, measured from the positive X axis like StartAngle. It is a plain settable value with NO normalisation: an EndAngle below StartAngle gives a clockwise arc rather than a long counter-clockwise one, and a difference beyond 360 wraps past the full circle. Sweep length is the absolute difference." },
                { "VArc.StartPoint", "Gets the starting point of the arc." },
                { "VArc.EndPoint", "Gets the ending point of the arc." },
                { "VArc.SelfIntersecting", "Always returns false (arcs cannot self-intersect)." },

                // VArc Methods
                { "VArc.Draw", "Renders the arc to the canvas." },
                { "VArc.Clone", "Creates a deep copy of this arc with all properties duplicated." },
                { "VArc.Move", "Translates the arc by the specified displacement vector." },
                { "VArc.Rotate", "Rotates the arc around the specified pivot by the given angle in degrees: Center moves around the pivot and BOTH ends shift by the same amount, so the sweep — its size and its direction — is untouched. The two angles are shifted, NOT normalised, which is what keeps an arc written as 350 to 370 a 20-degree arc after the turn. Normalising them independently, which this used to do, folded each into [0, 360) separately and rewrote that arc as 350 to 10: a 340-degree arc going the other way. Rotate(pivot, 0) was enough to trigger it, and its length, bounds, hit test and DXF output then all agreed on the wrong arc." },
                { "VArc.Flip", "Mirrors the arc across the line you pass — the infinite line through the given VLine's Start and End, at any angle, not just the horizontal. Center is reflected, and because reflecting a direction across a line at angle t maps an angle a to 2t - a, the ends become 2t - EndAngle and 2t - StartAngle: they SWAP, so a counter-clockwise arc comes back clockwise, which is what a mirror image is. It used to hardcode the t = 0 case, mirroring about the horizontal through the centre whatever line was passed, so mirroring about a vertical axis moved the centre correctly and left the arc facing the wrong way." },
                { "VArc.Scale", "Scales the arc relative to a center point by the specified factor." },
                { "VArc.GetBounds", "Returns the axis-aligned bounding box of the ARC ITSELF: its two endpoints, widened only by whichever of the four compass extremes (0, 90, 180, 270 degrees) the sweep actually passes through, tested with GeometryHelper.SweepContains so the direction of travel counts. It used to return the box of the whole CIRCLE, which is right only for a full turn and four times too large for a quarter arc — and everything downstream reads this box, so zoom-to-fit framed a circle that was not there, the cull index reserved space for it and rubber-band selection caught the arc when the rectangle was nowhere near it. A full-turn arc still bounds to the circle, because the sweep does reach all four extremes." },
                { "VArc.Contains", "Returns true when the point lies ON the arc — an arc encloses no area, so that is the only sensible reading. It is DistanceTo judged against a tolerance scaled to Radius, and it honours the sweep: a point on the circle but outside StartAngle..EndAngle returns false." },
                { "VArc.DistanceTo", "Returns the exact shortest distance from the point to the arc, honouring the sweep: when the ray from the centre through the point passes through the swept sector the distance is purely radial (|distanceToCentre - Radius|), and otherwise it is the distance to the nearer of StartPoint/EndPoint. A point at the centre returns Radius. Computed in closed form rather than by sampling, so the centre of a radius-10 half-circle measures exactly 10." },
                { "VArc.GetLength", "Returns the arc length." },
                { "VArc.Divide", "Divides the arc into equal segments, returning the division points." },
                { "VArc.MidPoint", "The point halfway along the arc — Evaluate(0.5), so it follows the sweep rather than being the midpoint of the chord. Read-only; move the arc by setting Center." },
                { "VArc.Evaluate", "The point on the arc at the normalised parameter, 0 at StartAngle and 1 at EndAngle, interpolating the sweep angle linearly. On a circular arc equal angle steps are equal arc-length steps, so this is also the arc-length parameterisation and agrees with PointAtParameter and Divide. Parameters outside [0, 1] are NOT clamped here — they extrapolate around the full circle." },
                { "VArc.Measure", "Returns points along the arc at fixed distance intervals." },
                { "VArc.Project", "Projects a point onto the arc, returning the closest point on the arc." },
                { "VArc.PointAtParameter", "Returns a point on the arc at the given normalized parameter (0 to 1)." },
                { "VArc.ParameterAtPoint", "Returns the normalised parameter (0 to 1) of the point on the arc nearest the one you pass — the inverse of Evaluate/PointAtParameter. The offset from StartAngle is measured with GeometryHelper.SweepOffset, so it travels in the ARC'S OWN DIRECTION and a point off either end clamps to 0 or 1. Folding that offset into [0, 360) first, as this used to, is only right for a counter-clockwise arc: on an arc from 90 to 0 the midpoint at 45 degrees came back as an offset of 315 against a sweep of -90 and clamped to 1, so the middle of the arc reported itself as the end of it. A zero sweep returns 0." },
                { "VArc.Offset", "Creates a concentric arc offset by the specified distance." },
                { "VArc.SetBounds", "Trims the arc in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. StartAngle/EndAngle are rescaled to span the new range. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VArc.NormalAtPoint", "Returns the normal vector at the specified point on the arc." },
                { "VArc.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. Exact against VLine, VCircle, another VArc, VRay and VXLine, and the underlying circle roots are filtered to the arc's angular sweep, so a crossing on the missing part of the circle is correctly discarded. Against VEllipse, VPolygon, VRectangle, VPolyline, VBezier or VSpline the arc is sampled into up to 1000 chords." },
                { "VArc.ToString", "Returns a string representation of the arc." },

                // VEllipse Properties
                { "VEllipse.Center", "Gets or sets the center point of the ellipse." },
                { "VEllipse.RadiusX", "Gets or sets the horizontal radius (semi-major or semi-minor axis)." },
                { "VEllipse.RadiusY", "Gets or sets the vertical radius (semi-major or semi-minor axis)." },
                { "VEllipse.Area", "Gets the area of the FULL ellipse (π × RadiusX × RadiusY). StartAngle/EndAngle are not applied — a partial sweep encloses no area, and this still reports the whole one — and Rotation does not change it. Read-only." },
                { "VEllipse.Circumference", "The approximate perimeter of the FULL ellipse, by Ramanujan's formula — exact only when RadiusX equals RadiusY. It ignores StartAngle/EndAngle: for the length of a partial sweep call GetLength(), which measures the curve actually drawn. Rotation does not change it. Read-only." },
                { "VEllipse.SelfIntersecting", "Always returns false (ellipses cannot self-intersect)." },
                { "VEllipse.Rotation", "Orientation of the ellipse in degrees, counter-clockwise: the direction its RadiusX axis points. 0 (the default) is an axis-aligned ellipse and behaves exactly as an ellipse always has. StartAngle and EndAngle are measured in the ellipse's OWN frame, so turning a half ellipse turns the half with it rather than re-cutting it. Rotate(pivot, degrees) writes this; before it existed, Rotate moved the centre and nothing else, so rotating an ellipse about its own centre was a silent no-op." },

                // VEllipse Methods
                { "VEllipse.Draw", "Renders the ellipse to the canvas." },
                { "VEllipse.Clone", "Creates a deep copy of this ellipse — Center (a fresh VXYZ, not the same instance), both radii, StartAngle, EndAngle and Rotation, plus the styling members. The copy auto-registers on the canvas like any other new shape." },
                { "VEllipse.Move", "Translates the ellipse by the specified displacement vector." },
                { "VEllipse.Rotate", "Rotates the ellipse around the specified pivot by the given angle in degrees: the centre travels around the pivot AND the ellipse turns, by writing Rotation. It used to move the centre only, so rotating about the ellipse's own centre did nothing at all and rotating about any other point made the ellipse orbit without turning." },
                { "VEllipse.Flip", "Mirrors the ellipse across the line you pass — the infinite line through the given VLine's Start and End, at any angle. Center is reflected, and because reflecting a direction across a line at angle t maps an angle a to 2t - a, Rotation becomes 2t - Rotation. Reflection also reverses the direction of travel, so the sweep is negated in the ellipse's own frame ((StartAngle, EndAngle) becomes (-EndAngle, -StartAngle)) — without that, mirroring the upper half of an ellipse would hand back the upper half again instead of the lower one. The radii are unchanged." },
                { "VEllipse.Scale", "Scales the ellipse relative to a centre point by the specified factor: Center moves towards or away from it and both radii are multiplied by |factor|. The absolute value means a negative factor mirrors the position without giving a negative radius. Rotation, StartAngle and EndAngle are untouched — a uniform scale does not turn a shape or re-cut its sweep." },
                { "VEllipse.GetBounds", "Returns the axis-aligned bounding box of the drawn ellipse — exact for a partial sweep and for a rotated one, computed from the endpoints plus whichever axis extremes the sweep actually reaches. It used to return the box of the FULL, axis-aligned ellipse, so a half ellipse claimed the space its missing half would have taken and a rotated one claimed a box that no longer contained it." },
                { "VEllipse.Contains", "For a FULL ellipse (a 360-degree sweep) this is an exact interior test — the implicit equation (dx/RadiusX)² + (dy/RadiusY)² <= 1, with dx/dy measured from Center. For a PARTIAL sweep there is no enclosed area, so it means 'lies on the curve' instead, judged with a tolerance scaled to the larger radius. Either way it is not a bounding-box test: a point in a corner of the box is outside. It honours Rotation: the point is taken into the ellipse's own frame first, because the implicit equation divides by the radii and so only means anything along the ellipse's own axes. A point exactly ON the boundary is decided by floating point and may go either way, which is inherent to an interior test and not specific to a rotated ellipse." },
                { "VEllipse.Evaluate", "Returns the point at a parameter in [0, 1] measured by ARC LENGTH, so 0.5 is the halfway point along the curve and Divide(n) gives evenly spaced points. This is what PointAtParameter calls. It used to interpolate the sweep angle linearly, which on an eccentric ellipse bunched divisions up near the flat ends; every other ICurve is length-parameterised, and callers like Measure and the animation samplers assume it. Use EvaluateByAngle when you want the angle-linear reading instead." },
                { "VEllipse.EvaluateByAngle", "Returns the point at a parameter in [0, 1] interpolated linearly through the sweep ANGLE, from StartAngle to EndAngle. This is the right choice when you want equal angles rather than equal distances — radial spokes, sector boundaries, a hand sweeping round a dial. For anything spaced along the curve use Evaluate. On a circle the two agree, because angle and arc length are proportional there; they diverge as the ellipse becomes more eccentric." },
                { "VEllipse.DistanceTo", "Returns the shortest distance from the point to the ellipse's CURVE, computed by sampling it (through Divide, so it honours Rotation as well). It honours the sweep: on a partial ellipse a point past either end measures to the nearer endpoint, not to the full ellipse. Zero on the curve, positive both inside and outside — pair it with Contains for the side." },
                { "VEllipse.GetLength", "Returns the length of the swept curve, computed numerically. Shares its implementation with the ICurve.GetLength explicit implementation — the two used to differ, so ellipse.GetLength() and ((ICurve)ellipse).GetLength() gave different answers for the same ellipse depending only on the static type at the call site." },
                { "VEllipse.PointAtAngle", "Returns the world point at the given angle in the ellipse's own frame (degrees), with Rotation applied. This is the single place the parametric form and the orientation are combined, so nothing can honour one and forget the other; Evaluate and EvaluateByAngle both route through it." },
                { "VEllipse.PointAtParameter", "Returns a point on the ellipse at the given normalized parameter (0 to 1)." },
                { "VEllipse.ParameterAtPoint", "Returns a parameter in [0, 1] for the point on the ellipse nearest the one you pass. One caveat worth knowing: it is NOT the inverse of Evaluate. Evaluate is arc-length parameterised while this measures the fraction of the SWEEP ANGLE covered, so on an eccentric ellipse ParameterAtPoint(Evaluate(0.5)) is not 0.5 — pair it with EvaluateByAngle, which is its true inverse. It measures with GeometryHelper.SweepOffset in the ellipse's own frame, so it honours Rotation and travels in the sweep's own direction: on an ellipse from 90 to 0, the point halfway along comes back as 0.5. A zero sweep returns 0, and the result is clamped to [0, 1]." },
                { "VEllipse.SetBounds", "Trims the ellipse in place: the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. The trim is by ARC LENGTH, matching Evaluate — SetBounds(0.25, 0.75) keeps the middle half of the CURVE, not of the sweep angle — and StartAngle/EndAngle are set to the angles at those arc fractions. Parameters are clamped to [0,1] and swapped if reversed." },
                { "VEllipse.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. Exact only against VLine, VRay and VXLine; every other partner (VCircle, VArc, VPolygon, VRectangle, VPolyline, VBezier, VSpline, another VEllipse) is answered by sampling both curves into up to 1000 segments each. The exact line/ellipse routine honours Rotation (it solves in the ellipse's own frame) but treats the ellipse as COMPLETE — a partial sweep's StartAngle/EndAngle is not applied there, so filter the points against the drawn arc yourself if that matters." },
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
                { "VPolygon.Intersect", "Computes the intersection between this polygon's OUTLINE and another curve, returning an IntersectionResult — crossing points, not overlapping area. The polygon contributes its real edges, so the answer is exact whenever the partner is also straight-edged (VLine, VPolyline, another VPolygon or VRectangle, or a VRay/VXLine, which are converted to their finite RenderExtent span first); a curved partner (VCircle, VArc, VEllipse, VBezier, VSpline) is sampled into up to 1000 chords. For the overlapping AREA use BooleanOps.Intersect(a, b) — polygon.Intersect(other) can never give you that, because this instance method always beats the boolean extension method of the same name." },
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
                { "VPolyline.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. The polyline contributes its real segments, so the answer is exact against any straight-edged partner (VLine, another VPolyline, VPolygon, VRectangle, VRay, VXLine); a curved partner (VCircle, VArc, VEllipse, VBezier, VSpline) is sampled into up to 1000 chords. Use SelfIntersecting for the polyline crossing ITSELF." },
                { "VPolyline.ToString", "Returns a string representation of the polyline." },

                // VText Properties
                { "VText.Location", "Gets or sets the anchor position of the text (VXYZ). Which corner or edge of the text box lands here is decided by Anchor; rotation by Angle happens about this point." },
                { "VText.Content", "Gets or sets the string to display. LiftChar and the indexer rewrite this string, replacing the lifted character with a space." },
                { "VText.Height", "Gets or sets the font height in world units. Default 12." },
                { "VText.Width", "Gets or sets the width of the text box in world units. Default 0, which means \"estimate it from the string\": 0.6 x Height per character of the LONGEST line, since C2VGeometry has no font metrics of its own. Set a value to override the width GetBounds and anchoring use — it describes the whole block, not one line, and it does not wrap the text (lines come only from newline characters in Content). It never changes the glyphs' size; Height does that." },
                { "VText.Font", "Gets or sets the font family (VFont enum). Default VFont.Arial." },
                { "VText.FontWeight", "Gets or sets the weight (VFontWeight.Normal or Bold). Default Normal." },
                { "VText.GlyphOutlineProvider", "Static. The host-supplied IGlyphOutlineProvider that turns characters into vector contours. The desktop app sets it at startup; when it is null, ToCharShape/LiftChar/LiftChars all return null." },
                { "VText.BlankChar", "Replaces the character at the given index with a space without returning a shape. Out-of-range indices are ignored." },
                { "VText.GetAnchorOffset", "Given a measured text width and height, returns the (offsetX, offsetY) that must be added to Location to reach the box's bottom-left corner for the current Anchor." },
                { "VText.DoesIntersect", "Text-aware overlap test: the text's rotated, anchor-aware bounding quad is tested against the other shape's bounding box using the Separating Axis Theorem. Shape.DoesIntersect delegates back here, so other.DoesIntersect(text) gives the same answer." },
                { "VText.Anchor", "Gets or sets the text anchor point (VTextAnchor enum). Controls which point of the text bounding box is placed at the text's position. Default is BottomLeft." },
                { "VText.Justify", "Gets or sets how the lines of a MULTI-LINE label line up with each other inside the text block (VTextJustify: Left, Center, Right). Default Left, and the lines come from newline characters (\\n) in Content. Composes with Anchor rather than competing with it: Anchor puts the block on the drawing, Justify decides the shape of the ragged edge inside it, so Anchor = MiddleCenter with Justify = Center is a block centred on its point whose short lines are also centred against its long ones. Single-line text is unaffected. It is pure layout inside the block: it never moves or resizes it, GetBounds() is identical for all three values, and Clone() carries it over. EXPORT: SVG and PDF lay a multi-line label out as lines and honour Justify — SVG through per-line text-anchor, PDF by shifting each line inside the block's measured width — as well as Anchor, so the exported label matches the canvas. DXF keeps the lines too (one TEXT entity each, stacked 1.2 x Height apart along the label's own down direction) but does NOT apply Justify or Anchor: every line starts at the same point, because R12 TEXT has no block width to justify inside." },
                { "VText.Angle", "Gets or sets the rotation of the text block in degrees, counterclockwise around Location. Characters rotate with the block (Excel-style). 0 = horizontal (default), 90 = reads bottom-to-top." },
                { "VText.Mask", "Gets or sets whether a solid rectangle is painted behind the text so it stays legible over whatever it crosses — the background mask a CAD package draws behind a dimension label. DEFAULT TRUE, painted in the canvas background colour, so a label looks no different over empty canvas and cleanly interrupts anything it crosses. The one cost is over a FILLED shape, where a masked label punches a canvas-coloured hole - set Mask = false there to let the drawing show through. The mask is part of the text and not a separate shape: it is drawn immediately before the glyphs, so it can never hide them, it never appears in the shape list, and it does NOT change GetBounds(), so zoom-extents and culling are unaffected. Colour comes from MaskColor and padding from MaskOffset. To decide what the masked label sits above, set ZIndex. Honoured by the canvas (all three render backends, since text always goes through the vector layer) and by the SVG and PDF exporters; DXF has no equivalent of a background fill in the R12 format this app writes, so a mask is simply absent there." },
                { "VText.MaskColor", "Gets or sets the colour of the Mask rectangle — a colour name or hex string exactly like Color, so VColor.Black and \"#202020\" both work. NULL is the default and means \"the canvas background\": it is resolved when the text is drawn, not captured when it is constructed, so a label keeps blending in after the background is changed with nothing to re-run. Away from a canvas (the SVG and PDF exporters) it resolves against the static CanvasBackgroundColor. Ignored when Mask is false." },
                { "VText.CanvasBackgroundColor", "Static. The canvas background colour as the host last published it (\"#RRGGBB\"), and the fallback a null MaskColor resolves against on any surface with no canvas of its own — the SVG and PDF exporters. Mirrors Shape.DefaultRegistry and VText.GlyphOutlineProvider: C2VGeometry has no UI and cannot know this by itself; DoodleSharp keeps it in step with RenderCanvas.CanvasBackground. The canvas renderer does NOT read it back — it resolves a null mask against its own live brush, which cannot go stale." },
                { "VText.MaskOffset", "Gets or sets how far the Mask extends beyond the text, as a FRACTION of the text height: 0 hugs the glyphs, 0.5 pads by half a text height on every side, 1 by a full one. Default 0.15, and values are clamped to [0, 1] on assignment. It is a fraction rather than a number of drawing units so a 2-unit label and a 200-unit one keep the same visual breathing room." },

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

                // VTextJustify enum values
                { "VTextJustify.Left", "Lines share a left edge and the ragged edge is on the right. Every line starts at the same x. The default, and what text does with no justification applied at all." },
                { "VTextJustify.Center", "Lines are centred on the block's vertical midline — half-way between the left edge of the widest line and its right edge — so both edges are ragged. The usual choice for a centred label, and the one people reach for Anchor = MiddleCenter expecting: pair the two." },
                { "VTextJustify.Right", "Lines share a right edge and the ragged edge is on the left. Every line ends at the same x, which is what lines a column of values or a right-hand table of dimensions up." },

                // VText Methods
                { "VText.Draw", "Renders the text to the canvas." },
                { "VText.Clone", "Creates a deep copy of this text with all properties duplicated." },
                { "VText.Move", "Translates the text by the specified displacement vector." },
                { "VText.Rotate", "Rotates the text around the specified pivot by the given angle in degrees. Both Location (moved around pivot) and Angle (text's own orientation) are updated, so the characters tilt by the same amount." },
                { "VText.Flip", "Reflects the text's Location across the line you pass — and nothing else. Angle is left alone and the glyphs are never mirrored, so a label moves to the other side of the line still reading left to right, which is almost always what you want of a label. If you want the block turned as well, set Angle yourself: for a mirror line at angle t, 2t - Angle is its mirror image." },
                { "VText.Scale", "Scales the text relative to a center point by the specified factor." },
                { "VText.GetBounds", "Returns the axis-aligned bounding box of the whole label, honouring Anchor (which corner of the box is at Location) and Angle (the box is rotated and re-fitted). It is MULTI-LINE AWARE: the width is the width of the WIDEST line and the height covers every line, with the gaps between them counted at 1.2 x Height, because a font's line box is taller than its em size. It is still an ESTIMATE rather than a measurement — C2VGeometry cannot measure a font, so with Width left at 0 a character is taken as 0.6 x Height wide — but the estimate is now the shape of the label instead of one long line: a three-line label used to report a box 66 x 10 when it was 18 x 34, because the width summed every line end to end (counting the newline characters themselves) while the height stayed at a single Height. Set Width to override the width. Neither Justify nor the Mask changes it. For VText this box IS the shape — there is no glyph outline to test against — so Contains, DistanceTo, click-to-select, rubber-band selection, culling and zoom-to-fit all read it." },
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
                { "VBezier.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. There is no closed form for a cubic Bezier against anything, so this always goes through the sampled path: the Bezier is decomposed into length x 10 segments (at least 2, capped at 1000), the partner likewise unless it is already straight-edged, every segment pair is tested and duplicate points are merged. Accurate to that sampling, not analytic." },
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
                { "VSpline.Intersect", "Computes the intersection with another curve, returning an IntersectionResult. Like VBezier there is no closed form, so the spline is decomposed into length x 10 segments (at least 2, capped at 1000) and tested pair-by-pair against the partner's segments, with duplicate points merged. Accurate to that sampling, not analytic." },
                { "VSpline.ToString", "Returns a string representation of the spline." },

                // VArrow Properties
                { "VArrow.Start", "Gets or sets the starting point of the arrow." },
                { "VArrow.End", "Gets or sets the ending point (tip) of the arrow." },
                { "VArrow.HeadLength", "Length of each arrowhead wing in world units, measured from the tip. Default 15. It does not scale with the shaft, so a short arrow with the default head looks head-heavy — reduce it for small arrows. With HeadAngle it determines the head's proportions: width 2 × HeadLength × sin(HeadAngle), depth HeadLength × cos(HeadAngle)." },
                { "VArrow.HeadAngle", "Half-angle in degrees between each arrowhead wing and the shaft, so the head spans twice this at the tip. Default 30, giving a 60-degree head; smaller values give a narrow dart, larger values a broad flat head. Combined with HeadLength (the length of each wing) it fully determines the head: the head is 2 × HeadLength × sin(HeadAngle) wide and HeadLength × cos(HeadAngle) deep. The head is a closed triangle, and every renderer and exporter reads its GEOMETRY from the same place (VArrow.GetArrowheadPoints), so the head is the same shape and size on the vector, raster and GPU backends and in SVG, PDF and DXF output. ITS FILL IS NOT YET CONSISTENT, though: the vector renderer fills the triangle with the stroke colour, while the raster and GPU backends stroke its outline — so a head that is solid under Legacy comes out hollow under Managed or GPU. DXF, being a wireframe format, writes the triangle as three LINE entities. Known open item; the geometry is right in all of them. ONE THING TO EXPECT IN AN EXISTING DRAWING: heads are now noticeably broader than they used to appear on the canvas, because that path was previously pinned to an effective 9.5-degree half-angle and ignored this property entirely. That is the correction, not a regression — reduce HeadAngle if you preferred the narrower dart." },
                { "VArrow.DoubleEnded", "When true, an identical head is drawn at Start as well as at End, so the arrow reads as bidirectional. Default false. Honoured by every renderer and by SVG, PDF and DXF export alike — the exporters used to drop the start head silently. Both heads are identical in size and angle; see HeadAngle for the one respect in which the head's APPEARANCE still differs between backends (filled versus outlined)." },
                { "VArrow.MidPoint", "The midpoint of the shaft, read-only. This is control point 0, the whole-shape move handle." },
                { "VArrow.GetStartArrowhead", "The two wing tip coordinates of the head at Start, as a (VXYZ, VXYZ) tuple. Returned whether or not DoubleEnded is set, so check the flag first if you are reproducing what is drawn." },
                { "VArrow.GetEndArrowhead", "The two wing tip coordinates of the head at End, as a (VXYZ, VXYZ) tuple — exactly the geometry every renderer and exporter draws, honouring both HeadLength and HeadAngle." },
                { "VArrow.GetArrowheadPoints", "GetArrowheadPoints(VXYZ tip, VXYZ from) — the two wing tips of a head pointing at tip and opening back towards from, using this arrow's HeadLength and HeadAngle. This is THE definition of an arrowhead's geometry in the library; every renderer and exporter calls it, which is what keeps an arrow the same shape and size on the vector, raster and GPU backends and in every export format (how the resulting triangle is PAINTED still varies — see HeadAngle). Useful directly when you want to draw your own head somewhere along a path rather than at the arrow's own ends. Returns (tip, tip) when tip and from coincide." },
                { "VArrow.ArrowheadWings", "Static. ArrowheadWings(VXYZ tip, VXYZ from, double headLength, double headAngleDegrees) — the same wing-tip calculation for a caller that supplies its own size and angle instead of taking them from a VArrow. Each wing is headLength long and headAngleDegrees off the shaft. This is what the dimension shapes use, at their own ArrowSize and VDimension.DimensionArrowAngleDegrees; use it to draw a consistent arrowhead on anything — a leader, a flow line, a hand-built annotation. Returns (tip, tip) for a degenerate or non-finite direction, so check for that before drawing." },

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
                { "VDimension.Offset", "How far the dimension line sits from the measured points, in world units. Default 20 (or ShapeDefaults.DimOffset). Positive and negative offsets put the dimension line on opposite sides. Together with OffsetFromOrigin and ExtendBeyondDimLines it also fixes the extension lines: each one spans from OffsetFromOrigin away from its measured point out to Offset + ExtendBeyondDimLines. (ExtensionLength does NOT participate — it is deprecated and inert.)" },
                { "VDimension.ExtensionLength", "DEPRECATED, and marked [Obsolete] — SETTING IT DOES NOTHING, and never did. Nothing reads it: not the renderer, not GetDimensionGeometry, not any exporter. An extension line's length is already fully determined by three other properties: it runs from OffsetFromOrigin away from the measured point out to Offset + ExtendBeyondDimLines past it, leaving nothing for this one to control. Set those instead. It is kept rather than deleted so existing code still compiles; the compiler warning is the signal that the assignment has no effect." },
                { "VDimension.ArrowSize", "Length of each arrowhead wing at both ends of the dimension line, in world units. Default 8 (or ShapeDefaults.DimArrowSize). The head's ANGLE is not adjustable per dimension: it is fixed at VDimension.DimensionArrowAngleDegrees (20°) so every dimension in a drawing matches." },
                { "VDimension.DimensionArrowAngleDegrees", "Constant, 20. The half-angle in degrees of a dimension arrowhead off its dimension line — shared by the canvas renderer, the tessellator (and so the raster and GPU backends) and every exporter, so a dimension's heads are the same SHAPE AND SIZE wherever it is drawn. (Their fill still differs by backend, the same open item noted on VArrow.HeadAngle: filled under the vector renderer, outlined under raster and GPU.) Read it when drawing your own annotation and you want it to match: VArrow.ArrowheadWings(tip, from, dim.ArrowSize, VDimension.DimensionArrowAngleDegrees). It is a compile-time constant, so there is nothing to set — dimension heads are not individually configurable; vary ArrowSize instead. Distinct from VArrow.HeadAngle, which IS per-arrow." },
                { "VDimension.TextHeight", "Gets or sets the height of the dimension text." },
                { "VDimension.DecimalPlaces", "Gets or sets the number of decimal places for distance display." },
                { "VDimension.ExtendBeyondDimLines", "How far each extension line runs PAST the dimension line, in world units. Default 1.25 (or ShapeDefaults.DimExtendBeyondDimLines) — the small overshoot that makes a dimension read as drafted rather than as a bare bracket. Zero stops the extension lines flush with the dimension line." },
                { "VDimension.OffsetFromOrigin", "The gap left between the measured point and where its extension line STARTS, in world units. Default 0.625 (or ShapeDefaults.DimOffsetFromOrigin). The drafting convention: a small gap so the extension line does not touch the geometry it is measuring. Zero makes it start exactly at the point. With Offset and ExtendBeyondDimLines this fully determines the extension line's length — ExtensionLength is deprecated and does nothing." },
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
                { "VRadialDimension.ArrowSize", "Length of the leader arrowhead's wings, in world units. Default 8 (or ShapeDefaults.DimArrowSize). As with VDimension the head angle is fixed at VDimension.DimensionArrowAngleDegrees (20°), so radial and linear dimensions match; only the size varies. With ShowDiameter set, both ends get a head." },
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
                { "Shape.LineWeight", "Gets or sets the thickness of the outline stroke. Default 2.0. It is DEVICE PIXELS by default, so a stroke keeps the same on-screen width at any zoom; tick Settings > Application Settings > Line Style Rendering > Display Line Weight and it is read as WORLD UNITS instead, so strokes thicken as you zoom in the way a CAD package shows true widths. It is not a plot width — AutoCAD's lineweight is an ink width in millimetres — and the exporters treat it as a screen quantity: SVG pins it to device pixels with vector-effect, PDF converts DIPs to points at 96 DPI, and DXF does not carry it at all. Two practical notes: while Display Line Weight is on, the Auto render backend stays on the WPF vector renderer, because neither the software rasterizer nor the GPU path reads line weight (both draw hairlines); and the value is clamped when it is scaled by zoom, so an enormous LineWeight cannot swallow the canvas." },
                { "Shape.LineType", "Gets or sets the stroke style (line pattern). Options: Continuous (solid), Dashed, Dotted, DashDot, DashDotDot, Center, Phantom, Hidden. The actual dash and gap lengths come from C2VGeometry.Rendering.LineTypePatterns, the single definition every backend and exporter shares." },
                { "Shape.LineTypeScale", "Gets or sets the multiplier on the dash and gap lengths of LineType. Default 1.0; above 1 lengthens dashes and gaps, below 1 shortens them, and a zero, negative or non-finite value renders solid rather than invisible. The pattern itself is defined in DEVICE PIXELS (LineTypePatterns), and this scales it — so unlike LineWeight it does NOT follow the Display Line Weight setting, and unlike AutoCAD's LTSCALE it is not measured in drawing units: zooming never changes how long a dash looks. It is also independent of LineWeight, so a hairline and a heavy line of the same type dash identically." },
                { "Shape.DrawFactor", "Gets or sets the draw factor (0.0 to 1.0) for progressive drawing animations." },
                { "Shape.OffsetX", "Gets or sets the X offset for translation animations." },
                { "Shape.OffsetY", "Gets or sets the Y offset for translation animations." },
                { "Shape.RotationAngle", "Gets or sets the rotation angle in degrees, counter-clockwise, about RotationPivot; written by RotateAnimation. The renderer applies it uniformly to every shape type, so any shape rotates. Declared virtual so a shape that rotates by rebuilding its own geometry can hook the setter — VRectangle overrides it and rebuilds its four corners, which is why it is the one shape excluded from the render transform (applying both would turn it twice). Rotation is otherwise a RENDER-TIME transform: Contains, DistanceTo and click-to-select operate on the unrotated geometry, so point queries against a rotated shape answer for its pre-rotation position. VRectangle, having baked the rotation into its corners, is again the exception." },
                { "Shape.RotationPivot", "Gets or sets the pivot point for rotation animations. Null uses shape center." },
                { "Shape.IsVisible", "Gets or sets whether this shape is visible on the canvas. Hidden shapes are not rendered but remain in the shape collection." },

                // Shape base class methods
                { "Shape.Place", "Puts the shape on the canvas and keeps it there: registers it with Shape.DefaultRegistry and sets IsExplicitlyDrawn = true, which exempts it from the pass that hides unnamed shapes after Main() returns. Idempotent — calling it twice, or on a shape that is already placed, is harmless — and Remove() is the inverse. A shape you construct yourself needs no Place() call, because construction already registered it. Reach for it when the shape did not come from a plain `new`: results of boolean ops, ArrayOps and Chart (registered but unnamed, so otherwise swept away — setting Name does the same job); the query results that deliberately do not draw their answer (GeometryHelper.IntersectLineLine and friends, VRay.ToFiniteLine, VRay.ToXLine, VXLine.ToFiniteLine); and anything built while Shape.AutoRegister was false. THERE IS A SECOND OVERLOAD, Place(Viewport): it does everything the no-argument one does AND assigns the shape to one cell of the viewport grid — new VCircle(new VXYZ(0, 0), 10).Place(Viewports[1][2]). Since construction already registered the shape on the root, that overload is normally a MOVE, and it works just as well several lines after the shape was built as it would have up front. It throws ArgumentNullException on a null viewport. Placing on a viewport that has since been SUBDIVIDED is not an error: the shape draws in that viewport's first cell, on the reading that the cell stayed where it was and merely got split. On the default 1x1 layout Viewports[0][0] IS the root, so Place() and Place(Viewports[0][0]) are the same call." },
                { "Shape.Draw", "The historical name for Place(), and exactly equivalent to it — a one-line forward, pinned by a test so the two cannot drift apart. It appears throughout older projects and samples, and the canvas drawing tools still emit it in the code they generate, so it is in no way discouraged; there is nothing to migrate. New code reads better with Place(), which says what actually happens: shapes render because they are registered, not because something was 'drawn'." },
                { "Shape.CopyStyleTo", "Copies this shape's styling onto another shape and returns that target, so the call chains. Copies six members — Color, FillColor, LineWeight, LineType, LineTypeScale, ZIndex — and touches nothing else: geometry, Name, Id, IsVisible and placement are all left alone. It is a no-op (returning the argument unchanged) when the target is null or is this same shape, which is what makes it comfortable to use on a boolean-op result that may legitimately be null. The motivating case is restyling a computed shape to match the input it came from: a.CopyStyleTo(a.Union(b))." },
                { "Shape.Remove", "Unregisters the shape from the canvas — the inverse of Place(). Unlike Hide(), the shape is gone from the collection, not merely unrendered." },
                { "Shape.Name", "Optional label for the shape, default an empty string. Also load-bearing for visibility: after Main() returns, shapes whose Name is empty and which were never explicitly drawn are hidden as construction leftovers." },
                { "Shape.Opacity", "Transparency multiplier from 0 (invisible) to 1 (opaque). Default 1.0. Applied on top of any alpha already present in Color or FillColor." },
                { "Shape.AutoRegister", "Static switch. When false, newly constructed shapes are not added to the canvas. Use it to build throwaway geometry cheaply, and always restore it in a finally block." },
                { "Shape.DefaultRegistry", "Static. The IShapeRegistry that receives every shape on construction — the mechanism behind auto-registration. The host application sets it; user code rarely touches it." },
                { "Shape.ResetDefaults", "Static. Restores DefaultColor, DefaultFillColor, DefaultLineWeight, DefaultLineType and DefaultLineTypeScale to their built-in values (Cyan, Transparent, 2.0, Continuous, 1.0)." },
                { "Shape.GetControlPoints", "Returns the interactive editing handles for this shape. The base implementation returns a single Move handle at the bounding-box centre; most shapes override it with vertex, radius or control handles." },
                { "Shape.MoveControlPoint", "Moves the handle at the given index to a new position. The base implementation treats index 0 as \"move the whole shape\"." },
                { "Shape.DoesIntersect", "Returns true when this shape overlaps another. When both shapes are curves (VLine, VCircle, VArc, VEllipse, VPolyline, VPolygon, VRectangle, VBezier, VSpline, VRay, VXLine) the answer comes from CurveIntersection, the same engine behind ICurve.Intersect, so the two always agree; otherwise it reports whether Intersect() produced a result, and defers to VText's specialised test when the other shape is text. Cheap enough for the loop it is usually written in — it does not build the intersection shapes just to discard them. Use it as the guard before calling Intersect() for the actual points: `if (ray.DoesIntersect(circle)) { foreach (var p in ray.Intersect(circle).Points) ... }`. It is symmetric — a.DoesIntersect(b) and b.DoesIntersect(a) agree — and a shape with no curve to test (Region, VHatch, VGrid, the dimensions) still answers false. Reach limit: a VRay or VXLine operand only extends as far as its RenderExtent (10000 by default)." },
                { "Shape.Clone", "Creates a deep copy of the shape with all properties duplicated. Returns the same type as the original (covariant return type), so no casting is needed." },
                { "Shape.Move", "Translates the shape by the specified displacement vector." },
                { "Shape.Rotate", "Rotates the shape around the specified pivot point by the given angle in degrees." },
                { "Shape.Flip", "Mirrors the shape across the specified line (axis of reflection)." },
                { "Shape.Scale", "Scales the shape relative to a center point by the specified factor." },
                { "Shape.GetBounds", "Returns the axis-aligned BoundingBox of the shape (Min, Max, Width, Height, Center, Area). It also deconstructs to a (min, max) tuple. VRay and VXLine have no far end, so their bounds are taken from RenderExtent (default 10000) rather than from the geometry: the box is finite, but it describes the drawn stretch, not the line — which is why RayCaster excludes those two by type rather than relying on a non-finite-bounds check. GetLength() on either really does return double.PositiveInfinity." },
                { "Shape.Contains", "Returns true if the specified point is inside or on the shape. The base implementation is a bounding-box test, but every shape with a real outline overrides it: the open curves (VLine, VPolyline, VArc, VBezier, VSpline, VXLine, VRay) answer 'lies on the stroke' — VRay is false behind its Origin — and the area types (VCircle, VEllipse, VRectangle, VPolygon, VGroup, VHatch, Region) do a genuine interior test. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension keep the bounding-box answer, because for those the box is the shape or there is no outline to test." },
                { "Shape.DistanceTo", "Returns the distance from the shape to the specified point. The base implementation measures from the bounding-box centre, but every shape with a real outline overrides it with the true shortest distance: exact for VLine, VArc, VPolyline, VPolygon (so also VRectangle), VCircle, VXLine and VRay; sampled for VEllipse, VBezier and VSpline; to the boundary for VHatch and Region; the nearest child for VGroup. For an area type this is the distance to the OUTLINE — zero on it and positive on both sides, not a signed depth — so pair it with Contains for the side. Only VPoint, VText, VGrid, VSpatialGrid, VArrow, VDimension and VRadialDimension use the base behaviour." },
                { "Shape.Intersect", "Computes the geometric intersection with another shape, or null when they do not meet. When both shapes are curves this defers to CurveIntersection and materialises the answer: one crossing comes back as a VPoint, several as a VGroup of VPoints, and a collinear overlap as the overlapping curve. Nothing it builds is registered — a query does not draw its own answer — so call Place() on the result if you want to see it. For curves, ICurve.Intersect(ICurve) is the better API: it returns an IntersectionResult carrying every point and every overlapping curve, where this can only hand back one shape. VLine, VRectangle, VPoint and VGroup override this with their own closed-form answers. Note that on a CONCRETE curve variable you will not reach this overload with a curve argument — line.Intersect(circle) binds to Intersect(ICurve) instead, because an override counts as declared on Shape and loses to a method declared further down. You get this one by holding the argument in a Shape-typed variable, or by passing something that is not an ICurve (VText, VGroup, Region, VHatch). Shapes with no curve to test — Region, VHatch, VGrid, VSpatialGrid, VDimension, VRadialDimension, VArrow, VText — still return null." },
                { "Shape.ToString", "Returns a string representation of the shape." },
                { "Shape.Show", "Shows this shape on the canvas by setting IsVisible to true." },
                { "Shape.Hide", "Hides this shape from the canvas by setting IsVisible to false. The shape remains in the collection but is not rendered." },
                { "Shape.ZIndex", "Draw order for the whole drawing: higher renders on top, the default is 0, and negatives are the natural way to push a backdrop behind everything. Shapes sharing a value keep the order they were created in. This is a GLOBAL key, not a relationship between two shapes, which is what the BringAbove/SendBehind pair it replaced could not express - those reordered the shape list once and were undone by the very next shape to be constructed, so \"this label is always on top\" was not sayable. Assigning it tells the registry the draw order is stale, so it takes effect on the next repaint with no further call - including from a Mouse or Frame callback. Hit-testing follows the same order, so the shape you click is the one you see on top. Inside a VGroup the children draw in the order the group holds them; the group's own ZIndex places the group as a whole. Clone() and CopyStyleTo() carry it across. One caveat: on the Managed and GPU render backends text is composited in a layer above the rasterised geometry, so a VText is always on top of geometry there whatever its ZIndex - text against text, and geometry against geometry, order correctly on every backend." },

                // Shape state flags and static style defaults
                { "Shape.IsPlaced", "True once the shape has been accepted by the registry. Set by Place() (and by construction, since shapes auto-register) and cleared by Remove(). It is what makes Place() idempotent: registering an already-placed shape is a no-op rather than a duplicate entry. Reading it tells you whether the shape is currently on the canvas; do not set it by hand, because writing true without registering leaves the canvas out of step with the flag." },
                { "Shape.IsExplicitlyDrawn", "True when Place() (or its alias Draw()) has been called on this shape. It is the flag the post-Main() sweep consults: a shape with an empty Name and IsExplicitlyDrawn false is treated as construction leftover and hidden. Setting Name achieves the same exemption, so a shape needs one or the other to survive. Default false." },
                { "Shape.IsSelected", "True while the shape is part of the canvas selection. Written by the selection tool and by Ctrl+A; the renderer draws selection handles for shapes where it is true. Setting it from code marks the shape as selected but does not scroll to it or update the Properties panel. Default false." },
                { "Shape.FlipProgress", "How far through a mirror the shape is drawn, 0 (unflipped) to 1 (fully mirrored across FlipAxis). Written by FlipAnimation each frame. Values outside [0, 1] are not clamped. Default 0, which is why FlipAxis alone changes nothing." },
                { "Shape.FlipAxis", "The VLine that FlipProgress mirrors across, or null for no flip. Only its geometry is read — the line is not drawn as part of the flip, and can be hidden or left off the canvas entirely. Set together with FlipProgress; FlipAnimation writes both. Default null." },
                { "Shape.DefaultColor", "Static. The stroke colour every new shape starts with unless its own type overrides it (VArc is Orange, VCircle Yellow, VPolygon LightBlue, VRectangle Magenta, VPoint White) or ShapeDefaults.GlobalColor is set. Default \"Cyan\". Changing it affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.DefaultFillColor", "Static. The fill colour every new shape starts with. Default \"Transparent\", which is why shapes are outlines until you set FillColor. Affects only shapes constructed afterwards; ResetDefaults() restores it." },
                { "Shape.DefaultLineWeight", "Static. The stroke thickness every new shape starts with. Default 2.0. It means device pixels unless Display Line Weight is ticked in Settings, in which case it is world units; ResetDefaults() restores it." },
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
                { "VXYZ.AngleToDegrees", "Returns the unsigned angle in DEGREES between this vector and another, 0 to 180 - the library's convention, so the answer can go straight into Rotate, VText.Angle, VArc's angles and the rest. Returns 0 when either vector has zero length. Unsigned: a direction 45 degrees above the X axis and one 45 degrees below both answer 45, so to ORIENT something along a 2D direction use Math.Atan2(dir.Y, dir.X).ToDegrees(), which keeps the sign." },
                { "VXYZ.AngleToRadians", "Returns the unsigned angle in RADIANS between this vector and another, 0 to π - for handing to System.Math, which works in radians. Returns 0 when either vector has zero length. Everything else in this library takes degrees; AngleToDegrees is the spelling that matches it." },
                { "VXYZ.AngleTo", "OBSOLETE - use AngleToDegrees for the library's convention, or AngleToRadians if you want radians. Behaviour is unchanged: it returns the unsigned angle in RADIANS, 0 to π. The name was retired because it says nothing about its unit while every rotation API here takes degrees, and the mismatch does not look like an error: text.Angle = dir.AngleTo(VXYZ.BasisX) on a direction pointing along -X assigns π as 3.14 DEGREES, so the label is drawn a hair crooked instead of turned right round - reported as a text mask that was 'slightly off axis'." },
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
                { "ICurve.Offset", "Creates a new curve parallel to this one at the specified distance; the sign chooses the side. THE RESULT IS A REAL SHAPE AND AUTO-REGISTERS, so it appears on the canvas immediately — call Remove() on it if you only wanted the geometry. There is also an Offset(List<double> distances) overload returning one curve per distance, for a family of parallels in a single call. Accuracy varies by shape and is documented per type: exact for VLine, VRay, VXLine, VCircle and VArc, and an approximation for VEllipse, VPolygon, VPolyline, VBezier and VSpline, whose true offset curves are not the same kind of curve. For a robust polygon offset with proper mitring, use BooleanOps.OffsetPolygon instead." },
                { "ICurve.SplitAtPoint", "Splits the curve at the specified point, returning the two pieces as a tuple of ICurve. The point does not have to lie on the curve — it is projected onto the nearest position first. Two things to expect. THE ORIGINAL IS NOT CONSUMED: it stays on the canvas exactly as it was, so you normally want original.Remove() after splitting, or you will be looking at the whole curve with the two halves drawn on top of it. And THE TWO PIECES ARE REAL SHAPES, so they auto-register and appear immediately; call Remove() on either if you only wanted the geometry. The piece TYPE is not always the type you split: splitting a VCircle gives two VArcs, and splitting a VPolygon or VRay gives open curves, because a trimmed closed or infinite curve is a different kind of thing. Use SetBounds when you want to trim a curve in place instead." },
                { "ICurve.SetBounds", "Trims the curve in place so that the parameter sub-range [startParameter, endParameter] becomes the new [0, 1]. Parameters are clamped to [0,1] and swapped if reversed. Implemented for VLine/VArc/VEllipse/VPolyline/VBezier/VSpline. Throws NotSupportedException on VCircle/VPolygon/VRay/VXLine because their trimmed result is a different shape type — use SplitAtPoint there." },
                { "ICurve.NormalAtPoint", "Returns the normal vector (perpendicular) to the curve at the specified point." },
                { "ICurve.PointsAtChordLengthFromPoint", "Returns the points on this curve that are exactly chordLength away from the given point in a straight line — the intersections of a circle of that radius with the curve. The reference point does not have to lie on the curve; it is projected onto it first. The list is empty when the circle never reaches the curve, and typically holds one point on each side when it does. Use it to step along a curve by true chord distance (setting out a fence line, spacing bolts on an arc); use Measure(segmentLength) instead when you want arc-length spacing." },
                { "ICurve.Place", "Puts the curve on the canvas and keeps it there. Declared on IDrawable, which ICurve extends, so the recommended name is reachable through an ICurve reference and not only through Shape. Exactly equivalent to Draw()." },
                { "ICurve.Draw", "The historical name for Place(), and exactly equivalent to it. Declared on IDrawable, which ICurve extends." },
                { "ICurve.Intersect", "Computes the intersection with another curve, returning an IntersectionResult carrying Points (every crossing) and Curves (any shared, overlapping stretch). This is the RICHER of the two intersection APIs and the one to reach for: Shape.Intersect(Shape) can only hand back a single shape. On a concrete curve variable this overload is what you get for any ICurve argument, because Shape.Intersect(Shape) is an override and therefore counts as declared on Shape. It forwards to CurveIntersection.Intersect, which is exact for line/line, line/circle, line/arc, line/ellipse, circle/circle, circle/arc and arc/arc, and exact for anything made of straight edges (VPolyline, VPolygon, VRectangle, and VRay/VXLine, which are first converted to their finite RenderExtent span); every other pairing is sampled into at most 1000 segments per curve. `foreach (var p in line.Intersect(circle).Points) new VPoint(p) { Color = \"Red\" };`" },

                // Frame
                { "Frame.Request", "Queues a callback for the next frame and returns a handle. The Action<double> overload receives elapsed seconds since the loop started; the Action overload is for callbacks that do not need it. Call it again from inside the callback to keep the loop running - that request lands on the next frame, not the current one, so the function does not re-enter itself. Requesting the same method twice queues it twice, as in JavaScript." },
                { "Frame.Cancel", "Removes a callback queued by Request, using the handle it returned. Unknown or already-run handles are ignored, so cancelling twice is safe." },
                { "Frame.Clear", "Drops every queued callback. Called automatically before each run, so a script never inherits the previous run's loops." },
                { "Frame.HasPending", "True while at least one callback is queued." },

                // Mouse
                { "Mouse.OnMove", "Registers the handler called when the pointer moves with NO button held — Mouse.OnMove(e => cursor.Center = e.Position). Pass null to detach. ASSIGNING REPLACES: a second call leaves one handler, not two, unlike Frame.Request which queues each request. While a button is held the move goes to OnDrag INSTEAD, with no fallback here, so a handler that must run during a drag has to be registered on both. Register it from Main(): handlers are dropped at the start of every run." },
                { "Mouse.OnDown", "Registers the handler called when a mouse button goes down; e.Button says which. Pass null to detach, and note that assigning replaces rather than adds. On the SECOND click of a double click this does not fire — OnDoubleClick fires in its place — so use OnClick if you want every click counted. Register it from Main(): handlers are dropped at the start of every run." },
                { "Mouse.OnUp", "Registers the handler called when a mouse button is released. Pass null to detach; assigning replaces rather than adds. It runs before any synthesised OnClick, and a drag always finishes with one — the canvas captures the mouse for the duration, so an up arrives even when the pointer left the canvas, and a handler tracking \"am I dragging?\" is never left stuck on." },
                { "Mouse.OnClick", "Registers the handler called after OnUp when the button went down and came back up within about 3 pixels — the usual \"the user clicked this\" event, which WPF does not give a bare canvas. A DRAG PRODUCES NO CLICK, which is exactly what makes it the right event for \"pick a shape\". The MouseInfo handed to it has Kind = Click and reuses the up event's Target, so hit-testing is not repeated. Pass null to detach; assigning replaces rather than adds." },
                { "Mouse.OnDoubleClick", "Registers the handler called on the second click of a double click, IN PLACE OF OnDown — so a down handler and a double-click handler do not both fire for that click. e.ClickCount is 2. In interactive mode the canvas's own double-click-zoom-to-fit is suppressed, so the gesture is yours. Pass null to detach; assigning replaces rather than adds." },
                { "Mouse.OnDrag", "Registers the handler called when the pointer moves with a button held, IN PLACE OF OnMove — it does NOT fall back to OnMove, so register both if the same work has to happen either way. Read e.LeftDown / e.RightDown / e.MiddleDown to see what is held (e.Button is None on a move). The canvas captures the mouse for the duration, so the drag keeps reporting even outside the canvas and always ends with an OnUp. Pass null to detach; assigning replaces rather than adds." },
                { "Mouse.OnWheel", "Registers the handler called when the wheel turns; read e.WheelNotches for the amount (or e.WheelDelta for the raw 120-per-notch WPF value). REGISTERING THIS IS WHAT TAKES THE WHEEL FROM THE CANVAS: with a wheel handler attached the canvas stops zooming on the wheel and the gesture is entirely yours. Interactive mode alone does NOT suppress wheel zoom — a sketch that only handles clicks or moves keeps it, because the wheel is the main way to navigate a drawing larger than the viewport. The zoom controls that fade in at the top-right of the viewport cell under the pointer are how the user zooms once you have taken the wheel; middle-drag still pans. Pass null to detach, which hands wheel zoom straight back on the next turn; assigning replaces rather than adds. HasWheelHandler reports the current state." },
                { "Mouse.OnEnter", "Registers the handler called when the pointer enters the canvas. Pass null to detach; assigning replaces rather than adds. Useful for showing a custom cursor shape that OnLeave hides again." },
                { "Mouse.OnLeave", "Registers the handler called when the pointer leaves the canvas. A drag in progress gets its OnUp FIRST, so a handler tracking \"am I dragging?\" is never left stuck on. Pass null to detach; assigning replaces rather than adds." },
                { "Mouse.CallbackFailed", "Event raised when a handler throws. The handlers are detached FIRST and then this is raised once, rather than letting the exception escape on every mouse move — user code runs in-process, and an unhandled exception from a move handler would reach WPF's dispatcher a hundred times a second and take the application down. DoodleSharp subscribes to it and prints the type and message to the console tagged \"Mouse\", so you normally just read the console; subscribe yourself if you want to react in code. Any half-finished work the handler did before throwing still reaches the screen." },
                { "Mouse.HasHandlers", "True while at least one handler is registered — which is also exactly what puts the canvas into interactive mode (click-to-select suppressed, double-click-zoom-to-fit suppressed, the F4 properties panel hidden and its menu item disabled). WHEEL ZOOM IS NOT PART OF THAT: the wheel is given up only when a wheel handler is registered, which HasWheelHandler reports separately, because losing the wheel would cost a script that merely watches clicks the main way to navigate a drawing larger than the viewport. Middle-drag panning, the drawing tools and the measuring tape are unaffected too. Read-only, and cheap: it is a plain field read, because the host tests it on every mouse event. Static." },
                { "Mouse.HasWheelHandler", "True while a wheel handler is registered, and the flag the canvas checks before deciding whether to zoom: it keeps its own wheel zoom unless user code has explicitly claimed the wheel with Mouse.OnWheel. This is deliberately narrower than HasHandlers — every OTHER handler leaves wheel zoom alone, so watching clicks does not cost the user the ability to navigate. Mouse.OnWheel(null) puts it back to false and hands the wheel back; Clear() does too, along with everything else. Read-only, and a plain field read because the canvas tests it on every wheel turn. Static." },
                { "Mouse.Clear", "Detaches every handler at once, which also takes the canvas out of interactive mode, hands the wheel back if a wheel handler had claimed it (HasHandlers and HasWheelHandler both go false), and resets the click/drag tracking. X, Y and IsDown deliberately survive — they describe where the pointer is, which stays true. The host calls this before each run and from the Stop button, and it is not optional: user code is compiled into a collectible AssemblyLoadContext, so a handler left registered would pin that assembly and keep firing against shapes the next run has already replaced. You rarely need to call it yourself; pass null to a single On* method to detach just that one." },
                { "Mouse.X", "Last known cursor X in world coordinates (Y-up, origin at the canvas centre). TRACKED EVEN WITH NO HANDLER REGISTERED, so it is usable from a Frame callback, a sketch's Draw(), or anywhere else that polls rather than reacts — reading it does not put the canvas into interactive mode. Static and read-only; it holds the last position seen and is not reset between runs." },
                { "Mouse.Y", "Last known cursor Y in world coordinates (Y-up, origin at the canvas centre). Tracked even with no handler registered, so polling it from a Frame callback or a sketch's Draw() costs nothing and does not put the canvas into interactive mode. Static and read-only." },
                { "Mouse.IsDown", "True while any mouse button is held over the canvas. Tracked even with no handler registered, so a Frame callback or a sketch's Draw() can poll it — the \"is the user pressing?\" flag for a paint-style sketch, with Mouse.X and Mouse.Y for where. It says nothing about WHICH button; register OnDown or OnDrag and read e.Button / e.LeftDown for that. Reset to false by Clear(). Static and read-only." },

                // MouseInfo
                { "MouseInfo.Kind", "Which kind of event this MouseInfo describes (MouseEventKind: Move, Down, Up, Click, DoubleClick, Drag, Wheel, Enter, Leave). It always matches the callback the event arrived through, so it is there for the case where one method is registered for several events and needs to switch on it." },
                { "MouseInfo.Position", "Cursor position in world coordinates — Y-up with the origin at the canvas centre, so it drops straight into a shape constructor with no conversion. This is the same value the rest of the app uses, which means it is GRID-SNAPPED while Snap to Grid (F9) is on and matches the coordinate readout in the status bar. Use RawPosition when you want the true cursor position regardless of snapping." },
                { "MouseInfo.RawPosition", "Cursor position in world coordinates, NEVER grid-snapped. Equal to Position unless Snap to Grid (F9) is on. This is also the point Target is hit-tested against, because hit-testing a snapped point would report whatever sits at the snap intersection rather than what is under the cursor — so pair RawPosition with Contains or DistanceTo when you are asking questions about what the pointer is over." },
                { "MouseInfo.X", "Shorthand for Position.X — world X, so grid-snapped when Snap to Grid is on. Read-only." },
                { "MouseInfo.Y", "Shorthand for Position.Y — world Y, measured UP from the canvas centre, and grid-snapped when Snap to Grid is on. Read-only." },
                { "MouseInfo.ScreenX", "Cursor X in device-independent pixels from the canvas's LEFT edge. Rarely what you want — geometry is built in world coordinates — but useful for decisions that are genuinely about pixels. Read-only." },
                { "MouseInfo.ScreenY", "Cursor Y in device-independent pixels from the canvas's TOP edge, INCREASING DOWNWARDS — the opposite of world Y, which increases upwards. Read-only." },
                { "MouseInfo.Button", "The button this event is ABOUT (MouseButtonKind): the one pressed or released on a down, up, click or double click, and MouseButtonKind.None on a move, drag, wheel turn, enter or leave. To ask what is currently HELD — during a drag, say — read LeftDown, RightDown and MiddleDown instead. Read-only." },
                { "MouseInfo.LeftDown", "Whether the left button is held down at the moment of this event. This is the one to read in an OnDrag handler, where Button is None. Read-only." },
                { "MouseInfo.RightDown", "Whether the right button is held down at the moment of this event. Read-only." },
                { "MouseInfo.MiddleDown", "Whether the middle button is held down at the moment of this event. Note that a middle-button DRAG remains the canvas's own pan gesture even in interactive mode — it is the only way to pan — so your drag handler does not run while it is in progress. Read-only." },
                { "MouseInfo.Shift", "Whether Shift is held. A plain bool: MouseInfo exposes no WPF types, so there is no ModifierKeys to unpack. Read-only." },
                { "MouseInfo.Ctrl", "Whether Ctrl is held. Read-only." },
                { "MouseInfo.Alt", "Whether Alt is held. Read-only." },
                { "MouseInfo.ClickCount", "1 for a single click, 2 for a double click, and 0 when the event is not about a button at all (a move, drag, wheel turn, enter or leave). On the synthesised click event it is at least 1. Read-only." },
                { "MouseInfo.WheelDelta", "How far the wheel turned, in WPF's RAW units of 120 per notch, positive away from the user. 0 unless Kind is MouseEventKind.Wheel. WheelNotches is the friendlier form and is what most code should read. Read-only." },
                { "MouseInfo.WheelNotches", "WheelDelta expressed in notches: 1.0 per detent, positive away from the user, computed as WheelDelta / 120.0. 0 unless the wheel turned. This is the value to scale a zoom or a step by. Read-only." },
                { "MouseInfo.Scale", "The canvas zoom factor when the event happened — screen pixels per world unit. Use it to express a pixel tolerance in world units: 8 / e.Scale is \"within 8 pixels\" whatever the zoom. Read-only." },
                { "MouseInfo.Target", "The topmost shape under the cursor, or null over empty space. Computed ON DEMAND and cached, so reading it costs nothing until you do and never costs twice — which matters because a move handler can run over a hundred times a second and most handlers never ask. Two things to know. It uses the same few-pixel tolerance the selection tool uses, so it answers \"what would clicking here have picked?\" rather than \"is the cursor strictly inside this shape?\" — use Shape.Contains(e.RawPosition) for the strict question. And while a timeline or a Frame loop is animating, the spatial index it consults holds the positions from the start of the frame, so it can LAG a fast-moving shape. The hit test runs against RawPosition, never the grid-snapped Position." },
                { "MouseInfo.Viewport", "Which cell of the viewport grid the pointer was in — the root when the drawing is undivided, which is every drawing that never sets Viewports.Rows or Viewports.Columns. Never null. Mouse handlers are registered once for the WHOLE drawing rather than per cell (a pointer has one onmousemove), so this is how one handler tells the cells apart: if (e.Viewport == Viewports[0][1]) { ... }. COMPARE BY REFERENCE, which == does here — a viewport keeps its identity across every resize that does not remove it, so a captured Viewport stays comparable, while its Path would change if the grid around it grew. Remember Position and RawPosition are in THAT cell's world coordinates, since every leaf has its own pan and zoom." },

                // MouseButtonKind
                { "MouseButtonKind.None", "No button — the value of MouseInfo.Button on a plain move, a drag, a wheel turn, or an enter/leave. Read LeftDown/RightDown/MiddleDown to find what is held on those events." },
                { "MouseButtonKind.Left", "The left button." },
                { "MouseButtonKind.Right", "The right button. It reaches your handlers in interactive mode, but only after an armed drawing tool has had its chance to treat the click as a cancel." },
                { "MouseButtonKind.Middle", "The middle button (the wheel pressed as a button). Reported to a down/up/click handler, but a middle-button DRAG stays the canvas's own pan gesture and does not reach your drag handler." },
                { "MouseButtonKind.XButton1", "The first extra button, if the mouse has one. Not present on most mice, so do not make it the only way to do something." },
                { "MouseButtonKind.XButton2", "The second extra button, if the mouse has one." },

                // MouseEventKind
                { "MouseEventKind.Move", "The pointer moved with no button held — delivered to Mouse.OnMove." },
                { "MouseEventKind.Down", "A button went down — delivered to Mouse.OnDown, except on the second click of a double click, where DoubleClick is delivered instead." },
                { "MouseEventKind.Up", "A button was released — delivered to Mouse.OnUp, before any synthesised Click." },
                { "MouseEventKind.Click", "A button went down and came back up within about 3 pixels. SYNTHESISED (see Mouse.OnClick), because WPF gives a bare canvas no click event; a drag therefore produces no Click." },
                { "MouseEventKind.DoubleClick", "A second click arrived inside the system double-click time — delivered to Mouse.OnDoubleClick in place of Down, with ClickCount 2." },
                { "MouseEventKind.Drag", "The pointer moved with a button held — delivered to Mouse.OnDrag in place of Move, and with no fallback to Move." },
                { "MouseEventKind.Wheel", "The wheel turned — delivered to Mouse.OnWheel. Read WheelNotches for the amount. You only ever see this kind once a wheel handler is registered: with none, the wheel stays the canvas's own zoom gesture and never reaches user code, unlike the other kinds which arrive as soon as any handler is attached." },
                { "MouseEventKind.Enter", "The pointer entered the canvas — delivered to Mouse.OnEnter." },
                { "MouseEventKind.Leave", "The pointer left the canvas — delivered to Mouse.OnLeave, after the OnUp of any drag that was in progress." },

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
                { "ValueAnimation.Apply", "Called by the timeline with the normalised time t (0 at this animation's start, 1 at its end) — YOU DO NOT CALL IT. It clamps t to [0, 1] (a negative t means \"not started yet\"), runs it through EasingFunction, then walks the value sequence: the eased time is scaled across the count-1 segments and the property is set by linear interpolation within whichever segment it lands in. So the two-value constructor is a straight A-to-B ramp, and the List<double> constructor gives evenly-spaced keyframes with the easing applied across the whole run rather than per leg. The property is set by reflection each frame, so it must be a writable double. Because Apply is a pure function of t the timeline is seekable — the Timeline panel's scrub bar and the GIF and MP4 exporters render time T directly instead of playing up to it." },

                // ObjectPropertyAnimation
                { "ObjectPropertyAnimation.Target", "Always null — this animation drives a property on an arbitrary object rather than a shape, so nothing is auto-drawn for it. The object's property setter is what moves the geometry." },
                { "ObjectPropertyAnimation.Duration", "Gets how long the object property animation takes (in seconds)." },
                { "ObjectPropertyAnimation.EasingFunction", "Gets or sets the easing function for smooth value interpolation." },
                { "ObjectPropertyAnimation.Apply", "Called by the timeline with the normalised time t — not by you. It clamps t to [0, 1], eases it, and writes startValue + (endValue - startValue) × easedT onto the target object's double property by reflection. Unlike every other animation, Animation.Target is null here: the point of this one is that it drives an ARBITRARY object rather than a Shape, so the timeline has no shape to place on the canvas or redraw. Nothing visible happens unless something else reads the property you are driving — typically your own code, recomputing geometry from it." },

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
                { "ArrayOps.LinearArray", "LinearArray(Shape shape, VXYZ direction, int count, double spacing) — count shapes IN TOTAL along direction, the original first and count-1 clones after it. direction is normalised internally, so spacing is a true world-unit distance whatever length you pass; VXYZ.BasisX gives a row along +X. A count of 1 returns just the original, and zero or less returns an empty list. End the chain with .DrawAll() — the clones have no Name, so the post-run sweep would otherwise hide them." },
                { "ArrayOps.RectangularArray", "RectangularArray(Shape shape, int rows, int cols, double rowSpacing, double colSpacing) — a rows × cols grid growing in +X (columns) and +Y (rows) from the original, which occupies the first cell. Returns rows × cols shapes in total. Zero or fewer rows or cols returns an empty list. Call .DrawAll() on the result." },
                { "ArrayOps.CircularArray", "CircularArray(Shape shape, VXYZ center, int count, double totalAngleDegrees = 360, bool rotateItems = true) — count shapes IN TOTAL around center, the original included. The angular step depends on whether the sweep closes: a full 360 divides by count (so the last copy does not land on the first), and a partial sweep divides by count-1 (so the last copy sits exactly at totalAngleDegrees). rotateItems: false translates each copy without turning it, which is what you want for text or symbols that must stay upright. Call .DrawAll() on the result." },
                { "ArrayOps.PathArray", "PathArray(Shape shape, ICurve path, int count, bool alignToPath = true) — count clones spread by EQUAL ARC LENGTH along any ICurve (line, arc, spline, polyline...). NOTE the original is NOT in the returned list, unlike LinearArray and CircularArray: you get exactly count clones. alignToPath rotates each clone to the curve's tangent there; pass false to keep them all in the source shape's orientation. Call .DrawAll() on the result." },
                { "ArrayOps.SpiralArray", "SpiralArray(Shape shape, VXYZ center, int count, double startRadius, double endRadius, double totalRevolutions = 1, bool rotateItems = true) — count clones winding from startRadius out to endRadius over totalRevolutions turns, radius and angle both interpolated linearly. As with PathArray the ORIGINAL IS NOT INCLUDED. endRadius smaller than startRadius spirals inward; a fractional totalRevolutions gives a partial turn. Call .DrawAll() on the result." },
                { "ArrayOps.Mirror", "Mirror(Shape shape, VLine mirrorLine) — returns a two-element list, [original, mirrored copy], reflected across the INFINITE line through mirrorLine's Start and End (not just the segment). The original is included, so this one list is the complete symmetric pair. Call .DrawAll() on it." },

                // BooleanOps
                { "BooleanOps.Union", "Combines two or more polygons into one. Returns a single VPolygon if successful, or null when it cannot form one — and then it reports why through GeometryDiagnostics (the console, tagged 'Geometry'): no polygons passed, an empty result, or N disjoint regions because the inputs never overlapped or touched. When you want every piece instead of a null, call BooleanOps.UnionAll, which returns List<VPolygon> and never returns null; or BooleanOps.UnionWithHoles(a, b) when the merged outline can enclose voids you care about. There are also Region overloads — Union(Region a, Region b, int segmentsPerCurve = 32) and Union(IEnumerable<Region> regions, int segmentsPerCurve = 32) — which forward to RegionBooleanOps and now carry the sampling precision through; there is deliberately no params Region[] form here, because it would make the argument-less BooleanOps.Union() ambiguous with params VPolygon[]." },
                { "BooleanOps.UnionAll", "Unions any number of polygons and returns EVERY resulting piece as a List<VPolygon> — never null, which is the difference from Union. Overlapping inputs merge into one piece; inputs that touch nothing come back as separate pieces; an empty input gives an empty list and a single input gives a copy of it. Overloads take params VPolygon[] or IEnumerable<VPolygon>. This is what the console diagnostic points you at when Union returns null. HOLES ARE NOT REPRESENTED in the result: if the merged outline can enclose a void that matters to you, use UnionWithHoles(a, b), which returns List<PolygonWithHoles> — though that form takes exactly two polygons. Results are unnamed method results, so Place() or name anything you want to keep." },
                { "BooleanOps.Intersect", "The overlapping area of two polygons (logical AND). Region overloads forward to RegionBooleanOps and take the sampling precision: Intersect(Region a, Region b, int segmentsPerCurve = 32) and Intersect(IEnumerable<Region>, int segmentsPerCurve = 32) — the params Region[] form cannot take it, so pass a list when you need to raise it." },
                { "BooleanOps.Difference", "Subtracts one polygon from another (a minus b). Region overloads forward to RegionBooleanOps and take the sampling precision: Difference(Region a, Region b, int segmentsPerCurve = 32) and Difference(IEnumerable<Region>, int segmentsPerCurve = 32), where the collection form is the first region minus every other. The params Region[] form cannot take the precision argument." },
                { "BooleanOps.Xor", "The symmetric difference of two polygons — the parts belonging to one but not both. Region overloads forward to RegionBooleanOps and take the sampling precision: Xor(Region a, Region b, int segmentsPerCurve = 32) and Xor(IEnumerable<Region>, int segmentsPerCurve = 32), the latter folding a running symmetric difference. The params Region[] form cannot take the precision argument." },
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
                { "CurveIntersection.Intersect", "Computes the intersection of two curves, dispatching on the pair of runtime types: line/line, line/circle, line/arc, line/ellipse, circle/circle, circle/arc and arc/arc use exact closed-form math (in either argument order); VRay and VXLine are converted to the finite segment spanning their RenderExtent and re-dispatched, so a ray against a circle takes the exact circle path rather than being sampled; every other combination falls through to IntersectGeneric, which samples both curves into segments. Returns an IntersectionResult holding Points and, for collinear overlapping lines, Curves." },
                { "CurveIntersection.IsSelfIntersecting", "Returns true when a curve crosses itself. VLine, VCircle, VArc, VEllipse and VRectangle are always false by construction; VPolyline, VPolygon, VBezier and VSpline are actually tested. Any other curve type returns false." },
                { "CurveIntersection.IntersectLineLine", "Exact intersection of two line segments. Returns a single point when they cross within both segments, or — when they are collinear and overlap — an IntersectionResult whose Curves holds the shared segment (HasOverlap is true). Parallel non-collinear lines give an empty result." },
                { "CurveIntersection.IntersectLineCircle", "Exact intersection of a line segment and a circle: 0, 1 (tangent) or 2 points, limited to the extent of the segment." },
                { "CurveIntersection.IntersectLineArc", "Exact intersection of a line segment and an arc. Circle roots outside the arc's start/end angle sweep are discarded." },
                { "CurveIntersection.IntersectLineEllipse", "Exact intersection of a line segment and an ellipse: 0, 1 (tangent) or 2 points. VEllipse.Rotation IS honoured — the line is taken into the ellipse's own frame, where the closed form applies, and the parameter that falls out is the same parameter on the original line, so the points come back in world coordinates with nothing to rotate back. The SWEEP is not: the ellipse is treated as COMPLETE, so a partial VEllipse's StartAngle/EndAngle is not applied here and you should filter the points against the drawn part yourself if that matters." },
                { "CurveIntersection.IntersectCircleCircle", "Exact intersection of two circles: 0, 1 (tangent) or 2 points. Two coincident circles (same centre and radius) return the circle itself in Curves, so HasOverlap is true and Points is empty." },
                { "CurveIntersection.IntersectCircleArc", "Exact circle/circle intersection filtered to the arc's angular sweep." },
                { "CurveIntersection.IntersectArcArc", "Exact circle/circle intersection filtered to both arcs' angular sweeps." },
                { "CurveIntersection.IntersectGeneric", "Fallback intersection by segment decomposition: both curves are sampled with GetSegments, every segment pair is tested, and duplicate points are merged. Works for any ICurve pair, at sampling accuracy — though a VLine, VPolyline or VPolygon operand contributes its REAL edges, so only genuinely curved operands are approximated. Intersect() no longer routes VRay and VXLine here (it converts them to their finite span and re-dispatches to the exact routines), but calling this directly with one still samples it into the 1000-segment cap." },
                { "CurveIntersection.GetSegments", "Samples a curve into line segments — VLine returns itself, VPolygon/VPolyline return their edges, and other curves are divided into length × segmentsPerUnit pieces (minimum 2, capped at 1000) — and since VRay and VXLine report an infinite GetLength(), they always hit that cap, spread over their RenderExtent. The synthesised segments are built through an internal non-registering factory, so they never appear on the canvas — but they are ordinary VLine objects to you, and moving or styling one has no effect on the source curve." },
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
                { "Parameter.ToString", "Returns \"Name = literal\", where the right-hand side is ToLiteral() — the value formatted as it would be written in C# source, so a string comes back quoted and escaped, a bool as true/false, and a date as DateTime.Parse(\"...\"). It is the parameter as the panel would write it back into your code, not a plain value dump." },
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
                { "GeometryHelper.SweepContains", "SweepContains(startDegrees, endDegrees, angleDegrees) — true when the sweep from startDegrees to endDegrees passes through angleDegrees. All three are in DEGREES, counter-clockwise from the positive X axis, and this is the one rule VArc, VEllipse and RayCaster all use to decide whether an angle lies on the DRAWN part of a curve — so a test you write with it agrees with the shape's own length, bounds and hit test. It works on the OFFSET from the start rather than on normalised absolute angles, which is what makes it right in the two cases a normalising test gets wrong. DIRECTION: a clockwise sweep (EndAngle below StartAngle) is read the way it travels, so SweepContains(90, 0, 45) is true. PAST THE WRAP: 350 to 370 is a 20-degree sweep, so SweepContains(350, 370, 5) is true (5 degrees is 365 on that sweep) while SweepContains(350, 370, 180) is false — a test that folded both ends into [0, 360) first would read it as the 340-degree sweep 350 to 10 and answer the other way round for both. Edges: a sweep of a full turn or more in either direction contains every angle; a zero sweep (start == end) contains only that angle; the ends themselves count, the comparison carrying a small tolerance." },
                { "GeometryHelper.SweepOffset", "SweepOffset(startDegrees, endDegrees, angleDegrees) — how far angleDegrees lies along the sweep from startDegrees to endDegrees, in DEGREES, measured in the direction the sweep travels and CLAMPED to it, so startDegrees + the result is always an angle the sweep actually reaches. It is SIGNED: positive on a counter-clockwise sweep and NEGATIVE on a clockwise one, so add it to the start rather than treating it as a magnitude. SweepOffset(0, 90, 45) is 45; SweepOffset(90, 0, 45) is -45; SweepOffset(0, 90, 200) clamps to 90; SweepOffset(350, 370, 5) is 15, because 5 degrees is 365 on that sweep. Two ways to use it: startDegrees + the result is the angle ON the curve nearest the one you asked about, which is how VArc.SplitAtPoint and VEllipse.SplitAtPoint find where to cut; and the result divided by (endDegrees - startDegrees) is the curve's own [0, 1] parameter, which is how VArc.ParameterAtPoint answers. There is no tolerance and no normalisation of the result — an angle outside the sweep comes back as the nearer end, not as an angle beyond it." },
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
                { "IDrawable.LineWeight", "Gets or sets the stroke thickness. Default 2.0, and interpreted as DEVICE PIXELS — so a stroke keeps the same on-screen width at any zoom — unless Settings > Application Settings > Line Style Rendering > Display Line Weight is ticked, which reads it as world units instead. See Shape.LineWeight for the full contract." },
                { "IDrawable.LineType", "Gets or sets the stroke pattern: Continuous, Dashed, Dotted, DashDot, DashDotDot, Center, Phantom or Hidden. The dash and gap lengths themselves come from C2VGeometry.Rendering.LineTypePatterns." },
                { "IDrawable.LineTypeScale", "Gets or sets the multiplier on the dash and gap lengths. Default is 1.0; values above 1 lengthen dashes and gaps, below 1 shorten them, and a non-positive value renders solid. Dash lengths are a fixed ON-SCREEN size: they do not follow Display Line Weight, do not change with zoom, and do not change with LineWeight." },

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
                { "BoundingBox.ToString", "Returns \"BoundingBox(Min: (x, y, z), Max: (x, y, z))\" — the two corners, in world coordinates. Remember the world is Y-up, so Min is the BOTTOM-left corner." },

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
                { "Canvas.Clear", "Removes every shape from the canvas. Geometry only: it does not rewind shape ids, stop a running timeline or reset the view - those belong to the host's between-runs reset. Safe with no canvas attached (it becomes a no-op), so it works in a unit test. Reach for it when the SET of shapes changes; if only positions change, build the shapes once and assign to them instead, which allocates nothing per event." },
                { "Canvas.Remove", "Takes the named shapes off the canvas: Remove(a, b, c) or Remove(someList). Nulls are skipped and a shape that is not on the canvas is ignored, so it is safe to call with a list you are also rebuilding. The list overload materialises its argument first, so you can pass a live view of the registry without it throwing mid-iteration while the shapes are being taken off. Note the element type is Shape, so a registry snapshot needs projecting: CanvasRenderer.Instance.GetShapes() is IReadOnlyList<IDrawable> and will not bind - write .OfType<Shape>() (or just call Canvas.Clear(), which is what you usually mean). Equivalent to calling shape.Remove() on each." },
                { "IShapeRegistry.Register", "Called by every Shape constructor when Shape.AutoRegister is true and Shape.DefaultRegistry is set — this is why shapes appear without an explicit call. Also called by Shape.Place() (and its alias Draw())." },
                { "IShapeRegistry.Unregister", "Removes a shape from the canvas. Called by Shape.Remove()." },
                { "IShapeRegistry.Clear", "Removes EVERY registered shape. This is what C2VGeometry.Canvas.Clear() calls, and it means exactly \"take everything off the canvas\": geometry only, so it must not rewind the shape id counter, stop a running timeline, or touch anything else belonging to the host's run lifecycle — all of which would be a nasty surprise fired from inside a mouse handler. DoodleSharp implements it EXPLICITLY (CanvasRenderer keeps a separate public Clear() for the between-runs reset that does do those things, and routes the interface member to ClearShapes()). Implementing it is not optional — it was added to the interface rather than given a default no-op body, because a registry that silently fails to clear is worse than one that fails to compile." },
                { "IShapeRegistry.NotifyOrderChanged", "Called when a shape's ZIndex is assigned, telling the host that the draw order it is holding is stale and has to be re-derived before the next paint. It replaced the MoveAbove/MoveBehind pair, which reordered the host's list directly - order is now a property of the shape (ZIndex ascending, creation order breaking ties) and the registry is merely told to re-sort. DoodleSharp's implementation drops its cached draw order and bumps the registry version, which is what makes a ZIndex assigned inside a mouse handler reach the screen." },
                { "IShapeRegistry.Place", "Place(Shape shape, Viewport viewport) — what shape.Place(viewport) calls. Registers the shape if it is not already, and assigns it to that cell of the viewport grid, taking it off whichever cell it was on. Because a shape auto-registers when it is CONSTRUCTED, long before any Place(viewport) call, this is almost always a MOVE rather than a first registration — which makes it a change to the shape SET from each viewport's point of view, so an implementation that caches per-viewport lists must drop them and bump whatever version counter its per-frame paths compare, or the shape keeps drawing in the cell it came from. It is a member of its own rather than a second meaning for NotifyOrderChanged, deliberately: overloading that one would have let every existing implementation compile while silently dropping viewport assignment, and a compile error in an implementer is the cheaper failure. DoodleSharp's CanvasRenderer routes it to PlaceOnViewport, storing the root as absence so an undivided canvas keeps the whole partitioning path empty." },

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
                { "VCoordinateSystem.ToString", "Returns \"CoordinateSystem(Origin=..., X=..., Y=..., Z=...)\" — note the text says CoordinateSystem without the V. Prints the origin and all three axis vectors, which is what you want when checking that a factory produced the frame you expected." },

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
                { "DxfExporter.Export", "Export(IReadOnlyList<IDrawable> shapes, string filePath) — writes the shapes to a DXF file (R12 ASCII). Pass CanvasRenderer.Instance.GetShapes() for the whole drawing, or any list you have built yourself. Coordinates go out unchanged: one drawing unit is one DXF unit, Y still points up. Circles and arcs stay true CIRCLE/ARC entities rather than being flattened to chords, so the file is usable in a CAD package; ellipses are polygonised over their real sweep (72 segments, through VEllipse.PointAtAngle, so Rotation and a partial sweep both survive) because R12 has no ELLIPSE entity, and shape types with no DXF equivalent are decomposed into polylines instead of being dropped. A rotated VRectangle is written through its four corner points, so its RotationAngle survives too. A multi-line VText becomes one TEXT entity per line, stacked 1.2 text heights apart, since R12 TEXT has no multi-line form; VText.Justify and VText.Anchor are not applied to them." },
                { "DxfExporter.ExportToString", "ExportToString(IReadOnlyList<IDrawable> shapes) — the same DXF content returned as a string instead of written to disk, for inspecting it, embedding it, or sending it somewhere other than a file." },

                // PdfExporter
                { "PdfExporter.Export", "Two overloads. Export(shapes, filePath) auto-sizes the page to the drawing's bounds and picks a sensible scale. Export(shapes, filePath, pageWidthMm, pageHeightMm, scaleMmPerUnit, marginMm) gives you the sheet: page size in millimetres (pass 0 for either dimension to auto-size to content), scaleMmPerUnit is how many millimetres on paper one drawing unit becomes, and marginMm is the border. Output is real vector PDF — colours, line weights and dash patterns are preserved, a rotated VRectangle and a partial or turned VEllipse export as drawn, and a multi-line VText is written one line at a time, honouring Anchor and Justify. An empty shape list writes nothing. There are no PageSize or Margin properties; everything is an argument to this call." },
                { "PdfExporter.ExportTiled", "ExportTiled(IReadOnlyList<PdfExporter.PdfTile> tiles, string filePath, double containerWidth, double containerHeight, double marginMm = 10) — exports a DIVIDED canvas: every cell tiled onto one page exactly as it appears on screen, each at its own pan and zoom, fitted to the page as a whole. containerWidth and containerHeight are the on-screen container the tiles' PageRects are measured within; the page keeps that aspect ratio and the cells keep their relative positions, so the result is the screen on paper. THERE IS NO scaleMmPerUnit HERE, deliberately: millimetres of paper per drawing unit has no single meaning across cells sitting at different zooms, so the plotted-to-scale overload is only offered for an undivided drawing." },

                // SvgExporter (static class, namespace DoodleSharp.Canvas)
                { "SvgExporter.Export", "Export(IEnumerable<IDrawable> shapes, double width = 800, double height = 600, double padding = 20) — returns the complete SVG document as a string. width and height become the <svg> element's size in PIXELS. padding is in WORLD units, not pixels: it widens the shapes' own bounding box before that box is written out as the viewBox, so the same padding value covers more of the picture the more you zoom the drawing out. The Y flip from world coordinates (Y up) to SVG coordinates (Y down) is handled for you by a scale(1, -1) group. Shape types with a native SVG element get one; anything else is flattened to <path> polylines rather than dropped — as is anything the element cannot express: a rotated VRectangle, a partial VEllipse. A multi-line VText becomes one <text> element per line and keeps its Anchor and its Justify. An empty shape list still returns a valid document, sized from width and height. Static — there is nothing to construct, and no Width/Height properties to set." },
                { "SvgExporter.SaveToFile", "SaveToFile(string filePath, IEnumerable<IDrawable> shapes, double width = 800, double height = 600) — the same document as Export, written straight to filePath (padding fixed at its default). Note the argument order: the path comes first. Example: SvgExporter.SaveToFile(@\"C:\\\\temp\\\\drawing.svg\", CanvasRenderer.Instance.GetShapes(), 1200, 900);" },
                { "SvgExporter.ExportTiled", "Static. ExportTiled(IReadOnlyList<SvgExporter.SvgTile> tiles, double width, double height) — exports a DIVIDED canvas: every cell tiled onto one page exactly as it appears on screen, each at its own pan and zoom, returned as an SVG string. Each tile carries its own rectangle on the page, its scale and its pan, and the transform is derived from that rather than re-computed here, which is what makes \"as it appears on screen\" literal instead of approximate. Shapes are still emitted in world coordinates; one matrix per tile carries the cell's scale, its pan, its position on the page and the Y flip from mathematical to screen coordinates all at once, and each tile is clipped to its own rectangle. With more than one tile a thin separator rectangle is stroked round each cell, drawn last so geometry cannot paint over it. THIS IS DELIBERATELY NOT WHAT Export DOES for an undivided drawing: Export fits the SHAPES with padding and ignores the screen entirely, so the two produce different pictures and switching the single-cell case over would silently change the output of every export ever made." },
                { "SvgExporter.SaveTiledToFile", "Static. SaveTiledToFile(string filePath, IReadOnlyList<SvgExporter.SvgTile> tiles, double width, double height) — the same document as ExportTiled, written straight to filePath. Note the argument order: the path comes first, as it does in SaveToFile." },

                // SvgExporter.SvgTile / PdfExporter.PdfTile — one cell of a divided canvas.
                { "SvgTile.PageRect", "System.Windows.Rect. Where this cell sits on the page, in DEVICE PIXELS — X and Y from the page's top-left, plus Width and Height. Everything drawn for the tile is clipped to it, and with more than one tile it is also stroked as the separator you see between cells." },
                { "SvgTile.Scale", "SCREEN PIXELS PER WORLD UNIT in this cell — that leaf's own zoom, and the same quantity as MouseInfo.Scale, not its reciprocal. Multiply a world distance by it to get pixels; divide to go the other way. Every leaf carries its own, which is the whole point of tiling rather than fitting the drawing once." },
                { "SvgTile.PanX", "This cell's horizontal pan, in pixels, exactly as the on-screen canvas holds it." },
                { "SvgTile.PanY", "This cell's vertical pan, in pixels. It is applied before the Y flip from world (Y up) to SVG (Y down), so you pass the canvas's value through unchanged." },
                { "SvgTile.Shapes", "What is placed on this cell — IReadOnlyList<IDrawable>, the same type PdfTile.Shapes uses, so a tiling built for one exporter transfers to the other unchanged. In the app this comes from CanvasRenderer.GetShapes(leaf)." },
                { "SvgTile.Deconstruct", "Lets a tile be destructured in one line, because SvgTile is a record struct: var (rect, scale, panX, panY, shapes) = tile;" },
                { "SvgTile.Equals", "Value equality generated for the record struct: two tiles are equal when PageRect, Scale, PanX, PanY and Shapes all match. Shapes is compared by REFERENCE, being a sequence, so two tiles holding equivalent but distinct lists are not equal." },
                { "SvgTile.GetHashCode", "Hash generated for the record struct, combining all five members. Shapes contributes its reference hash, so it is stable but not structural." },
                { "SvgTile.ToString", "The record struct's generated form: \"SvgTile { PageRect = ..., Scale = ..., PanX = ..., PanY = ..., Shapes = ... }\". Handy when checking a tiling by eye; the Shapes part is just the collection's own ToString." },

                { "PdfTile.PageRect", "System.Windows.Rect. Where this cell sits inside the on-screen container, in DEVICE PIXELS. ExportTiled maps the container onto the page, so these are container coordinates rather than millimetres — the page keeps the container's aspect ratio and each rectangle scales with it." },
                { "PdfTile.Scale", "SCREEN PIXELS PER WORLD UNIT in this cell — that leaf's own zoom, and the same quantity as MouseInfo.Scale, not its reciprocal. Multiply a world distance by it to get pixels. Cells at different zooms carry different values, which is why a per-drawing plot scale (scaleMmPerUnit) is not offered for a tiled export." },
                { "PdfTile.PanX", "This cell's horizontal pan, in pixels, exactly as the on-screen canvas holds it." },
                { "PdfTile.PanY", "This cell's vertical pan, in pixels, applied before the flip from world Y-up to page coordinates." },
                { "PdfTile.Shapes", "What is placed on this cell — IReadOnlyList<IDrawable>, the same type SvgTile.Shapes uses, so a tiling built for one exporter transfers to the other unchanged. In the app this comes from CanvasRenderer.GetShapes(leaf)." },
                { "PdfTile.Deconstruct", "Lets a tile be destructured in one line, because PdfTile is a record struct: var (rect, scale, panX, panY, shapes) = tile;" },
                { "PdfTile.Equals", "Value equality generated for the record struct: two tiles are equal when PageRect, Scale, PanX, PanY and Shapes all match. Shapes is compared by REFERENCE, so two tiles holding equivalent but distinct lists are not equal." },
                { "PdfTile.GetHashCode", "Hash generated for the record struct, combining all five members. Shapes contributes its reference hash, so it is stable but not structural." },
                { "PdfTile.ToString", "The record struct's generated form: \"PdfTile { PageRect = ..., Scale = ..., PanX = ..., PanY = ..., Shapes = ... }\"." },

                // SnapType (enum, namespace DoodleSharp.Canvas)
                { "SnapType.None", "No snap kind. SnapEngine never RETURNS a result of this type - when nothing is in range FindSnapPoint returns null instead - so you will only ever see it on a SnapResult you built yourself without setting Type. It is deliberately the zero value and deliberately first in the enum: that is what makes an unassigned SnapType field read as None rather than as Endpoint. Reordering the enum, or dropping this member because nothing returns it, would silently change what default(SnapType) means." },
                { "SnapType.Endpoint", "The start or end of an open curve, or a vertex of a polyline, polygon or rectangle. Highest priority of all eight, so a slightly more distant endpoint still beats a nearer Nearest or Extension candidate." },
                { "SnapType.Midpoint", "The middle of a segment or curve — half way along a line, or the mid-parameter point of an arc. Second priority." },
                { "SnapType.Center", "The centre of a circle, ellipse or arc. Note this is the centre POINT, which for an arc is not on the curve at all. Third priority." },
                { "SnapType.Intersection", "A point where two shapes cross. Found by testing shapes against each other, so it is the one snap type whose cost grows with the number of shapes in range — IntersectionSnapEnabled turns it off. Fourth priority." },
                { "SnapType.Nearest", "The closest point anywhere on a curve. LOWEST priority of the eight, deliberately: it can always produce a candidate, so ranking it above anything else would mean you could never reach an endpoint or a midpoint." },
                { "SnapType.Perpendicular", "The point on a shape at which a line from SnapEngine.ReferencePoint meets it at a right angle. Needs ReferencePoint to be set (the drawing tool sets it to your first click); with it null this type produces nothing. ReferenceSource and ConstraintPoint on the result carry the two ends of the relationship. Fifth priority." },
                { "SnapType.Extension", "A point on the invisible continuation of an existing edge, beyond its endpoint — what lets you line a new point up with a wall that stops short. The result carries ExtensionSource (the endpoint it runs from) and ExtensionAngle (its direction in DEGREES). Seventh priority, above Nearest only." },
                { "SnapType.Tangent", "The point on a circle or arc where a line from SnapEngine.ReferencePoint would just touch it. Like Perpendicular it needs ReferencePoint; the result also carries TangentCenter, the centre of the circle being touched. Sixth priority." },

                // SnapResult (namespace DoodleSharp.Canvas)
                { "SnapResult.Point", "The snapped position, in WORLD coordinates (Y up, origin at the canvas centre). This is the point a drawing tool actually places — not the cursor position it was found from. Always populated." },
                { "SnapResult.Type", "Which SnapType produced this candidate. It is also what decides ties: SnapEngine ranks by type first and by Distance only within a type." },
                { "SnapResult.Distance", "How far Point is from the cursor, in WORLD units. Compare candidates of the SAME type with it; across types the type ranking wins. Always populated." },
                { "SnapResult.ExtensionSource", "Extension snaps only, null otherwise. The endpoint the invisible continuation runs from — draw a dotted line from here to Point to show the user what they are aligned with." },
                { "SnapResult.ExtensionAngle", "Extension snaps only. The direction of the extended edge in DEGREES, counter-clockwise from +X. Left at 0 for every other snap type, so test Type before reading it." },
                { "SnapResult.ReferenceSource", "Perpendicular and Tangent snaps only, null otherwise. The point the relationship was measured from — whatever SnapEngine.ReferencePoint held, which for the drawing tool is your first click." },
                { "SnapResult.ConstraintPoint", "OBSOLETE - use Point. Nominally the point where a Perpendicular snap lands on the shape, or where a Tangent snap touches it, and null for every other type. In practice it is always EXACTLY Point, and inherently so: the foot of the perpendicular IS the perpendicular snap point, and the touch point IS the tangent snap point, so there is no configuration in which the two differ. Nothing reads it. It is deprecated rather than deleted so existing code keeps compiling, with a warning that names the replacement." },
                { "SnapResult.TangentCenter", "Tangent snaps only, null otherwise. The centre of the circle or arc being touched, which is what lets an overlay draw the radius to the tangent point." },

                // SnapEngine (namespace DoodleSharp.Canvas)
                { "SnapEngine.FindSnapPoint", "Two overloads, both returning the winning SnapResult or null when nothing is within tolerance. FindSnapPoint(VXYZ cursorWorld, IReadOnlyList<IDrawable> shapes, double scale) considers every shape you hand it — CanvasRenderer.Instance.GetShapes() is the usual argument. FindSnapPoint(VXYZ cursorWorld, SceneIndex spatialIndex, double scale) lets the cull index narrow the candidates first, which is what the canvas uses on large drawings; that overload THROWS ArgumentNullException on a null index rather than returning null - it holds no shapes of its own, so it cannot fall back to a full scan, and answering \"no snap\" would be indistinguishable from an empty neighbourhood and would silently disable snapping on every mouse move. scale is the canvas zoom: the tolerance is a fixed 15 SCREEN pixels internally and is divided by scale to get a world tolerance, so pass 1.0 if you are working in world units and want a 15-unit radius. Candidates are ranked by SnapType first and Distance second." },
                { "SnapEngine.SyncFromSettings", "Overwrites all eight snap toggles from the application's Snap Settings (Settings > Application Settings > Snap Settings). Nothing calls it for you except DrawingTool's constructor and DrawingTool.RefreshSnapSettings(), so an engine you construct yourself keeps whatever you set on it until you call this — which also means calling it will discard your own toggle choices." },
                { "SnapEngine.ReferencePoint", "The point that Perpendicular and Tangent snaps measure FROM — in a drawing tool, your first click. VXYZ?, null by default, and while it is null neither of those two types can produce a candidate at all. DrawingTool sets it after the first OnLeftClick and clears it when the shape finishes or is cancelled." },
                { "SnapEngine.EndpointSnapEnabled", "Whether Endpoint snaps are collected. True by default, like all eight toggles. Set it directly, or call SyncFromSettings() to take the application's setting instead." },
                { "SnapEngine.MidpointSnapEnabled", "Whether Midpoint snaps are collected. True by default." },
                { "SnapEngine.CenterSnapEnabled", "Whether Center snaps are collected. True by default." },
                { "SnapEngine.IntersectionSnapEnabled", "Whether Intersection snaps are collected. True by default. This is the one worth turning off on a dense drawing: intersections are found by testing shapes against each other, so unlike the rest its cost grows with the number of shapes in range rather than being per-shape." },
                { "SnapEngine.NearestSnapEnabled", "Whether Nearest snaps are collected. True by default. Turning it off is the way to stop 'anywhere on the curve' answering when you wanted a real feature point — though the type ranking already puts it last." },
                { "SnapEngine.PerpendicularSnapEnabled", "Whether Perpendicular snaps are collected. True by default. Has no effect while ReferencePoint is null, because the type needs a point to measure from." },
                { "SnapEngine.ExtensionSnapEnabled", "Whether Extension snaps are collected. True by default." },
                { "SnapEngine.TangentSnapEnabled", "Whether Tangent snaps are collected. True by default. Like Perpendicular, it needs ReferencePoint to be set." },

                // DrawingMode / DrawingInputMode (enums, namespace DoodleSharp.Canvas)
                { "DrawingMode.None", "Idle — no shape is being drawn. Every click handler on DrawingTool returns false in this state, so the host can pass the event on to selection or panning. SetMode(DrawingMode.None) and Cancel() both land here." },
                { "DrawingMode.Point", "One click places a VPoint." },
                { "DrawingMode.Line", "Two clicks: start, then end. Produces a VLine. This is the L keyboard shortcut on the canvas." },
                { "DrawingMode.Circle", "Two clicks: the centre, then a point at the RADIUS distance from it. Produces a VCircle. This is the C keyboard shortcut." },
                { "DrawingMode.CircleDiameter", "Two clicks: the centre, then a point whose distance from it is read as the DIAMETER — so the resulting circle is half the size the same two clicks would give in Circle mode." },
                { "DrawingMode.CircleTwoPoints", "Two clicks that are the opposite ENDS of a diameter; the centre falls half way between them. Built through VCircle.FromTwoPoints." },
                { "DrawingMode.CircleThreePoints", "Three clicks on the circumference — the circumcircle through them. Collinear points have no circumcircle, so the underlying VCircle(p1, p2, p3) constructor throws." },
                { "DrawingMode.Rectangle", "Two clicks: any corner, then the opposite one. The corners are normalised, so it does not matter which way round you drag. Produces a VRectangle. This is the R keyboard shortcut." },
                { "DrawingMode.Ellipse", "Two clicks: the centre, then a point whose X and Y offsets from it become RadiusX and RadiusY. Produces a VEllipse." },
                { "DrawingMode.Arc", "Three clicks: the start, a point ON the arc, then the end — not centre-start-end. The centre is derived as the circumcentre, and the middle click decides which way round the sweep goes. Draw > Arc in the menu offers ten further constructions that map onto the VArc.From* factories." },
                { "DrawingMode.Polygon", "Click each vertex; a DOUBLE-click finishes it (OnDoubleClick, not OnLeftClick). Needs at least three points, and the closing edge back to the first vertex is implicit. Produces a VPolygon." },
                { "DrawingMode.Polyline", "Click each point; a DOUBLE-click finishes it. Needs at least two points. Produces an open VPolyline." },
                { "DrawingMode.Bezier", "Four clicks: start, first control point, second control point, end. Produces a VBezier." },
                { "DrawingMode.Spline", "Click each control point; a DOUBLE-click finishes it. Needs at least two. Produces a VSpline through the points." },
                { "DrawingMode.Arrow", "Two clicks: tail, then head. Produces a VArrow." },
                { "DrawingMode.Text", "One click for the position — and then, unlike every other mode, NOTHING is created. The tool raises TextPlacementRequested with the location and waits; the host asks the user for the string and calls CompleteText(location, content), which is what finally builds the VText and raises ShapeCompleted." },
                { "DrawingInputMode.None", "No keyboard entry in progress: the next point follows the mouse and whatever it snaps to. The default, and where Enter and Escape both return you to." },
                { "DrawingInputMode.Distance", "Typed digits set the distance from the last placed point; the direction still comes from the cursor. Reached by Tab from None, or immediately by typing a digit while drawing. On Enter the value lands in DrawingTool.OverrideDistance." },
                { "DrawingInputMode.Angle", "Typed digits set the direction in DEGREES, counter-clockwise from +X; the distance still comes from the cursor unless OverrideDistance is also set. Reached by Tab from Distance. On Enter the value lands in DrawingTool.OverrideAngle." },

                // DrawingTool (namespace DoodleSharp.Canvas)
                { "DrawingTool.Mode", "Which shape is being drawn, as a DrawingMode. Read-only — change it with SetMode, which also clears the points in progress and raises ModeChanged. DrawingMode.None means idle." },
                { "DrawingTool.Points", "The click points collected so far for the shape in progress, in world coordinates and in click order. The list itself is read-only as a property but its CONTENTS are the tool's working state — treat it as read-only. Cleared by SetMode, Cancel, OnRightClick and by completing a shape." },
                { "DrawingTool.CurrentPoint", "The last position passed to OnMouseMove, after the orthogonal constraint has been applied but before snapping. VXYZ?, null until the mouse has moved. GetPreviewShape() and GetEffectiveEndPoint() both read it." },
                { "DrawingTool.IsOrthoMode", "The orthogonal constraint — what holding Shift does. When true and at least one point is down, the next point is forced onto the horizontal or vertical through the previous one, whichever the cursor is closer to. Settable; the canvas mirrors the live Shift key onto it. Applied BEFORE snapping." },
                { "DrawingTool.CurrentSnap", "The SnapResult under the cursor as of the last OnMouseMove, or null if nothing was in range. Read-only. This is what the canvas draws the snap marker from, and what OnLeftClick uses in place of the raw cursor position." },
                { "DrawingTool.SnapEngine", "The tool's own SnapEngine, created in the constructor and already synced from the application's Snap Settings. Get-only, but the engine itself is mutable — this is where you reach in to toggle a snap type or read ReferencePoint, which the tool sets to your first click." },
                { "DrawingTool.InputMode", "Which value the keyboard is currently editing, as a DrawingInputMode. Read-only — CycleInputMode(), StartDistanceInput(), HandleEnterInput(), HandleEscapeInput() and ResetInputMode() are what change it." },
                { "DrawingTool.InputBuffer", "The characters typed so far in the current input mode, as a string (\"\" when nothing has been typed). Read-only. Digits, a single '.', and a leading '-' are accepted; anything else is refused by HandleCharInput." },
                { "DrawingTool.IsBufferSelected", "True when the buffer holds a pre-filled value that the next keystroke should REPLACE rather than append to — the same behaviour as text selected in a text box. Set when CycleInputMode or StartDistanceInput pre-populates the buffer from the current distance or angle. Read-only." },
                { "DrawingTool.OverrideDistance", "The committed distance from the last placed point, in world units, or null when none is in force. double?, read-only, always non-negative (a typed minus sign is dropped by taking the absolute value). Survives Enter so the following click can consume it; cleared by ResetInputMode, which OnLeftClick calls after placing a point." },
                { "DrawingTool.OverrideAngle", "The committed direction in DEGREES counter-clockwise from +X, or null when none is in force. double?, read-only. Unlike OverrideDistance a negative value is kept, so -90 is straight down." },
                { "DrawingTool.StatusMessage", "A one-line prompt for the current state, e.g. \"Line: Click end point (Shift: ortho)\" or \"Circle (3 Points): Click second point\". \"Ready\" when Mode is None. Get-only, recomputed on every read — the status bar shows it." },
                { "DrawingTool.InputChanged", "Raised whenever InputMode or InputBuffer changes, so a host can repaint the little distance/angle readout. EventHandler, no payload — read GetInputDisplayText() in the handler." },
                { "DrawingTool.ShapeCompleted", "Raised with the finished Shape once enough points have been collected (or a double-click ended a multi-point shape, or CompleteText supplied a string). EventHandler<Shape>. The shape has already been constructed, and therefore already auto-registered on the canvas, by the time this fires — the event is your chance to name it, style it or write it into the user's source, not to place it." },
                { "DrawingTool.ModeChanged", "Raised with the new DrawingMode whenever SetMode changes it, and by Cancel() when it actually had something to cancel. EventHandler<DrawingMode>. Used to update the Draw menu's check marks and the status bar." },
                { "DrawingTool.TextPlacementRequested", "Raised by Text mode after its single click, carrying the VXYZ where the text should go. EventHandler<VXYZ>. Nothing is created yet: the host is expected to ask the user for a string and call CompleteText(location, content), which builds the VText and raises ShapeCompleted. This is the only mode that cannot finish on its own." },
                { "DrawingTool.SetMode", "SetMode(DrawingMode mode) — arms the tool for a shape type. Clears Points, CurrentPoint, CurrentSnap and SnapEngine.ReferencePoint, then raises ModeChanged (unconditionally, unlike Cancel). It does NOT reset the keyboard input mode; Cancel() does." },
                { "DrawingTool.Cancel", "Cancel() — what Esc does. Drops the shape in progress AND leaves drawing mode entirely (Mode becomes None), clearing the points, the snap, SnapEngine.ReferencePoint and the input mode with its overrides. ModeChanged is raised only if something was actually being drawn. Compare OnRightClick(), which discards the points but stays in the mode." },
                { "DrawingTool.CycleInputMode", "CycleInputMode() — what Tab does: None to Distance to Angle to None. Returns false, changing nothing, when Mode is None or no point has been placed yet, so a host can let Tab do its normal job. On entering a mode it pre-fills InputBuffer with the current distance or angle and sets IsBufferSelected, so typing replaces it." },
                { "DrawingTool.StartDistanceInput", "StartDistanceInput() — jumps straight into Distance mode without the Tab cycle, which is what typing a digit while drawing does. Pre-fills InputBuffer with the current distance from the last point to the cursor or snap, with IsBufferSelected set so the digit you just typed replaces it. Does nothing when Mode is None or no point has been placed." },
                { "DrawingTool.HandleCharInput", "HandleCharInput(char c) — feeds one typed character into the buffer. Accepts digits, one '.', and '-' only at the start; returns FALSE for anything else and for any character at all when InputMode is None, which is how the host knows to handle the key itself. A true return means the value was applied to OverrideDistance/OverrideAngle immediately, so the preview updates as you type." },
                { "DrawingTool.HandleBackspace", "HandleBackspace() — deletes the last character, or clears the whole buffer when IsBufferSelected. Returns false when InputMode is None, or when the buffer is already empty, so Backspace falls through to the host." },
                { "DrawingTool.HandleEnterInput", "HandleEnterInput() — commits the typed value into OverrideDistance or OverrideAngle and leaves input mode. Returns false when InputMode is None. Note the override SURVIVES this call: it is consumed by the next OnLeftClick, which is what makes 'type 120, Enter, click the direction' work." },
                { "DrawingTool.HandleEscapeInput", "HandleEscapeInput() — abandons keyboard entry: clears the buffer AND the overrides, leaving the shape in progress untouched. Returns false when InputMode is None, which is what lets the host's Esc go on to Cancel() the whole shape instead." },
                { "DrawingTool.ResetInputMode", "ResetInputMode() — back to DrawingInputMode.None with an empty buffer and both overrides cleared. Called automatically by Cancel() and by OnLeftClick after a point is placed, so overrides apply to one click and no more. Raises no event." },
                { "DrawingTool.GetEffectiveEndPoint", "GetEffectiveEndPoint() — the position the next click would actually place, resolving the typed overrides, the live snap and the raw cursor in that order of precedence. VXYZ?, null when there is nothing to go on. With an override in force the point is built from the last placed point (or the Extension snap's source) by distance and angle; otherwise it is CurrentSnap.Point, or failing that CurrentPoint. This is what the preview shape is drawn from." },
                { "DrawingTool.GetInputDisplayText", "GetInputDisplayText() — the little readout for the current input mode, e.g. \"Distance: 120_\" or \"Angle: 45_°\", with '_' standing in for the caret. Returns \"\" when InputMode is None." },
                { "DrawingTool.OnMouseMove", "OnMouseMove(VXYZ worldPos, IReadOnlyList<IDrawable> shapes, double scale, SceneIndex? spatialIndex = null) — updates CurrentPoint and CurrentSnap. Order matters and is fixed: the orthogonal constraint is applied first, then snapping runs against the constrained position. Pass the spatial index on a large drawing and the snap search is culled by it; leave it null and every shape in the list is considered. Does nothing when Mode is None." },
                { "DrawingTool.OnLeftClick", "OnLeftClick(VXYZ worldPos) — places a point. The position used is the effective one: a typed override wins, then CurrentSnap.Point, then the ortho-constrained cursor. Appends to Points, sets SnapEngine.ReferencePoint on the first click (which is what enables Perpendicular and Tangent snaps for the second), clears the input overrides, and — once the mode's click count is reached — constructs the real shape, raises ShapeCompleted and clears Points while STAYING in the mode so you can draw another. Returns false only when Mode is None. Text mode is the exception: it raises TextPlacementRequested instead of building anything." },
                { "DrawingTool.OnDoubleClick", "OnDoubleClick(VXYZ worldPos) — finishes the multi-point shapes, and only those: Polygon (3+ points), Polyline (2+) and Spline (2+). Returns false in any other mode, or when there are too few points, so the host can fall through to its own double-click behaviour. The worldPos argument is not added as a point." },
                { "DrawingTool.OnRightClick", "OnRightClick() — two-stage cancel. With points collected it discards them and STAYS in the drawing mode, ready to start another shape; with none it leaves the mode entirely by calling Cancel(). Returns false when Mode is None." },
                { "DrawingTool.GetPreviewShape", "GetPreviewShape() — the grey rubber-band shape for the state as it stands, built fresh on each call from Points plus GetEffectiveEndPoint(). Returns null when Mode is None or no point has been placed. The returned shape is for drawing an overlay; it is not the shape ShapeCompleted will hand you." },
                { "DrawingTool.RefreshSnapSettings", "RefreshSnapSettings() — re-reads the eight snap toggles from the application's Snap Settings into the tool's SnapEngine. Call it after the user changes them, since the engine holds its own copy taken when the tool was constructed." },
                { "DrawingTool.CompleteText", "CompleteText(VXYZ location, string content) — the second half of Text mode: builds the VText at the location TextPlacementRequested handed you and raises ShapeCompleted. Does nothing if Mode is no longer Text or content is null or empty, so cancelling the host's text prompt is safe." },

                // GifEncoder
                { "GifEncoder.AddFrame", "AddFrame(BitmapSource frame) — appends one frame to the animation. Every frame must be the width and height passed to the constructor. Frames are written to the stream as they arrive, so a long animation does not accumulate in memory." },
                { "GifEncoder.Dispose", "Writes the GIF trailer and releases the stream. The file is NOT a valid GIF until this runs, so construct the encoder in a using statement — there is no Save() method; Dispose is what finalises the file. Frame delay and looping are constructor arguments (frameDelayMs, repeat), not properties." },

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
                { "VColor.WithOpacity", "WithOpacity(int r, int g, int b, double opacity) — an #AARRGGBB string from RGB 0-255 plus an opacity of 0.0 (invisible) to 1.0 (opaque); values outside that range are clamped rather than rejected. WithOpacity(255, 0, 0, 0.5) is \"#7FFF0000\". Use it for a translucent FillColor over other geometry. Distinct from Shape.Opacity, which scales the whole shape including its stroke; this one only affects the colour you assign it to." },
                { "VColor.GetVibrantColors", "The 25 saturated colour NAMES that GetRandomVibrantColor draws from — Red through Chartreuse — as a string[]. A fresh copy each call, so you can shuffle or trim it without affecting later calls. The obvious use is a chart palette: new ChartOptions { Palette = VColor.GetVibrantColors() }. Note it includes Aqua and Chartreuse, which are valid colour strings but are NOT in the ColorName enum or VColor's properties — a reminder that the palette is wider than the enum." },
                { "VColor.GetPastelColors", "The 25 soft colour NAMES that GetRandomPastelColor draws from — LightBlue, PaleGreen, Thistle, MistyRose, Cornsilk and so on — as a string[]. A fresh copy each call. Better suited to FillColor than to strokes, since several are close to white on a light background. Like the vibrant list it contains names outside the ColorName enum (MistyRose, PeachPuff, LemonChiffon, Honeydew, AliceBlue, LavenderBlush, Cornsilk, Beige, AntiqueWhite, PapayaWhip, BlanchedAlmond)." },

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
                { "PolygonWithHoles.ToString", "Returns \"PolygonWithHoles(Outer: N pts, Holes: M)\" — the outer boundary's vertex count and the number of holes. The quickest way to check what a *WithHoles boolean operation actually produced." },

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
                { "RegionBooleanOps.Union", "Merges regions into one. Returns a single Region, or NULL when they do not all connect — a union of genuinely disjoint regions has no single-region answer. Overloads: Union(a, b, segmentsPerCurve = 32), Union(params Region[]), and Union(IEnumerable<Region> regions, int segmentsPerCurve = 32), which folds across the whole collection. segmentsPerCurve is the sampling density used to approximate curved boundaries before clipping — raise it for large or tightly-curved regions, lower it for speed. NOTE the params overload CANNOT take it (C# forbids an optional parameter after params), so pass a List when you want to control precision: RegionBooleanOps.Union(new List<Region> { a, b, c }, 128)." },
                { "RegionBooleanOps.Intersect", "The overlapping area of regions, as a List<Region> — a list rather than a single region because an intersection can legitimately be several disjoint pieces. Overloads: Intersect(a, b, segmentsPerCurve = 32), Intersect(IEnumerable<Region>, segmentsPerCurve = 32) folding to the area common to ALL of them, and Intersect(params Region[]), which cannot take the precision argument. Returns an empty list when nothing overlaps." },
                { "RegionBooleanOps.Difference", "The first region minus the others, as a List<Region> — again a list, because subtracting can split a region into pieces. Overloads: Difference(a, b, segmentsPerCurve = 32), Difference(IEnumerable<Region>, segmentsPerCurve = 32) which subtracts every subsequent region from the first, and Difference(params Region[]) without the precision argument. An empty list means the subtraction removed everything." },
                { "RegionBooleanOps.Xor", "The symmetric difference — everything belonging to one region but not both — as a List<Region>. Overloads: Xor(a, b, segmentsPerCurve = 32), Xor(IEnumerable<Region>, segmentsPerCurve = 32) folding a running symmetric difference across the collection, and Xor(params Region[]) without the precision argument." },
                { "RegionBooleanOps.DefaultSegmentsPerCurve", "Constant, 32 — the default sampling density every region boolean uses when flattening a curved boundary before clipping. Public so it is nameable rather than a magic number: RegionBooleanOps.Union(regions, RegionBooleanOps.DefaultSegmentsPerCurve * 4) reads better than passing 128. Raise it when a region has large or tightly-curved edges and the result looks faceted; lower it when you are folding many regions and want speed. It is not a limit — any positive value works." },
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
                { "HatchType.ToString", "Returns \"HatchType(Name: N lines)\" — the pattern name and how many line families it is built from. A family is one direction of the pattern, so ANSI31 reports 1 and a cross-hatch reports 2." },

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

                // BuiltInHatch values. Each carries the pattern's own description from the AutoCAD
                // .pat library, plus the geometry, because the names alone say nothing.
                { "BuiltInHatch.SOLID", "Solid fill — a single family of 45° lines spaced 0.125 apart. Close enough to read as filled at normal scale, but it IS lines, not a flood fill: zoom in far enough, or raise PatternScale, and you will see them. For a genuinely filled area set the shape's FillColor instead." },
                { "BuiltInHatch.ANGLE", "Angle steel. Horizontal and vertical dashed families 0.275 apart, forming the L-profile look of angle iron in section." },
                { "BuiltInHatch.ANSI31", "ANSI iron, brick, stone masonry — the standard 45° single-hatch every engineer reads as \"cut material\". Plain parallel lines at 45°, 0.125 apart. The default choice when you just want a section hatched." },
                { "BuiltInHatch.ANSI32", "ANSI steel. Pairs of close 45° lines, the pairs 0.375 apart — the double-line convention for steel." },
                { "BuiltInHatch.ANSI33", "ANSI bronze, brass, copper. A solid 45° line alternating with a dashed 45° line, 0.25 apart." },
                { "BuiltInHatch.ANSI34", "ANSI plastic, rubber. Four widely-spaced 45° families 0.75 apart — the sparsest of the ANSI set, so it reads as a light material." },
                { "BuiltInHatch.ANSI35", "ANSI fire brick, refractory material. A solid 45° line alternating with a long-dash-dot 45° line." },
                { "BuiltInHatch.ANSI36", "ANSI marble, slate, glass. A single 45° dash-dot family, offset row to row so the dashes stagger." },
                { "BuiltInHatch.ANSI37", "ANSI lead, zinc, magnesium, and sound/heat/electrical insulation. Full 45°/135° crosshatch, 0.125 apart — the densest of the ANSI set." },
                { "BuiltInHatch.ANSI38", "ANSI aluminium. Solid 45° lines crossed by a widely-spaced dashed 135° family." },
                { "BuiltInHatch.AR_B816", "Architectural: 8×16 block elevation, stretcher bond. Draws concrete blocks as seen in elevation. Sized in inches at building scale, so it needs a large boundary or a large PatternScale before it looks like blockwork rather than a solid smear." },
                { "BuiltInHatch.AR_B816C", "Architectural: 8×16 block elevation, stretcher bond, with mortar joints — AR_B816 with the joint thickness drawn as a double line. Building-scale (inches)." },
                { "BuiltInHatch.AR_B88", "Architectural: 8×8 block elevation, stretcher bond — square blocks rather than the 8×16 of AR_B816. Building-scale (inches)." },
                { "BuiltInHatch.AR_BRELM", "Architectural: standard brick elevation, English bond, with mortar joints — alternating courses of stretchers and headers, the eight-family pattern of the set. Building-scale (inches)." },
                { "BuiltInHatch.AR_BRSTD", "Architectural: standard brick elevation, stretcher bond — the ordinary running-bond brick wall. Building-scale (inches); the most useful of the AR_* family for elevations." },
                { "BuiltInHatch.AR_CONC", "Architectural: random dot and stone pattern — concrete in section. Thirteen line families at irregular angles and long broken dash sequences produce a convincingly random aggregate. The most expensive built-in pattern to generate; keep PatternScale sane on a big boundary." },
                { "BuiltInHatch.AR_HBONE", "Architectural: standard brick herringbone at 45° — interlocking diagonal brickwork, for paving and floors. Building-scale (inches)." },
                { "BuiltInHatch.AR_PARQ1", "Architectural: 2×12 parquet flooring in a 12×12 tile — alternating blocks of parallel boards. Building-scale (inches)." },
                { "BuiltInHatch.AR_RROOF", "Architectural: roof shingle texture — irregular broken horizontal lines, for a shingled roof in elevation. Building-scale (inches)." },
                { "BuiltInHatch.AR_RSHKE", "Architectural: roof wood shake texture — coarser and more irregular than AR_RROOF, for split shakes. Building-scale (inches)." },
                { "BuiltInHatch.AR_SAND", "Architectural: random dot pattern — sand, or a fine-grained fill. The same idea as AR_CONC at a much smaller grain, and much cheaper to generate." },
                { "BuiltInHatch.BOX", "Box steel. Nested square outlines drawn from eight families — the hollow-section profile seen end-on." },
                { "BuiltInHatch.BRASS", "Brass material. A solid horizontal line alternating with a dashed one, 0.25 apart. Horizontal rather than the 45° of the ANSI set, so it stands out against neighbouring sections." },
                { "BuiltInHatch.BRICK", "Brick or masonry-type surface — horizontal courses with staggered vertical joints. The generic brick pattern at unit scale; use AR_BRSTD when you want real brick dimensions." },
                { "BuiltInHatch.BRSTONE", "Brick and stone — brick courses with a banded stone element, the two materials together." },
                { "BuiltInHatch.CLAY", "Clay material. Three tightly-grouped horizontal lines then a dashed one, repeating every 0.1875." },
                { "BuiltInHatch.CORK", "Cork material. Horizontal lines overlaid with short 135° dashes in groups of three." },
                { "BuiltInHatch.CROSS", "A series of crosses — small plus signs on a 0.25 grid. A marker pattern rather than a material." },
                { "BuiltInHatch.DASH", "Dashed lines — a single horizontal dashed family, dash and gap both 0.125. The simplest broken fill." },
                { "BuiltInHatch.DOLMIT", "Geological rock layering (dolomite). Horizontal beds crossed by a sparse 45° dashed family." },
                { "BuiltInHatch.DOTS", "A series of dots — a dot grid, 0.03125 × 0.0625. Fine stipple; the dots come out as zero-length segments, so line weight is what makes them visible." },
                { "BuiltInHatch.EARTH", "Earth or ground (subterranean) — short dashes on a broken 0.25 grid, three horizontal families and three vertical, the standard for cut earth below grade." },
                { "BuiltInHatch.ESCHER", "Escher pattern — an interlocking tessellation built from twenty-one families at 60°/180°/300°. Decorative; by far the most complex of the non-architectural patterns." },
                { "BuiltInHatch.FLEX", "Flexible material — horizontal dashes with a 45° zig, suggesting something that bends." },
                { "BuiltInHatch.GOST_GLASS", "GOST (Russian standard) glass. Groups of short 45° strokes at 6-unit spacing — larger than the ANSI patterns, so it suits drawings in millimetres." },
                { "BuiltInHatch.GOST_WOOD", "GOST (Russian standard) wood. Vertical broken lines at 6-unit spacing, reading as end grain." },
                { "BuiltInHatch.GOST_GROUND", "GOST (Russian standard) ground. Three closely-spaced 45° families at 10-unit pitch." },
                { "BuiltInHatch.GRASS", "Grass area — short strokes at 45°, 90° and 135° forming scattered tufts. A landscape pattern, not a section hatch." },
                { "BuiltInHatch.GRATE", "Grated area — very close horizontal lines (0.03125) crossed by sparser verticals (0.125). Reads as a metal grating or grille." },
                { "BuiltInHatch.GRAVEL", "Gravel pattern — forty-plus families at scattered angles, giving irregular angular stones. Expensive to generate; the coarse cousin of AR_SAND." },
                { "BuiltInHatch.HEX", "Hexagons — a hexagonal tiling from three families at 0°, 60° and 120°. Geometric, no material meaning." },
                { "BuiltInHatch.HONEY", "Honeycomb pattern — hexagons packed more tightly than HEX, with the cells offset." },
                { "BuiltInHatch.HOUND", "Houndstooth check — the woven textile pattern, from two crossed dashed families." },
                { "BuiltInHatch.INSUL", "Insulation material — a solid horizontal line with two dashed lines between, the batt-insulation convention." },
                { "BuiltInHatch.LINE", "Parallel horizontal lines, 0.125 apart. The plainest pattern there is; set PatternAngle to turn it to any direction you like, which makes it the general-purpose \"lines at an angle\" fill." },
                { "BuiltInHatch.MUDST", "Mud and sand — a single horizontal family with a long broken dash sequence." },
                { "BuiltInHatch.NET", "Horizontal/vertical grid — square mesh at 0.125. The standard crosshatch grid; use SQUARE for the same idea drawn as separate small squares." },
                { "BuiltInHatch.NET3", "Network pattern 0-60-120 — a triangular mesh from three families 60° apart, the isometric counterpart of NET." },
                { "BuiltInHatch.PLAST", "Plastic material — three close horizontal lines repeating every 0.25." },
                { "BuiltInHatch.PLASTI", "Plastic material, a denser variant of PLAST with a fourth line in each group. The two are separate library entries and look very similar; pick whichever reads better at your scale." },
                { "BuiltInHatch.SACNCR", "Concrete — fine 45° solid lines at 0.09375 with a family of dots between them. Much cheaper than AR_CONC and the right choice at unit scale rather than building scale." },
                { "BuiltInHatch.SQUARE", "Small aligned squares — a 0.125 grid of separate square outlines rather than continuous lines. Visually similar to NET but with gaps at the corners." },
                { "BuiltInHatch.STARS", "Star of David — overlapping triangles from three families 60° apart. Decorative." },
                { "BuiltInHatch.STEEL", "Steel material. Two 45° families 0.0625 apart, repeating every 0.125 — closer than ANSI31 and coarser than ANSI32." },
                { "BuiltInHatch.SWAMP", "Swampy area — horizontal water lines with vertical tufts, the map convention for marsh. A landscape pattern." },
                { "BuiltInHatch.TRANS", "Heat transfer material — a solid horizontal line alternating with a dashed one at 0.25. Very close to BRASS, differing only in the dash-to-gap ratio (equal here, a longer dash in BRASS)." },
                { "BuiltInHatch.TRIANG", "Equilateral triangles — a triangular tiling from three families at 0°, 60° and 120°, spaced 0.1875." },
                { "BuiltInHatch.ZIGZAG", "Staircase effect — horizontal and vertical dashes offset so they step, producing a zigzag. Geometric." },
                { "BuiltInHatch.ACAD_ISO02W100", "ISO dashed line, as a hatch — one horizontal family of long dashes (12 on, 3 off) at 5-unit row spacing. The ISO family is line-work rather than texture: use it when you want a fill of directional broken lines, and set PatternAngle to aim them." },
                { "BuiltInHatch.ACAD_ISO03W100", "ISO dashed space line — 12 on, 18 off, so much airier than ACAD_ISO02W100." },
                { "BuiltInHatch.ACAD_ISO04W100", "ISO long dashed dotted line — a 24-unit dash followed by a dot." },
                { "BuiltInHatch.ACAD_ISO05W100", "ISO long dashed double-dotted line — a 24-unit dash followed by two dots." },
                { "BuiltInHatch.ACAD_ISO06W100", "ISO long dashed triplicate-dotted line — a 24-unit dash followed by three dots, built from two overlaid families." },
                { "BuiltInHatch.ACAD_ISO07W100", "ISO dotted line — dots only, 0.5 on and 3 off. The lightest of the ISO family." },
                { "BuiltInHatch.ACAD_ISO08W100", "ISO long dashed short dashed line — a 24-unit dash alternating with a 6-unit one." },
                { "BuiltInHatch.ACAD_ISO09W100", "ISO long dashed double-short-dashed line — a 24-unit dash followed by two 6-unit dashes." },
                { "BuiltInHatch.ACAD_ISO10W100", "ISO dashed dotted line — a 12-unit dash followed by a dot. The classic centre-line rhythm." },
                { "BuiltInHatch.ACAD_ISO11W100", "ISO double-dashed dotted line — two 12-unit dashes followed by a dot." },
                { "BuiltInHatch.ACAD_ISO12W100", "ISO dashed double-dotted line — a 12-unit dash followed by two dots." },
                { "BuiltInHatch.ACAD_ISO13W100", "ISO double-dashed double-dotted line — two 12-unit dashes followed by two dots, from two overlaid families." },
                { "BuiltInHatch.ACAD_ISO14W100", "ISO dashed triplicate-dotted line — a 12-unit dash followed by three dots, from two overlaid families." },
                { "BuiltInHatch.ACAD_ISO15W100", "ISO double-dashed triplicate-dotted line — two 12-unit dashes followed by three dots. The busiest of the ISO family." },

                // ColorName values. The name IS the string that Color and FillColor take,
                // so the useful thing to add is the exact colour each one resolves to.
                { "ColorName.Red", "#FF0000 — vivid red." },
                { "ColorName.Green", "#008000 — dark vivid green." },
                { "ColorName.Blue", "#0000FF — vivid blue." },
                { "ColorName.Yellow", "#FFFF00 — vivid yellow." },
                { "ColorName.Orange", "#FFA500 — vivid orange." },
                { "ColorName.Purple", "#800080 — dark vivid magenta." },
                { "ColorName.Pink", "#FFC0CB — very light vivid red." },
                { "ColorName.Cyan", "#00FFFF — vivid cyan." },
                { "ColorName.Magenta", "#FF00FF — vivid magenta." },
                { "ColorName.White", "#FFFFFF — near-white neutral." },
                { "ColorName.Black", "#000000 — near-black neutral." },
                { "ColorName.Gray", "#808080 — mid grey." },
                { "ColorName.Brown", "#A52A2A — dark red." },
                { "ColorName.Coral", "#FF7F50 — light vivid orange." },
                { "ColorName.Crimson", "#DC143C — red." },
                { "ColorName.DarkBlue", "#00008B — dark vivid blue." },
                { "ColorName.DarkGreen", "#006400 — very dark vivid green." },
                { "ColorName.DarkRed", "#8B0000 — dark vivid red." },
                { "ColorName.DarkOrange", "#FF8C00 — vivid orange." },
                { "ColorName.DarkViolet", "#9400D3 — dark vivid violet." },
                { "ColorName.DeepPink", "#FF1493 — vivid pink-red." },
                { "ColorName.DeepSkyBlue", "#00BFFF — vivid azure." },
                { "ColorName.DodgerBlue", "#1E90FF — vivid azure." },
                { "ColorName.ForestGreen", "#228B22 — dark green." },
                { "ColorName.Fuchsia", "#FF00FF — vivid magenta." },
                { "ColorName.Gold", "#FFD700 — vivid yellow." },
                { "ColorName.GreenYellow", "#ADFF2F — vivid yellow-green." },
                { "ColorName.HotPink", "#FF69B4 — light vivid pink-red." },
                { "ColorName.IndianRed", "#CD5C5C — red." },
                { "ColorName.Indigo", "#4B0082 — dark vivid violet." },
                { "ColorName.Khaki", "#F0E68C — light yellow." },
                { "ColorName.Lavender", "#E6E6FA — very light blue." },
                { "ColorName.LawnGreen", "#7CFC00 — vivid yellow-green." },
                { "ColorName.LightBlue", "#ADD8E6 — light cyan." },
                { "ColorName.LightCoral", "#F08080 — light red." },
                { "ColorName.LightGreen", "#90EE90 — light green." },
                { "ColorName.LightPink", "#FFB6C1 — very light vivid red." },
                { "ColorName.LightSalmon", "#FFA07A — light vivid orange." },
                { "ColorName.LightSeaGreen", "#20B2AA — dark cyan." },
                { "ColorName.LightSkyBlue", "#87CEFA — light vivid azure." },
                { "ColorName.LightYellow", "#FFFFE0 — very light vivid yellow." },
                { "ColorName.Lime", "#00FF00 — vivid green." },
                { "ColorName.LimeGreen", "#32CD32 — green." },
                { "ColorName.Maroon", "#800000 — dark vivid red." },
                { "ColorName.MediumBlue", "#0000CD — dark vivid blue." },
                { "ColorName.MediumOrchid", "#BA55D3 — magenta." },
                { "ColorName.MediumPurple", "#9370DB — light violet." },
                { "ColorName.MediumSeaGreen", "#3CB371 — green." },
                { "ColorName.MediumSlateBlue", "#7B68EE — light blue." },
                { "ColorName.MediumSpringGreen", "#00FA9A — vivid green-cyan." },
                { "ColorName.MediumTurquoise", "#48D1CC — cyan." },
                { "ColorName.MediumVioletRed", "#C71585 — pink-red." },
                { "ColorName.MidnightBlue", "#191970 — dark blue." },
                { "ColorName.Navy", "#000080 — dark vivid blue." },
                { "ColorName.Olive", "#808000 — dark vivid yellow." },
                { "ColorName.OliveDrab", "#6B8E23 — dark yellow-green." },
                { "ColorName.OrangeRed", "#FF4500 — vivid orange." },
                { "ColorName.Orchid", "#DA70D6 — light magenta." },
                { "ColorName.PaleGreen", "#98FB98 — light vivid green." },
                { "ColorName.PaleTurquoise", "#AFEEEE — very light cyan." },
                { "ColorName.PaleVioletRed", "#DB7093 — light pink-red." },
                { "ColorName.Peru", "#CD853F — orange." },
                { "ColorName.Plum", "#DDA0DD — light magenta." },
                { "ColorName.RoyalBlue", "#4169E1 — blue." },
                { "ColorName.Salmon", "#FA8072 — light vivid red." },
                { "ColorName.SandyBrown", "#F4A460 — light orange." },
                { "ColorName.SeaGreen", "#2E8B57 — dark green." },
                { "ColorName.Sienna", "#A0522D — dark orange." },
                { "ColorName.Silver", "#C0C0C0 — light grey." },
                { "ColorName.SkyBlue", "#87CEEB — light azure." },
                { "ColorName.SlateBlue", "#6A5ACD — blue." },
                { "ColorName.SlateGray", "#708090 — muted azure." },
                { "ColorName.SpringGreen", "#00FF7F — vivid green." },
                { "ColorName.SteelBlue", "#4682B4 — azure." },
                { "ColorName.Tan", "#D2B48C — light orange." },
                { "ColorName.Teal", "#008080 — dark vivid cyan." },
                { "ColorName.Thistle", "#D8BFD8 — light muted magenta." },
                { "ColorName.Tomato", "#FF6347 — light vivid red." },
                { "ColorName.Turquoise", "#40E0D0 — green-cyan." },
                { "ColorName.Violet", "#EE82EE — light magenta." },
                { "ColorName.Wheat", "#F5DEB3 — very light orange." },
                { "ColorName.YellowGreen", "#9ACD32 — yellow-green." },

                // ControlPointType values
                { "ControlPointType.Move", "The handle that translates the WHOLE shape. Every shape's GetControlPoints() puts this one first, at index 0, positioned at the shape's centre or centroid — so MoveControlPoint(0, target) moves the shape so that its centre lands on target. Not to be confused with Shape.Move, which takes a displacement rather than a destination." },
                { "ControlPointType.Vertex", "A corner or endpoint you can drag independently: a line's Start or End, a polygon or polyline vertex, a rectangle corner, an arc's start and end, a dimension's measured points." },
                { "ControlPointType.Radius", "A handle that resizes rather than moves — a circle's or ellipse's radius, or an arc's. Dragging it changes a size property; the shape stays centred where it was." },
                { "ControlPointType.Rotation", "A handle that rotates the shape about its own centre. Declared for completeness; none of the built-in shapes currently emit one, so you will only see it if you produce your own control points." },
                { "ControlPointType.CurveControl", "An off-curve control handle that bends the curve without lying on it — a bézier's P1 and P2, or a spline's control points. Renderers usually draw these differently from Vertex handles because they are not points the curve passes through (a spline's control points are an exception: Catmull-Rom does pass through them)." },

                // ParamKind values
                { "ParamKind.Number", "The parameter holds a number. Every numeric type collapses to this one kind and is stored as double, so an int parameter and a double parameter are indistinguishable afterwards — read it back with .Num or Get<int>(name) if you need an integer. This is the only kind that gets a slider in the Global Parameters panel, from its Min/Max/Step." },
                { "ParamKind.Boolean", "The parameter holds true or false. Shown as a checkbox in the panel." },
                { "ParamKind.Text", "The parameter holds a string. Shown as a text box." },
                { "ParamKind.Date", "The parameter holds a DateTime. Shown as a date picker. Note that date parameters are deliberately never written back into your source, because the declaring expression is usually something like DateTime.Now and rewriting it would be wrong." },

                // FillRule values
                { "FillRule.EvenOdd", "Count crossings: a point is inside when a ray from it crosses the outline an odd number of times. A loop inside another loop therefore punches a hole whatever direction it winds in, which is why this is the rule the geometry library emits filled areas with — outer boundary first, holes after, no winding bookkeeping required. The zero value, so it is what you get by default." },
                { "FillRule.NonZero", "Count crossing direction: a point is inside when the signed crossings do not cancel. An inner loop only becomes a hole if it winds opposite to the outer one, so hole direction matters. Choose it when you are feeding in loops whose winding you control deliberately." },

                // VFont values
                { "VFont.Arial", "Arial — clean sans-serif, and the default for VText. The safest choice: present on every Windows machine and legible at small sizes." },
                { "VFont.TimesNewRoman", "Times New Roman — classic serif. Good for body text in a titled drawing; less good for small annotation, where the serifs blur." },
                { "VFont.CourierNew", "Courier New — monospace. Every character the same width, so use it for tables and aligned columns of numbers." },
                { "VFont.Verdana", "Verdana — wide sans-serif designed for screen legibility. The most readable choice at small text heights, at the cost of taking more room." },
                { "VFont.Georgia", "Georgia — serif with large, open letterforms; holds up much better than Times New Roman at small sizes." },
                { "VFont.Tahoma", "Tahoma — compact sans-serif. Verdana's narrower relative: similar clarity, less width." },
                { "VFont.TrebuchetMS", "Trebuchet MS — humanist sans-serif with more character than Arial, for titles and labels." },
                { "VFont.Consolas", "Consolas — modern monospace, and much better looking than Courier New. The right monospace for code or aligned data." },
                { "VFont.Calibri", "Calibri — the Office default. Rounded sans-serif that reads as informal." },
                { "VFont.Cambria", "Cambria — serif designed for body text, with sturdy strokes that survive being drawn small." },
                { "VFont.SegoeUI", "Segoe UI — the Windows system font, and what the DoodleSharp interface itself uses. Pick it when annotation should look native to the application." },
                { "VFont.ComicSansMS", "Comic Sans MS — casual script. Use deliberately." },
                { "VFont.Impact", "Impact — very heavy condensed display face. Titles and callouts only; unreadable as body text." },
                { "VFont.LucidaConsole", "Lucida Console — monospace, heavier than Consolas. An alternative when Consolas is not available." },

                // VFontWeight values
                { "VFontWeight.Normal", "Normal weight (400) — the default for VText." },
                { "VFontWeight.Bold", "Bold weight (700). Set text.FontWeight = VFontWeight.Bold. There is no intermediate weight in this enum; for anything finer, a colour or size change usually reads better than a weight change would." },

                // Shape caching seam
                { "Shape.Revision", "A counter that increases every time the shape's geometry is reassigned. It is the cache-invalidation seam: derived data that is expensive to recompute (a hatch's generated segments, a region's sampled outline) is memoised against this value and regenerated only when it changes. You would read it to build your own cache over a shape the same way: store the Revision alongside your result and recompute when they differ. The honest limitation is that it tracks ASSIGNMENT, not mutation — editing a VPolygon's Points list in place changes the geometry without bumping it, which is what Invalidate() is for. Type uint, and it wraps, harmlessly: it is only ever compared for equality against a previously stored copy." },
                { "Shape.Invalidate", "Marks the shape's derived data stale, bumping Revision so every cache built over it is rebuilt on next use. Call it after mutating a shape's geometry THROUGH A COLLECTION rather than through a property — polygon.Points[3] = ..., or hatch.Boundary.Add(...) — because an in-place list edit bypasses the property setters that would normally bump the revision, and without this the hatch or region will keep drawing its previous shape. Assigning the whole collection (polygon.Points = newList) does not need it. Cheap: one increment, no allocation." },

                // VHatch overrides and caching
                { "VHatch.GetCachedLines", "The generated hatch segments, computed once and reused until the hatch changes — the read-only counterpart of GenerateLines(). Returns IReadOnlyList<(VXYZ Start, VXYZ End)> and THE LIST IS SHARED: do not modify it, and do not hold it past a change to the hatch. That trade exists because hatch generation is the most expensive thing a drawing can ask the renderer to do; regenerating a few hundred hatches every frame cost 11.5 ms and 146 MB of allocation per frame before this existed. Use it when you only want to read the segments; use GenerateLines() when you want a list of your own. The cache turns over when Boundary, Pattern, PatternScale or PatternAngle is assigned — editing the boundary list in place bypasses that, so call Invalidate() if you do." },
                { "VHatch.Clone", "Returns an independent copy: the boundary points are cloned, the pattern and its scale and angle are carried over, and the styling is copied. Like every Clone, the copy has no Name, so mark it with Place() or set a Name or the post-run sweep will hide it." },
                { "VHatch.Move", "Translates every boundary point by the displacement vector. The pattern is anchored to the pattern origin rather than to the boundary, so moving a hatch slides the boundary across the pattern and the lines inside it land in different places — visible if you move a hatch a fraction of its spacing." },
                { "VHatch.Rotate", "Rotates the boundary about the pivot AND adds the same angle to PatternAngle, so the hatching turns with the shape instead of staying fixed in world space. Angle in degrees, counter-clockwise." },
                { "VHatch.Scale", "Scales the boundary about the centre AND multiplies PatternScale by |factor|, so the pattern grows with the shape rather than getting denser. Use the absolute value deliberately: a negative factor mirrors the boundary without inverting the pattern spacing." },
                { "VHatch.Flip", "Mirrors every boundary point across the given line. PatternAngle is NOT mirrored, so the hatch lines keep their original direction while the boundary reverses — if you want the hatching mirrored too, negate PatternAngle yourself." },
                { "VHatch.GetBounds", "The bounding box of the boundary points. An empty boundary returns a degenerate box at the origin. This measures the boundary, not the generated lines, which is the same thing since the lines are clipped to it." },
                { "VHatch.GetControlPoints", "A single Move handle at the centre of the boundary's bounding box. Editing individual boundary vertices interactively is not supported; rebuild the hatch from a new boundary instead." },
                { "VHatch.ToString", "Returns \"VHatch(pattern, Scale:..., Angle:...)\" — the pattern name plus the two properties that most often need checking when a fill comes out too dense or turned the wrong way." },

                // Region caching
                { "Region.GetCachedOutline", "Gives you the region's boundary as plain vertices — out List<VXYZ> outer and out List<List<VXYZ>> holes — sampling each curved edge into segmentsPerCurve pieces, and memoising the result against Shape.Revision and the segment count. THE RETURNED LISTS ARE SHARED and must not be modified; copy them if you need to. This exists because sampling a region is not cheap — every non-line edge goes through ICurve.Divide, and a bézier or spline internally walks itself a few hundred times to get arc-length parameterisation — so doing it per frame per region was one of the most expensive things a drawing could contain. Prefer it over Region.SampleLoop when you are reading the same region repeatedly; use SampleLoop when you want a list you own, or want to sample one loop only." },

                // Frame (requestAnimationFrame-style loop)
                { "Frame.Pump", "Runs every queued callback once and returns true if any ran. THE HOST CALLS THIS, once per frame — you do not. It is documented because its two guarantees shape how you write a callback. First, it swaps queues before running anything, so a callback that calls Frame.Request during the pump is scheduled for the NEXT frame rather than re-entered on this one; that is what makes the self-rescheduling idiom terminate instead of hanging the UI thread. Second, a callback that throws stops the whole loop: the queue is cleared, CallbackFailed is raised, and nothing further runs — because user code runs in-process and an exception reaching WPF's dispatcher sixty times a second would take the application down. The elapsedSeconds it passes each callback is measured from when the loop started, and its clock is independent of timeline playback, so a per-frame callback has no notion of being paused." },
                { "Frame.CallbackFailed", "Raised with the exception when a per-frame callback throws. The queue has already been cleared by the time it fires, so the loop has stopped. DoodleSharp subscribes to this and reports the exception in the console panel — which is why a broken Frame.Request callback shows up as one console message rather than as a crash. Subscribe yourself if you want to recover: nothing stops you re-arming with Frame.Request from the handler, though you will usually want to fix the callback instead." },


                // RayHit / RayQuery
                { "RayHit.Deconstruct", "Lets a hit be destructured in one line, because RayHit is a record struct: if (caster.FindIntersection(origin, dir) is RayHit h) { var (shape, point, distance) = h; ... }. Equivalent to reading Shape, Point and Distance separately. Note FindIntersection returns RayHit? — pattern-match or check HasValue before destructuring." },
                { "RayHit.Equals", "Value equality generated for the record struct: two hits are equal when their Shape, Point and Distance all match. Shape is compared by REFERENCE (shapes have no value equality), Point fuzzily via VXYZ's 1e-9 tolerance, and Distance as an exact double — so comparing whole hits is rarely what you want. Compare hit.Shape, or compare distances with a tolerance of your own." },
                { "RayHit.GetHashCode", "Hash generated for the record struct, combining Shape (reference hash), Point (VXYZ rounds to 8 decimals) and Distance. Usable as a dictionary key, subject to the exact-double caveat on Equals." },
                { "RayHit.ToString", "The record struct's generated form: \"RayHit { Shape = ..., Point = (x, y, z), Distance = ... }\", where the Shape part is whatever that shape's own ToString returns." },
                { "RayQuery.Deconstruct", "Lets a query be destructured as var (origin, direction) = query, because RayQuery is a record struct. Mostly useful when reading back the list you passed to RayCaster.FindIntersections, whose results come back in the same order as the queries." },
                { "RayQuery.Equals", "Value equality generated for the record struct: two queries are equal when their Origin and Direction are. VXYZ compares fuzzily (IsAlmostEqualTo, 1e-9), so coordinates that differ only in floating-point noise still match. Note the direction is compared as given — it is not normalised first, so (1, 0, 0) and (2, 0, 0) describe the same ray but are NOT equal." },
                { "RayQuery.GetHashCode", "Hash generated for the record struct, combining Origin and Direction. VXYZ rounds to 8 decimals when hashing so that its fuzzy equality still groups correctly in a Dictionary or HashSet." },
                { "RayQuery.ToString", "The record struct's generated form: \"RayQuery { Origin = (x, y, z), Direction = (x, y, z) }\"." },

                // IDrawable
                { "IDrawable.Place", "Puts the drawable on the canvas and keeps it there. Declared as a DEFAULT interface implementation, forwarding to Draw(), specifically so that the recommended name works through an IDrawable reference — CanvasRenderer.GetShapes() hands back IDrawable, and without this \"prefer Place()\" would fail to compile in exactly the place the documentation sends you. Shape implements it outright, so every real shape reaches the same method. Exactly equivalent to Draw()." },

                // ConsoleOutput
                { "ConsoleOutput.Instance", "The one ConsoleOutput for the process — a lazily-created singleton, so there is no constructor to call and no way to get a second one. Prefer VizConsole.Log for ordinary output; reach for this when you need to read the console back, clear it, or write an entry with a clickable source location." },
                { "ConsoleOutput.WriteLine", "WriteLine(string moduleName, int lineNumber, string message) — adds a normal output line prefixed with [moduleName:lineNumber]. You have to supply the module and line yourself, which is precisely what VizConsole.Log captures automatically via CallerFilePath/CallerLineNumber, so Log is almost always the better call." },
                { "ConsoleOutput.WriteError", "WriteError(string moduleName, int lineNumber, string message) — the same as WriteLine but flagged as an error, so the panel renders it in the error colour. Use it for your own validation failures; runtime exceptions are already reported by the host." },
                { "ConsoleOutput.WriteCompilationError", "WriteCompilationError(string filePath, int lineNumber, int column, string message) — an error carrying a full source location, which is what makes the console line clickable and able to jump the editor to that exact position. Used by the compiler; useful to you if you are building your own analysis pass over the project." },
                { "ConsoleOutput.AddEntry", "AddEntry(string message, string? filePath = null, int lineNumber = 0, int column = 0, bool isError = false) — the general-purpose form. With a filePath and a lineNumber greater than zero the entry becomes clickable and navigates the editor; with neither it is a plain line. ModuleName is derived from the file name for you. This is what the Find References output uses." },
                { "ConsoleOutput.Clear", "Removes every entry and refreshes the panel immediately (no throttling on this one). This is what the console's Clear button does. It does not affect what your code has already written elsewhere — there is only one buffer." },
                { "ConsoleOutput.Flush", "Forces any throttled update to reach the panel now. Console writes are batched so that a tight loop of Log calls does not repaint per line; the consequence is that the last few lines can lag by a fraction of a second. The host calls this when your code finishes, so you only need it if you are reading the panel state mid-run." },
                { "ConsoleOutput.GetEntries", "Returns a snapshot of every ConsoleEntry currently in the console, as an IReadOnlyList. It is a copy taken under the lock, so it is safe to enumerate while other threads keep writing. Use it to assert on your own output, or to save a run's log." },
                { "ConsoleOutput.GetFormattedOutput", "Returns the whole console as one string, each entry rendered as \"[module:line] message\" and followed by a newline where the entry is a line rather than a fragment. The straightforward way to copy a run's output into a file or a clipboard." },
                { "ConsoleOutput.OutputChanged", "Raised when the console contents change, so a view can refresh. Fires immediately for Clear and on the throttle interval for writes, NOT once per Log — so do not use it to count messages. The sender is the ConsoleOutput instance and the args carry nothing; re-read GetEntries in the handler." },

                // ConsoleEntry
                { "ConsoleEntry.Message", "The text of the line. Defaults to an empty string rather than null, so it is always safe to read." },
                { "ConsoleEntry.ModuleName", "The source file name the entry came from — a bare file name, not a full path — used as the [module:line] prefix in the panel. Empty when the entry has no origin." },
                { "ConsoleEntry.LineNumber", "The 1-based source line the entry came from. Zero means \"no line\", which is also what stops the entry being clickable." },
                { "ConsoleEntry.Column", "The 1-based column within the line, for entries precise enough to have one (compiler diagnostics). Zero when unknown; the editor still navigates on LineNumber alone." },
                { "ConsoleEntry.FilePath", "The full path of the source file, or null. This is what the editor needs to open the right file — ModuleName is only for display. Nullable on purpose: most entries do not have one." },
                { "ConsoleEntry.IsError", "True when the entry should be rendered as an error. Set by WriteError and by WriteCompilationError; also settable through AddEntry's isError argument." },
                { "ConsoleEntry.IsNewLine", "Whether the entry terminates its line. True for everything the current API produces; the flag exists so a future write-without-newline can append to the previous entry, and GetFormattedOutput honours it when joining entries into a string." },
                { "ConsoleEntry.IsClickable", "Computed, not settable: true when there is a non-empty FilePath AND a LineNumber greater than zero. That is the exact condition under which clicking the console line navigates the editor to the source, so it is the property to check rather than testing FilePath yourself." },

                // C2VGeometry.Rendering — sink protocol. Plumbing: nothing here is needed to draw.
                { "IPrimitiveSink.Hints", "The TessellationHints this sink wants honoured — how finely to flatten curves (Scale, world units per device pixel) and whether to offer native forms first (PreferNative). The tessellator reads it before every shape, so changing Scale between frames is how a zoom changes flattening fineness." },
                { "IPrimitiveSink.BeginShape", "BeginShape(Shape shape, in PenSpec pen) — called before a shape's primitives, with everything needed to style them. RETURN FALSE TO DECLINE THE SHAPE: the tessellator skips it entirely and Tessellate returns false, which is how a sink says \"I cannot handle this type, use another renderer\". Returning true commits you to receiving the Emit* calls until EndShape." },
                { "IPrimitiveSink.EndShape", "Called after the last primitive of a shape, and only if BeginShape returned true. Balance any state you pushed in BeginShape here." },
                { "IPrimitiveSink.EmitPolyline", "EmitPolyline(IReadOnlyList<VXYZ> points, bool closed) — a stroked run of connected points in WORLD coordinates. closed means join the last point back to the first; it does not mean fill. THE LIST IS THE TESSELLATOR'S SCRATCH BUFFER and is reused for the next shape, so copy it if you need to keep it past the call." },
                { "IPrimitiveSink.EmitFilledLoops", "EmitFilledLoops(IReadOnlyList<IReadOnlyList<VXYZ>> loops, FillRule rule) — a filled area in world coordinates. The FIRST loop is the outer boundary and every other loop is a hole. As with EmitPolyline, the lists are scratch buffers: copy before keeping." },
                { "IPrimitiveSink.EmitPoint", "EmitPoint(VXYZ point) — a zero-area mark, from a VPoint or from a degenerate shape. How big it draws is the sink's decision; the geometry carries no size." },
                { "IPrimitiveSink.EmitText", "EmitText(VText text) — text handed over UNFLATTENED, because a sink with a real text stack should use it: glyph outlines lose hinting and cost far more. The VText carries its own Location, Height, Font, FontWeight, Anchor and Angle. If you genuinely need outlines, VText.ToCharShape / LiftChar go through VText.GlyphOutlineProvider. Note the object may be a per-call temporary (dimension labels are), so do not assume it will still be valid later." },
                { "IPrimitiveSink.TryEmitNative", "TryEmitNative(Shape shape, in PenSpec pen) — offered before flattening, but ONLY when Hints.PreferNative is set. Return true to claim the shape and suppress tessellation entirely; return false and it gets flattened as normal. This is the hook that lets one tessellator serve both a rasterizer, which wants everything as segments, and a DXF writer, which wants a circle to stay a CIRCLE entity. A default interface method returning false, so an existing sink needs no change." },

                { "BoundsPrimitiveSink.MinX", "Left edge of the accumulated bounding box, in world coordinates. Meaningless until HasBounds is true." },
                { "BoundsPrimitiveSink.MinY", "Bottom edge of the accumulated bounding box, in world coordinates (Y is up). Meaningless until HasBounds is true." },
                { "BoundsPrimitiveSink.MaxX", "Right edge of the accumulated bounding box, in world coordinates. Meaningless until HasBounds is true." },
                { "BoundsPrimitiveSink.MaxY", "Top edge of the accumulated bounding box, in world coordinates (Y is up). Meaningless until HasBounds is true." },
                { "BoundsPrimitiveSink.HasBounds", "False until something has actually been measured — the accumulators start at MaxValue/MinValue, so reading Min/Max before anything arrives gives nonsense. Always check this first." },
                { "BoundsPrimitiveSink.Reset", "Clears the accumulated box so the instance can measure a fresh set. The instance is meant to be reused; allocating one per measurement is the thing this class exists to avoid." },
                { "BoundsPrimitiveSink.IncludeBounds", "IncludeBounds(Shape shape) — folds a shape the tessellator DECLINED into the extents, using the shape's own GetBounds(). The pattern is: if (tessellator.Tessellate(shape, sink) == false) sink.IncludeBounds(shape). Without it a shape type the tessellator does not handle is simply left out of the extents and can sit off screen after a zoom-to-fit." },
                { "BoundsPrimitiveSink.Hints", "The tessellation hints used while measuring. Flattening fineness barely affects a bounding box, so the defaults are almost always right here." },
                { "BoundsPrimitiveSink.BeginShape", "Accepts every shape (always returns true) and records its animation offsets so the measured box reflects where the shape is actually drawn, not where its untranslated geometry sits." },
                { "BoundsPrimitiveSink.EndShape", "No-op — nothing is buffered per shape." },
                { "BoundsPrimitiveSink.EmitPolyline", "Folds every point of the run into the box." },
                { "BoundsPrimitiveSink.EmitFilledLoops", "Folds every point of every loop into the box. Holes are included, which is harmless: a hole is inside the outer boundary by definition." },
                { "BoundsPrimitiveSink.EmitPoint", "Folds the single point into the box. A point contributes no area, so a drawing of nothing but VPoints still gets a box that spans them." },
                { "BoundsPrimitiveSink.EmitText", "Folds the text's box into the extents, from its Location, Height, Anchor and measured width — an approximation, since the exact ink extent depends on the font, but consistently a little generous rather than short." },

                { "PolylineFallbackSink.OnPolyline", "Action<IReadOnlyList<VXYZ>, bool, PenSpec> called for each stroked run: the points in world coordinates, whether the run closes, and the pen. Null by default, and a null callback means those primitives are silently dropped — set the ones you care about. The point list is a reused scratch buffer; copy it if you keep it." },
                { "PolylineFallbackSink.OnFilled", "Action<IReadOnlyList<IReadOnlyList<VXYZ>>, PenSpec> called for each filled area, first loop outer and the rest holes. Null by default. Note it does not pass the FillRule; this sink is for consumers that treat outer-plus-holes literally." },
                { "PolylineFallbackSink.OnPoint", "Action<VXYZ, PenSpec> called for each zero-area mark. Null by default." },
                { "PolylineFallbackSink.OnText", "Action<VText> called for text, which has no polyline form here — it is handed over intact for you to render with a real text stack. Null by default, so text is dropped unless you set it, which is a common way to accidentally produce an export with no labels." },
                { "PolylineFallbackSink.Unhandled", "The shapes even the tessellator could not reduce, accumulated across the run. AN EMPTY LIST MEANS THE PASS WAS COMPLETE — checking it is what turns a silently incomplete export into a reportable one, which is exactly the bug this sink was written to close. Call Reset() before reusing the instance." },
                { "PolylineFallbackSink.Reset", "Clears the Unhandled list so the instance can be reused for another pass. It does not clear the callbacks." },
                { "PolylineFallbackSink.Hints", "The tessellation hints. Leave PreferNative false here — the whole point of this sink is that it wants everything flattened." },
                { "PolylineFallbackSink.BeginShape", "Accepts every shape (always returns true) and captures its PenSpec for the callbacks that follow." },
                { "PolylineFallbackSink.EndShape", "No-op — nothing is buffered per shape." },
                { "PolylineFallbackSink.EmitPolyline", "Forwards to OnPolyline, if set." },
                { "PolylineFallbackSink.EmitFilledLoops", "Forwards to OnFilled, if set. The FillRule is not passed on." },
                { "PolylineFallbackSink.EmitPoint", "Forwards to OnPoint, if set." },
                { "PolylineFallbackSink.EmitText", "Forwards to OnText, if set." },

                { "ShapeTessellator.Tessellate", "Tessellate(Shape shape, IPrimitiveSink sink) — decomposes the shape into primitives and pushes them into the sink. THE RETURN VALUE IS NOT OPTIONAL: false means the sink declined the shape (its BeginShape returned false) and you must do something else with it — draw it yourself, record it as unhandled, or fold its bounds in. Ignoring the result is precisely how dimensions, arrows and construction lines vanish from an export. Groups recurse into their children. Not thread-safe: the instance holds reusable buffers, so give each thread its own." },
                { "ShapeTessellator.SegmentsForRadius", "Static. SegmentsForRadius(double radiusPixels) — how many straight segments a circular arc of that on-screen radius should be flattened into. Radius is in PIXELS, not world units, which is the whole point: a circle of radius 1 needs a different segment count depending on how far you have zoomed in. Exposed so a caller can match the renderer's fineness exactly rather than guessing at it." },

                { "TessellationHints.Scale", "SCREEN PIXELS PER WORLD UNIT — the view's zoom, the same quantity as MouseInfo.Scale and SvgTile.Scale. Multiply a world size by this to get its size on screen, which is exactly what the tessellator does (radiusPixels = radius * Scale) before choosing a segment count. Default 1.0, which means a 1:1 view. Setting it too low wastes segments on a shape only a few pixels across; too high and a large circle visibly becomes a polygon. NOTE the property's own XML comment in the library says \"world units per device pixel\", which is the reciprocal and is wrong — the code multiplies." },
                { "TessellationHints.PreferNative", "Set it when your sink can express a shape directly — a DXF CIRCLE entity, an SVG <circle>, a PDF arc — and the tessellator will offer each shape to IPrimitiveSink.TryEmitNative before flattening it, only flattening what you decline. Default false, which is right for a rasterizer: it wants everything as segments." },

                { "PenSpec.From", "Static. PenSpec.From(Shape shape) — snapshots the shape's five styling members plus Opacity into a PenSpec. The way to build one; the six-argument constructor exists for synthesising a pen that does not come from a shape." },
                { "PenSpec.HasFill", "Whether there is a GENUINE fill, rather than a value that merely looks like one. False for a null or empty FillColor and for the literal strings \"Transparent\" and \"None\" (case-insensitively). Check this before filling anything — testing FillColor for null yourself is how a \"Transparent\" fill ends up painted as an actual colour." },
                { "PenSpec.Color", "The stroke colour, as the same colour-name or hex string the shape carried. Readonly field, not a property." },
                { "PenSpec.FillColor", "The fill colour string as the shape carried it — which may be \"Transparent\" or \"None\", so test HasFill rather than this. Readonly field." },
                { "PenSpec.LineWeight", "The shape's stroke width. Whether that is device pixels or world units depends on the host's Display Line Weight setting, so a sink should decide once rather than per shape. Readonly field. Worth knowing: DoodleSharp's own raster and GPU sinks do not read it at all — they draw one-pixel hairlines — which is why Auto stays on the WPF vector backend while Display Line Weight is on." },
                { "PenSpec.LineType", "The shape's LineType (Continuous, Dashed, Dotted, ...). The sink turns it into an actual dash array — but it should not invent one: C2VGeometry.Rendering.LineTypePatterns.DevicePixels(lineType) is the single shared definition, and a sink with its own table is how the same dashed line came to look different on different backends. Readonly field." },
                { "PenSpec.LineTypeScale", "Multiplier on the dash pattern's lengths. Readonly field. Pass it through LineTypePatterns.IsSolid(lineType, scale) before building a pattern and LineTypePatterns.ClampScale(scale) before applying it, so a degenerate value renders as a solid line rather than as nothing." },
                { "PenSpec.Opacity", "The shape's opacity, 0 (invisible) to 1 (opaque). Readonly field. Combine it with the alpha already present in a #AARRGGBB colour string rather than treating them as alternatives." },

                { "LineTypePatterns.DevicePixels", "Static. DevicePixels(LineType lineType) — the alternating dash/gap run lengths for a line type (dash, gap, dash, gap ...), in DEVICE PIXELS at a LineTypeScale of 1, or an EMPTY span for Continuous. The values: Dashed 8,4 — Dotted 2,4 — DashDot 8,4,2,4 — DashDotDot 8,4,2,4,2,4 — Center 12,4,4,4 — Phantom 12,4,4,4,4,4 — Hidden 4,4. Returned as a ReadOnlySpan<double> over a SHARED static array because this is called per shape per frame and must not allocate: read it, never write to it, and copy into your own buffer to scale or unit-convert. A consumer whose dash lengths are multiples of something else divides that something out first — WPF multiplies the pattern by the pen thickness, so the canvas divides by thickness before handing it over, which is why dash length is now independent of LineWeight." },
                { "LineTypePatterns.IsSolid", "Static. IsSolid(LineType lineType, double scale) — the check to make BEFORE building a dash pattern. True when the type is Continuous (no pattern at all) and also when the scale is zero, negative or non-finite: those would collapse every run to zero length, which rasterises as nothing at all rather than as a line, so a degenerate scale is deliberately treated as solid rather than as invisible." },
                { "LineTypePatterns.ClampScale", "Static. ClampScale(double scale) — folds a caller-supplied LineTypeScale into the supported range. Returns 1.0 for a non-finite or non-positive value, otherwise clamps into [MinScale, MaxScale]. It is a guard, not a validator: it never throws, so it is safe to apply to whatever a user's shape happens to carry." },
                { "LineTypePatterns.MinScale", "Static const, 0.01. The lower clamp on a line type scale — below it the pattern is treated as solid rather than as a run of sub-pixel dashes that would not be visible as anything." },
                { "LineTypePatterns.MaxScale", "Static const, 1000.0. The upper clamp on a line type scale, so one shape with a runaway value cannot produce a dash longer than any plausible drawing." },

                { "GlyphOutlineProvider.GetCharContours", "The provider's one method: given a VText and a character index, it returns that character's outline as a list of closed contours in WORLD coordinates, already positioned, anchored and rotated to match how the text is drawn — or null for whitespace or an out-of-range index. A glyph with a hole (o, A, 8) comes back as more than one contour. You would not normally call this; VText.ToCharShape(i), VText.LiftChar(i), the indexer text[i] and VText.LiftChars(start, count) wrap it and hand back real shapes instead of raw point lists." },

                // Control-point handles, per shape. Index 0 is always the whole-shape Move handle,
                // and MoveControlPoint takes a DESTINATION, not a displacement.
                { "VLine.GetControlPoints", "Three handles: [0] Move at the midpoint, [1] Vertex at Start, [2] Vertex at End." },
                { "VLine.MoveControlPoint", "Index 0 translates the whole line so its midpoint lands on newPosition; 1 sets Start; 2 sets End. Any other index is ignored." },
                { "VCircle.GetControlPoints", "Two handles: [0] Move at Center, [1] Radius on the circle at Center + (Radius, 0)." },
                { "VCircle.MoveControlPoint", "Index 0 moves the circle so its centre lands on newPosition; 1 sets Radius to the distance from Center to newPosition, so dragging it resizes about the centre. Any other index is ignored." },
                { "VArc.GetControlPoints", "Four handles: [0] Move at Center, [1] Radius, [2] Vertex at StartPoint, [3] Vertex at EndPoint." },
                { "VArc.MoveControlPoint", "Index 0 recentres the arc on newPosition; 1 sets Radius to the distance from Center. Indices 2 and 3 set StartAngle and EndAngle from the direction of newPosition relative to Center AND ALSO set Radius to its distance — so dragging an end handle both sweeps and resizes the arc, which is what makes the handle follow the cursor exactly. Set StartAngle or EndAngle directly if you want to sweep without resizing." },
                { "VRectangle.GetControlPoints", "Three handles: [0] Move at the centre, [1] Vertex at Corner, [2] Vertex at the opposite corner. There are no edge-midpoint handles — resize by dragging a corner." },
                { "VRectangle.MoveControlPoint", "Index 0 recentres the rectangle on newPosition. Index 1 drags the Corner while HOLDING THE OPPOSITE CORNER FIXED, and index 2 drags the opposite corner while holding Corner fixed — so both resize rather than translate, and Width and Height both change. Dragging a handle past the far corner flips the rectangle rather than producing a negative size. It stays axis-aligned throughout; use RotationAngle to turn it." },
                { "VPolygon.GetControlPoints", "One Move handle at the vertex centroid, at index 0, followed by one Vertex handle per point in Points order — so handle N corresponds to Points[N-1]. An empty polygon returns an empty list, with no Move handle." },
                { "VPolygon.MoveControlPoint", "Index 0 translates the polygon so its centroid lands on newPosition. Index 1..Points.Count sets Points[index-1] and rebuilds the internal edge curves. Out-of-range indices are ignored. Note the centroid used is the plain average of the vertices, not the area centroid." },
                { "VPolyline.GetControlPoints", "One Move handle at the vertex average, at index 0, then one Vertex handle per point in Points order — handle N is Points[N-1]. An empty polyline returns an empty list." },
                { "VPolyline.MoveControlPoint", "Index 0 translates the polyline so its vertex average lands on newPosition; index 1..Points.Count sets Points[index-1]. Out-of-range indices are ignored." },
                { "VBezier.GetControlPoints", "Five handles: [0] Move at the curve centre, [1] Vertex at P0, [2] CurveControl at P1, [3] CurveControl at P2, [4] Vertex at P3. The two CurveControl handles are the off-curve tangent handles — the curve does not pass through them." },
                { "VBezier.MoveControlPoint", "Index 0 translates the whole curve; 1 to 4 set P0, P1, P2 and P3 respectively. Moving P1 or P2 changes the curvature without moving either endpoint." },
                { "VSpline.GetControlPoints", "One Move handle at the average of the control points, at index 0, then one CurveControl handle per entry of ControlPoints — handle N is ControlPoints[N-1]. They are typed CurveControl even though a Catmull-Rom spline passes THROUGH its control points, so dragging one moves the curve to it." },
                { "VSpline.MoveControlPoint", "Index 0 translates the whole spline; index 1..ControlPoints.Count sets ControlPoints[index-1]. Because the tangent at each point depends on its neighbours, moving one point visibly reshapes the two spans on either side, not just the adjacent one." },
                { "VEllipse.GetControlPoints", "Three handles: [0] Move at Center, [1] Radius at the end of the RadiusX axis, [2] Radius at the end of the RadiusY axis. The two radius handles go through PointAtAngle, so they sit on the curve however the ellipse is turned. Nothing for StartAngle/EndAngle or Rotation — set those directly." },
                { "VEllipse.MoveControlPoint", "Index 0 recentres the ellipse on newPosition; 1 sets RadiusX and 2 sets RadiusY, each to the DISTANCE from Center to newPosition. Measuring the distance rather than a world-axis displacement is what makes dragging a handle on a turned ellipse resize the axis that handle belongs to, and it cannot produce a negative radius. Rotation itself is not draggable — assign it, or call Rotate(pivot, degrees)." },
                { "VText.GetControlPoints", "A single Move handle at Location, at index 0. Height, Anchor and Angle are not draggable." },
                { "VText.MoveControlPoint", "Index 0 sets Location to newPosition. Remember Location's meaning depends on Anchor, so the text moves relative to whichever corner or edge the anchor names." },
                { "VArrow.GetControlPoints", "Three handles: [0] Move at MidPoint, [1] Vertex at Start, [2] Vertex at End (the tip)." },
                { "VArrow.MoveControlPoint", "Index 0 translates the arrow so its midpoint lands on newPosition; 1 sets Start; 2 sets End. The head is rebuilt from the new shaft direction automatically." },
                { "VDimension.GetControlPoints", "Three handles: [0] Move at the centre, [1] Vertex at Point1, [2] Vertex at Point2. Offset is not a handle — set it directly to push the dimension line further out." },
                { "VDimension.MoveControlPoint", "Index 0 translates the whole dimension; 1 and 2 set Point1 and Point2, which re-measures Distance and rebuilds DisplayText." },
                { "VRadialDimension.GetControlPoints", "Two handles: [0] Move at Center, [1] Vertex at the leader's outer end. Dragging the second one is how LeaderAngle is edited interactively." },
                { "VRadialDimension.MoveControlPoint", "Index 0 recentres the dimension on newPosition; 1 sets LeaderAngle from the direction of newPosition relative to Center, swinging the leader around without changing Radius." },
                { "VRadialDimension.GetBounds", "The bounding box covering the dimensioned circle's extent and the leader and text — the bounding-box answer, since a dimension has no outline of its own. Contains and DistanceTo are likewise box-based on this shape." },
                { "VRadialDimension.Flip", "Mirrors Center across the given line and mirrors the leader direction with it, so the annotation stays on the same side of the geometry it belongs to. Radius is unchanged — a mirror does not resize." },
                { "VRadialDimension.ToString", "Returns \"VRadialDimension(Center: ..., R: ..., text)\", where the last part is the DisplayText actually drawn — so it already reflects ShowDiameter, Prefix, Suffix, DecimalPlaces and any CustomText." },
                { "VGroup.GetControlPoints", "Five handles: [0] Move at the group centre, then four Vertex handles at the corners of the group's bounding box (Min, Max, top-left, bottom-right). Only the Move handle does anything today; the corner handles are placeholders for a future box scale, so dragging one has no effect." },
                { "VGroup.MoveControlPoint", "Index 0 translates the whole group so its centre lands on newPosition, which moves every child. The corner indices are accepted and ignored." },
                { "VGroup.Move", "Translates every child by the displacement vector; the group holds no geometry of its own. Distinct from setting OffsetX/OffsetY, which the renderer applies as a transform around the whole group without touching the children — that is what animations use, and it is why an animated group snaps back if you clear the offsets." },
                { "VGroup.Rotate", "Rotates every child about the SAME pivot, so the group turns as one rigid body rather than each child spinning in place. Angle in degrees, counter-clockwise. Pass GetCenter() as the pivot to turn a group about itself." },
                { "VGroup.Scale", "Scales every child about the same centre point, so spacing between children scales too. Pass GetCenter() to grow a group in place." },
                { "VGroup.Flip", "Mirrors every child across the given line. Because each child mirrors independently about the same line, the group's internal arrangement mirrors correctly as a whole." },
                { "VGroup.Clone", "A deep copy: every child is cloned, the Name is carried across (unusually — most Clone implementations drop it) and the group's own styling is copied. The clones are new shapes, so the copy is fully independent of the original." },
                { "VGroup.GetBounds", "The union of every child's bounding box. An EMPTY GROUP RETURNS A DEGENERATE BOX AT THE ORIGIN rather than null, so check Count if the difference matters — an empty group would otherwise drag a zoom-to-fit back to (0, 0)." },
                { "VGroup.Contains", "True when ANY child contains the point — each child using its own exact test, so a point inside a circle in the group counts and a point merely inside the group's bounding box does not. Returns false for an empty group. Not to be confused with ContainsShape, which asks about membership." },
                { "VGroup.DistanceTo", "The smallest DistanceTo across every child, so it measures to whichever child is nearest — zero when the point sits on any child's outline. An EMPTY GROUP RETURNS double.MaxValue." },
                { "VGroup.DoesIntersect", "True when any child intersects the other shape. When the other shape is itself a VGroup, every pair of children is tested, so it is O(n × m) — fine for tens of shapes, worth avoiding in a loop over thousands." },
                { "VGroup.Intersect", "Returns the FIRST intersection found between any child and the other shape, as a Shape, or null if none intersects. \"First\" is in child order, which is creation order — so it is a hit test, not a complete answer. Iterate Shapes yourself if you need every intersection. Each child answers through Shape.Intersect, so a curve child against a curve argument now returns real crossing points (a VPoint for one, a VGroup for several) instead of null; nothing it builds is registered, so Place() the result to see it." },
                { "VGroup.ToString", "Returns \"VGroup(N shapes)\", with the group's Name appended in quotes when one is set — for example VGroup(4 shapes, \"wheel\"). N is the direct child count, so a nested group counts as one child rather than as its contents." },
                { "VCell.Clone", "An independent copy of the cell keeping the same UniqueId, Column and Row as the original — deliberately, since those identify its position in the grid, so a cloned cell is a copy of a specific cell rather than a new one. Neighbours are NOT cloned: the copy is detached from the grid, so pathfinding across a clone will not work." },
                { "VCell.Move", "Translates the cell's four corners and its Center together. Moving a single cell out of its grid leaves the grid's own Location and cell layout unchanged, so the grid will no longer agree with where the cell is — move the VSpatialGrid instead if you want the whole grid to travel." },
                { "VCell.Rotate", "Rotates the cell's corners and its Center about the pivot, in degrees counter-clockwise. The cell stops being axis-aligned, which is fine for drawing but means GetCellAt/GetClosestCell lookups on the parent grid become unreliable." },
                { "VCell.Scale", "Scales the corners and Center about the given centre point and multiplies CellSize by |factor|, so the cell's recorded size stays consistent with its drawn size." },
                { "VCell.Flip", "Mirrors the cell's corners and its Center across the given line." },
                { "VCell.ToString", "Returns \"VCell(Id=..., Col=..., Row=..., Center=...)\" — the cell's UniqueId, its column and row within the owning VSpatialGrid, and its centre point. Useful when logging a path returned by VSpatialGrid.FindPath, which is a list of cells." },
                { "VSpatialGrid.Clone", "A new grid with the same Location, XCount, YCount and CellSize, and the styling copied — the cells are REBUILT rather than copied, so per-cell state does not come across: Blocked flags are cleared and any styling you applied to individual cells is lost. Set them again after cloning." },
                { "VSpatialGrid.Move", "Translates the grid Location and every cell together, and drops the internal nearest-cell index so subsequent GetClosestCell lookups are correct. Prefer this over moving cells individually." },
                { "VSpatialGrid.Rotate", "Rotates the Location and every cell about the pivot, in degrees counter-clockwise. The cells stop being axis-aligned, so GetCellAt — which relies on an axis-aligned layout — becomes unreliable; GetClosestCell still works, since it measures distance." },
                { "VSpatialGrid.Scale", "Scales the Location and every cell about the given centre and multiplies CellSize by |factor|, keeping the grid's recorded cell size consistent with its geometry. The nearest-cell index is dropped." },
                { "VSpatialGrid.Flip", "Mirrors the Location and every cell across the given line. Row and column numbering is unchanged, so after a mirror the cell at column 0 is on the opposite side." },
                { "VSpatialGrid.GetBounds", "The union of every cell's bounding box — the drawn extent of the grid, which is slightly larger than XCount × CellSize by the outer cell walls. A grid with no cells returns a degenerate box at Location." },
                { "VSpatialGrid.DistanceTo", "The smallest distance from the point to any cell's outline, so it is zero on any cell edge and positive inside a cell as well as outside the grid. A grid with no cells falls back to the distance to Location. There is deliberately no Contains override: a grid is a diagram, not an area." },
                { "VSpatialGrid.ToString", "Returns \"VSpatialGrid(XCountxYCount, CellSize=..., Location=...)\" — the grid's dimensions, cell size and the centre of its bottom-left cell. Handy in a VizConsole.Log while working out whether a grid covers the area you meant." },

                // Curve members whose behaviour is specific to the shape
                { "VEllipse.Divide", "Returns numberOfSegments + 1 points spaced by EQUAL ARC LENGTH along the ellipse. This matters on an eccentric ellipse: equal SWEEP ANGLE would bunch points near the flat ends, and everything sampling the curve — dashes, animation paths, morph targets — would inherit the distortion. Use EvaluateByAngle if you specifically want equal angles. Zero or fewer segments returns an empty list." },
                { "VEllipse.Measure", "Returns points spaced segmentLength apart by arc length, walking from StartAngle. The count comes from the numerically-computed total length divided by segmentLength, so the last point can fall short of the end by up to one interval. A segmentLength of zero or less returns an empty list." },
                { "VEllipse.StartPoint", "The point at parameter 0 — the ellipse's position at StartAngle, in world coordinates, so it honours Rotation. On a full ellipse (0 to 360) with Rotation 0 this is Center + (RadiusX, 0), and StartPoint and EndPoint coincide; with a Rotation it is that same point turned about Center. PointAtAngle(StartAngle) is the explicit form." },
                { "VEllipse.EndPoint", "The point at parameter 1 — the ellipse's position at EndAngle. Equal to StartPoint on a full ellipse." },
                { "VEllipse.Vertices", "Just the Center, as a one-element list — an ellipse has no natural vertices. The ICurve contract asks for the shape's defining points, and for an ellipse that is its centre plus its two radii, which are separate properties. Use Divide or Measure to get points along the curve." },
                { "VEllipse.Project", "The closest point ON the ellipse to the given point, found by sampling the curve at 100 positions and taking the nearest — so it is accurate to about a hundredth of the perimeter, not exact. Good enough for snapping and for SplitAtPoint; raise your own sampling if you need better." },
                { "VEllipse.NormalAtPoint", "The outward unit normal derived from the ellipse's implicit equation — the gradient (dx/RadiusX², dy/RadiusY²) normalised, with dx/dy measured from Center. It HONOURS Rotation: the point is taken into the ellipse's own frame, where the gradient is that simple, and the answer is brought back out, so a turned ellipse gives a turned normal. It is evaluated wherever you point it, so pass a point on or near the curve; a point at the centre gives a zero vector." },
                { "VEllipse.Offset", "Returns a NEW VEllipse with both radii grown by distance (negative shrinks), keeping the centre, the angle range and Rotation. That is a concentric ellipse, which is NOT the true offset curve of an ellipse — the exact offset is not an ellipse at all — so the gap between the two varies around the perimeter. Fine for a visual halo; not for a tolerance band." },
                { "VEllipse.PointAtSegmentLength", "The point at the given arc-length distance from the start, found by walking a sampled polyline and interpolating within the segment where the distance runs out. Approximate for the same reason Project is." },
                { "VEllipse.PointsAtChordLengthFromPoint", "The points on the ellipse exactly chordLength away in a straight line from the projection of your reference point onto the curve — found by sampling 100 positions and keeping the crossings, so expect one on each side and an empty list if the chord never reaches. Use Measure for arc-length spacing instead." },
                { "VEllipse.SplitAtPoint", "Splits at the projection of the given point, returning two VEllipse arcs — (StartAngle to split) and (split to EndAngle) — as ICurve, in sweep order and both carrying this ellipse's Rotation. The split angle is measured as an OFFSET along the sweep (GeometryHelper.SweepOffset), so it honours the direction of travel and lands between this ellipse's own two angles however they are written; normalising it into [0, 360) instead, as this used to, made splitting a sweep of 350 to 370 — or any clockwise sweep — produce two pieces that between them covered far more than the original. Both pieces are new shapes and register on the canvas, and the original is NOT removed, so call Remove() on it if you only want the pieces." },
                { "VRay.Divide", "Divides the ray's DRAWN portion, not the ray itself — a ray has no far end, so the parameter range [0, 1] is mapped onto [Origin, Origin + Direction × RenderExtent] (RenderExtent defaults to 10,000). You get numberOfSegments + 1 points. Raise RenderExtent first if you need to sample further out; zero or fewer segments returns an empty list." },
                { "VRay.Measure", "Points every segmentLength along the ray starting at Origin, stopping at RenderExtent — so the count is RenderExtent / segmentLength, which with the default extent of 10,000 can be a very large list. Set RenderExtent to the range you actually care about before calling. A segmentLength at or below 1e-9 returns an empty list." },
                { "VRay.NormalAtPoint", "The unit normal perpendicular to Direction, rotated 90° clockwise from it — (Direction.Y, -Direction.X) normalised. Constant along the ray, so the point you pass is ignored." },
                { "VRay.Offset", "Returns a NEW VRay parallel to this one, displaced by distance along the normal (positive one side, negative the other), carrying the same Direction and RenderExtent. A parallel ray, not a trimmed one." },
                { "VRay.PointAtSegmentLength", "The point at the given distance from Origin along Direction. A NEGATIVE distance returns Origin rather than extrapolating backwards, because a ray does not exist behind its origin. Unlike Divide and Measure, this is not limited by RenderExtent — the ray really is infinite, only its drawing is bounded." },
                { "VRay.PointsAtChordLengthFromPoint", "The points chordLength either side of the projection of your reference point onto the ray. Returns TWO points normally, but only ONE when the backwards point would fall behind Origin, since that is off the ray — so check the count rather than assuming two." },
                { "VXLine.Divide", "Divides the line's DRAWN portion: an infinite line has no ends, so [0, 1] is mapped symmetrically onto [-RenderExtent, +RenderExtent] measured from BasePoint (RenderExtent defaults to 10,000). Parameter 0.5 is therefore BasePoint itself. You get numberOfSegments + 1 points; zero or fewer returns an empty list." },
                { "VXLine.Measure", "Points every segmentLength in BOTH directions from BasePoint out to RenderExtent, returned sorted along the direction — so roughly 2 × RenderExtent / segmentLength points, which with the default extent is a lot. Reduce RenderExtent first. A segmentLength at or below 1e-9 returns an empty list." },
                { "VXLine.NormalAtPoint", "The unit normal perpendicular to Direction — (Direction.Y, -Direction.X) normalised. Constant along the line, so the point argument is ignored." },
                { "VXLine.Offset", "Returns a NEW VXLine parallel to this one, displaced by distance along the normal, with the same Direction and RenderExtent. This is the natural way to build a family of parallel construction lines." },
                { "VXLine.PointAtSegmentLength", "The point at the signed distance from BasePoint along Direction. NEGATIVE VALUES ARE VALID here, unlike on VRay — the line extends both ways — and the result is not clamped to RenderExtent, which only bounds what is drawn." },
                { "VXLine.PointsAtChordLengthFromPoint", "The two points chordLength either side of the projection of your reference point onto the line. Always returns exactly two, since there is line in both directions." },
                { "VCircle.Vertices", "Just the Center, as a one-element list — a circle has no vertices; its other defining value, Radius, is a separate property. Use Divide or Measure for points on the circumference." },
                { "VCircle.PointAtSegmentLength", "The point at the given arc-length distance measured counter-clockwise from angle 0 (Center + (Radius, 0)). Exact, not sampled: the angle is simply distance / Radius. Distances beyond the circumference wrap around." },
                { "VCircle.PointsAtChordLengthFromPoint", "The points on the circle exactly chordLength away in a straight line from the projection of your reference point onto the circle — two symmetric points when the chord fits within the diameter, and an empty list when chordLength exceeds 2 × Radius. Handy for stepping around a circle by true chord rather than by arc." },
                { "VCircle.SplitAtPoint", "Splits the circle at the projection of the given point, returning TWO VArcs as ICurve — 0-to-angle and angle-to-360 — because a circle cut once is no longer a circle. Both arcs are new shapes and register on the canvas, and the circle itself is NOT removed, so call Remove() on it unless you want all three." },
                { "VArc.Vertices", "Three points: Center, StartPoint and EndPoint — the arc's defining geometry apart from Radius. Not points along the curve; use Divide or Measure for those." },
                { "VArc.PointAtSegmentLength", "The point at the given arc-length distance from StartPoint, measured along the sweep. Exact rather than sampled — the angle is simply distance / Radius — and it follows the sweep direction, so it works on an arc whose EndAngle is less than its StartAngle. A distance beyond the arc's own length CLAMPS to EndPoint rather than continuing round the circle." },
                { "VArc.PointsAtChordLengthFromPoint", "The points on the arc exactly chordLength away in a straight line from the projection of your reference point onto it. Points that would land outside the arc's sweep are excluded, so you can get two, one or none — check the count. The right call for setting out equal chords along a curved alignment." },
                { "VArc.SplitAtPoint", "Splits at the projection of the given point, returning two VArcs as ICurve — StartAngle-to-split and split-to-EndAngle, in sweep order. The split angle is expressed as an OFFSET along the sweep (GeometryHelper.SweepOffset) rather than as the raw Atan2 value, so it honours the direction of travel and lands between this arc's own two angles however they are written. Atan2 answers in (-180, 180], which need not lie between them: splitting an arc written as 350 to 370 at (r, 0) used to give [350, 0] and [0, 370] — two arcs together 36 times longer than the one they replaced. Both pieces register on the canvas and the original arc stays, so Remove() it if you only want the pieces." },
                { "VPolyline.Vertices", "The Points list ITSELF, not a copy — mutating what you get back mutates the polyline, and does so without bumping Shape.Revision, so call Invalidate() afterwards. Use Points directly for clarity." },
                { "VPolygon.Vertices", "The Points list ITSELF, not a copy — mutating it mutates the polygon, and it will NOT rebuild the internal edge curves or bump Shape.Revision, so call Invalidate() (and prefer assigning Points, or AddPoint) if you go that route." },
                { "VBezier.Vertices", "The four defining points as a new list: P0, P1, P2, P3 — endpoints and both off-curve control handles, in order. A copy, so editing it does nothing to the curve; assign P0..P3 instead." },
                { "VSpline.Vertices", "The ControlPoints list ITSELF, not a copy. Because a Catmull-Rom spline passes through its control points, these are points on the curve — unlike a bézier's. Mutating the list mutates the spline without bumping Shape.Revision; call Invalidate() if you do." },
                { "VPolyline.NormalAtPoint", "The normal of whichever SEGMENT the given point is nearest — (dy, -dx) of that segment, so 90° clockwise from the direction of travel. It is per-segment, so the value jumps discontinuously as the point crosses a vertex; there is no averaged vertex normal. The result is a unit vector. A polyline with fewer than two points returns (0, 1, 0), straight up, as a safe default rather than a zero vector." },
                { "VPolyline.PointAtSegmentLength", "The point at the given arc-length distance from the first vertex, walking the segments in order. A distance beyond the total length returns the last point rather than extrapolating." },
                { "VPolyline.PointsAtChordLengthFromPoint", "The points on the polyline exactly chordLength away in a straight line from the projection of your reference point. A straight chord can cross a zig-zag more than twice, so this can return more than two points — take them all rather than assuming a pair." },
                { "VPolyline.SplitAtPoint", "Splits at the segment nearest the given point, returning two VPolylines as ICurve: the vertices up to the split plus the split point, and the split point plus the vertices after it. Both register on the canvas; the original is not removed." },
                { "VBezier.Measure", "Points spaced segmentLength apart by ARC LENGTH along the curve, which is not the same as equal steps in t — a cubic bézier moves at very different speeds along its length. It walks a 200-step polyline approximation and interpolates within it, so spacing is accurate to well under a percent of the curve for any reasonable step. A segmentLength at or below 1e-9 returns an empty list; the first point is always the curve start." },
                { "VBezier.NormalAtPoint", "The unit normal at the nearest point on the curve: the derivative there, rotated to (-ty, tx) — 90° counter-clockwise, so it points to the left of the direction of travel. Continuous along the curve, unlike a polyline's." },
                { "VBezier.Offset", "Returns a NEW VBezier offset by the given distance. Be aware how coarse this is: it uses only the END tangents — P0 and P1 are displaced along the normal at the start, P2 and P3 along the normal at the end — so it is exact only where the curve is nearly straight or nearly circular. The true offset of a cubic bézier is not a cubic bézier at all, and this approximation drifts noticeably on an S-curve or where curvature is tight relative to the offset distance. Sample the curve and offset the points yourself if you need accuracy." },
                { "VBezier.Project", "The closest point on the curve to the given point, by finding the closest parameter and evaluating there. This is what SplitAtPoint, DistanceTo and PointsAtChordLengthFromPoint build on." },
                { "VBezier.PointAtSegmentLength", "The point at the given arc-length distance from P0, found by walking a 100-step polyline approximation. Clamps: a distance of zero or less returns P0, and anything at or beyond the total length returns P3." },
                { "VBezier.PointsAtChordLengthFromPoint", "The points on the curve exactly chordLength in a straight line from the projection of your reference point. Found by scanning 100 sampled positions for crossings, so a chord much smaller than the sample spacing can be missed; normally one point on each side." },
                { "VBezier.SplitAtPoint", "Splits at the nearest point on the curve using De Casteljau's algorithm twice, so both halves are EXACT cubic béziers rather than approximations — the returned pair reproduces the original curve precisely. Both are new VBezier shapes and register on the canvas; the original is not removed." },
                { "VSpline.Measure", "Points spaced segmentLength apart by ARC LENGTH along the spline, walking a fixed 32-steps-per-span approximation (independent of SegmentsPerSpan, which only affects rendering). A segmentLength at or below 1e-9 returns an empty list; the first point is always the spline start." },
                { "VSpline.NormalAtPoint", "The unit normal at the nearest point on the spline: the derivative there rotated to (-ty, tx) — 90° counter-clockwise, so it points to the left of the direction of travel." },
                { "VSpline.Offset", "Returns a new spline through offset CONTROL POINTS: each control point is displaced along the normal estimated from its neighbours. That keeps the shape recognisable, but it is not the true offset curve — the offset spline passes through the displaced control points rather than staying a constant distance from the original, so the gap varies where curvature changes. Tight curvature relative to the offset distance will pinch or self-intersect." },
                { "VSpline.Project", "The closest point on the spline to the given point, by finding the closest parameter and evaluating there. Underpins DistanceTo and SplitAtPoint." },
                { "VSpline.PointAtSegmentLength", "The point at the given arc-length distance from the first control point, walking the rendered point list. Clamps: zero or less returns StartPoint, at or beyond the total length returns EndPoint." },
                { "VSpline.PointsAtChordLengthFromPoint", "The points on the spline approximately chordLength in a straight line from the projection of your reference point. Read \"approximately\" literally: it scans the rendered polyline for segments that cross the chord circle and returns each crossing SEGMENT'S MIDPOINT rather than the true intersection, so accuracy is bounded by the sampling density — raise SegmentsPerSpan if you need better. A wandering spline can be crossed more than twice by the same circle, so do not assume a pair." },
                { "VSpline.SplitAtPoint", "Splits at the nearest point, returning two VSplines as ICurve: the control points up to the split span plus the split point, and the split point plus the remaining control points. Because Catmull-Rom tangents depend on neighbouring points, THE TWO HALVES DO NOT EXACTLY REPRODUCE THE ORIGINAL near the cut — the end tangents change. Both halves register on the canvas; the original is not removed." },
                { "VRectangle.GetLength", "The perimeter — 2 × (Width + Height). VRectangle derives from VPolygon, so this is the closed polygon's total edge length, not a diagonal." },
                { "VRectangle.Offset", "Inherited from VPolygon: each vertex is displaced along the average of its two adjacent edge normals, giving a parallel rectangle. Two things to know. There is NO MITER COMPENSATION, so on a general polygon a sharp corner ends up closer than the requested distance; on a rectangle every corner is 90° so the shortfall is a uniform factor of √2 rather than the exact distance you asked for. And WHICH WAY IT GROWS depends on the winding of Points — the normal is taken 90° counter-clockwise from each edge — so try both signs rather than assuming positive means outward. For a robust offset with proper mitring use BooleanOps.OffsetPolygon, which goes through Clipper2. A rectangle collapsed by an inward offset larger than half its smaller dimension comes back inverted rather than empty, so check the result." },
                { "VRectangle.Intersect", "Two overloads, and which you get depends on the argument. With another CURVE (VLine, VCircle, VArc, VEllipse, VPolyline, VPolygon, another VRectangle, VRay, VXLine) you get VPolygon's Intersect(ICurve): an IntersectionResult whose Points are where the OUTLINES cross, exact for straight-edged partners and sampled for curved ones. With a non-curve shape you get the inherited Shape.Intersect(Shape), which returns a Shape or null — rectangle/rectangle and line/rectangle are answered in closed form by GeometryHelper. Either way this is an OUTLINE test, so a shape wholly inside the rectangle does not intersect it: use Contains for containment, and BooleanOps.Intersect(a, b) for the overlapping area." },

                // Viewports. The canvas pane is a recursive grid; every leaf owns a canvas.
                { "Viewport.MaxDimension", "Const int, 8. The most rows or columns ONE viewport may be split into — so a single grid tops out at 8 x 8, and nesting is how you go finer. Not a round number picked at random: every leaf owns a canvas, and a canvas carries a spatial index, its own render layers and a share of the process-wide pen cache, so the cap is what stops a typo (or a Rows assigned from a computed value) asking for thousands of them. Rows or Columns outside 1..MaxDimension throws ArgumentOutOfRangeException." },
                { "Viewport.Root", "Static. The whole drawing surface, undivided until code divides it — the same object the bare name Viewports refers to. Reset() REPLACES it with a new instance, so a Viewport variable captured before a reset is stale (its IsAttached goes false); read Viewports again rather than caching the root across runs." },
                { "Viewport.LayoutChanged", "Static event, Action. Raised whenever the layout actually changes: a Rows, Columns, Height or Width assignment that changed something, or Reset(). Re-stating a value it already has raises nothing, which is what stops the grid being rebuilt on every F5. It MAY ARRIVE ON A THREAD-POOL THREAD, because Main() does not run on the UI thread — a host that rebuilds visuals has to marshal, and should coalesce, since a script that sets Rows and then Columns raises this twice for one intended layout. This is a host hook; user code rarely subscribes." },
                { "Viewport.Reset", "Static. Puts the layout back to a single undivided viewport, detaching every cell (their IsAttached goes false) and raising LayoutChanged. The host calls it as part of the between-runs reset, alongside rewinding shape ids — so the source always says how the canvas is divided, and deleting a Viewports.Rows = 3 line takes effect on the next run rather than lingering until restart. Note it also installs a NEW root object, so a Viewport captured before the call no longer refers to anything on screen. Canvas.Clear() does NOT do this: clearing the shapes leaves the layout alone." },
                { "Viewport.Leaves", "Static. Every leaf of the whole tree, depth-first and left to right — the order the cells appear on screen. One element on the default 1x1 layout, and that element is the root itself. Returns a snapshot, so it is safe to iterate while placing shapes. Use it to walk the cells without knowing the shape of the tree: foreach (var leaf in Viewport.Leaves()) VizConsole.Log(leaf.Path);" },
                { "Viewport.Rows", "How many rows this viewport is split into. 1, the default, means it is a leaf and draws. Setting it above 1 subdivides: the children become the leaves and this viewport stops drawing anything itself. Must be between 1 and MaxDimension (8) or it throws ArgumentOutOfRangeException naming the value it got. ASSIGNING THE VALUE IT ALREADY HAS IS A NO-OP that raises nothing — which matters because Main() re-runs on every F5 and re-states the layout, and treating that as a change would rebuild the grid and throw away every cell's pan and zoom. A resize REUSES the cell already at each position, so subdividing a cell and then widening its parent keeps the cell's own subdivision; cells that fall off the end are detached." },
                { "Viewport.Columns", "How many columns this viewport is split into. 1, the default, means it is a leaf and draws. Same rules as Rows in every respect: 1..MaxDimension (8) or ArgumentOutOfRangeException, re-stating the current value changes and raises nothing, and a resize reuses the cells that survive rather than rebuilding them." },
                { "Viewport.IsLeaf", "True when this viewport is undivided — Rows and Columns are both 1 — and therefore owns a canvas of its own that shapes can be placed on. False once it has been subdivided, at which point its children draw and it does not. Shapes already placed on a viewport that is later subdivided are not lost: they draw in its first cell (see ResolveVisible)." },
                { "Viewport.Parent", "The viewport this one subdivides, or NULL for the root. Height and Width are stored on the parent, which is why reading either on the root throws — there is nothing for it to be sized within." },
                { "Viewport.Depth", "0 for the root, 1 for its cells, 2 for a cell's cells, and so on. Useful for reporting, and for a guard against subdividing without limit." },
                { "Viewport.RowIndex", "Which row of its PARENT this viewport occupies, 0-based; -1 for the root, which sits in no grid. This is the index Height addresses." },
                { "Viewport.ColumnIndex", "Which column of its PARENT this viewport occupies, 0-based; -1 for the root. This is the index Width addresses." },
                { "Viewport.Height", "How tall this viewport's ROW is, in XAML's grid-length spelling: \"*\" for a share of the space, \"3*\" for three shares, or a number for a fixed height in device pixels. Defaults to \"*\", so a fresh grid shares the room equally. IT ADDRESSES THE ROW, NOT THE CELL — every viewport in the same row reports and sets the same value, exactly as a XAML RowDefinition is shared by the cells sitting in it, so Viewports[0][0].Height = \"3*\" and Viewports[0].Height = \"3*\" are the same act and the second says it more directly. Reading OR setting it on the root throws InvalidOperationException: the root always fills the pane. An unparseable value throws ArgumentException and changes nothing; \"Auto\" is rejected by name. Reads back in canonical form, so \"1*\" comes back as \"*\". A size survives a resize that keeps its row, and new rows start at an equal share." },
                { "Viewport.Width", "How wide this viewport's COLUMN is, in the same spelling as Height and with the same rules — \"*\", \"3*\" or a pixel count, addressing the whole column rather than the single cell, throwing InvalidOperationException on the root and ArgumentException on a value it cannot parse. Viewports[0][2].Width = \"4*\" gives the last column four shares while the others keep one each." },
                { "Viewport.RowHeightAt", "RowHeightAt(int row) — the PARSED height of one of THIS viewport's rows, as a ViewportLength. Height is the string spelling user code sets; this is what a host reads to lay the row out, and what you use if you want the number rather than the text: Viewports.RowHeightAt(0).Value. Note the difference in subject — Height is the row this viewport SITS IN, RowHeightAt is a row this viewport CONTAINS. A row index outside 0..Rows-1 throws ArgumentOutOfRangeException with the same message the indexer gives, naming the size the layout actually has." },
                { "Viewport.ColumnWidthAt", "ColumnWidthAt(int column) — the PARSED width of one of THIS viewport's columns, as a ViewportLength. The counterpart to RowHeightAt, with the same opposition of subject against Width, and the same ArgumentOutOfRangeException for an index outside 0..Columns-1." },
                { "Viewport.Path", "How this viewport is written in code — \"Viewports\" for the root, \"Viewports[0][1]\" for a cell, \"Viewports[0][1][2][0]\" for a cell of a cell. Stable for a given position in a given layout, which is what lets the host match a rebuilt tree against the canvases it already holds and keep each cell's pan and zoom across a re-run. Shapes are keyed on node IDENTITY, not on this; the path is for matching and display. ToString() returns it." },
                { "Viewport.IsAttached", "True while this viewport is part of the live tree. It goes FALSE when a resize shrinks the grid past it, or when Reset() replaces the root — at which point the object still answers questions about itself but is nowhere on screen. Shapes placed on a detached viewport are not lost: ResolveVisible re-homes them to the nearest surviving ancestor's first leaf. Worth testing if you hold a Viewport in a field across runs." },
                { "Viewport.Item", "The [row] indexer. Returns a ViewportRow, so that vp[row][column] reads two-dimensionally and so the row itself can be given a Height. 0-BASED, ROW FIRST. A row outside 0..Rows-1 (including a negative) throws ArgumentOutOfRangeException whose message NAMES THE CURRENT SIZE — \"Viewports[2] is out of range. The layout is 1 row x 1 column ...\" — because the usual cause is indexing before setting Rows. On a LEAF the only cell is itself, which is why Viewports[0][0] is the root on the default 1x1 layout." },
                { "Viewport.ResolveVisible", "The leaf this viewport's shapes are actually drawn in RIGHT NOW, and the method the shape registry consults when it decides which cell to draw a shape in. following both ways a viewport can stop being drawable: SUBDIVIDED, so it goes down to the first cell (the cell stayed where it was, it merely got split), and REMOVED by a resize or a reset, so it goes up to the nearest surviving ancestor first, falling back to the current root when the whole subtree is gone. Resolved on demand rather than fixed up at resize time, so the rule keeps holding however many times a cell is split again. This is why shrinking a layout re-homes shapes rather than losing them or throwing." },
                { "Viewport.FirstLeaf", "The leaf this viewport draws through once it has been SUBDIVIDED: itself when it is still a leaf, otherwise its first descendant leaf, descending first-cell-first however deep the nesting goes. Resolved on demand rather than fixed up at the moment a leaf is split, so \"the cell stayed where it was, it just got split\" keeps holding however many times it is split again. It walks DOWN only. ResolveVisible is the one that also walks UP past a viewport the layout has removed, and it is what the shape registry consults — reach for FirstLeaf when you know the viewport is still attached and only want the downward half." },
                { "Viewport.ToString", "Returns Path — \"Viewports\", \"Viewports[0][1]\", and so on: how the viewport is written in code, which is what makes it readable in a log line or an exception message." },

                { "ViewportRow.Item", "The [column] indexer — the cell at this row and that column, as a Viewport. This is the second half of Viewports[row][column]. A column outside 0..Columns-1 (including a negative) throws ArgumentOutOfRangeException whose message names the viewport's current size. On a LEAF the only cell is itself, so on the default 1x1 layout Viewports[0][0] returns the root." },
                { "ViewportRow.Height", "How tall THIS ROW is, in XAML's grid-length spelling: \"*\" for a share, \"3*\" for three shares, or a number for a fixed pixel height. Defaults to \"*\". Viewports[0].Height = \"2*\" is the direct way to say what Viewports[0][0].Height = \"2*\" says through a cell — a height belongs to the row, so both set the same value and every cell in the row reads it back. An unparseable value throws ArgumentException and changes nothing; \"Auto\" is rejected by name. Reads back canonical, so \"1*\" comes back as \"*\". Setting it on a row of an undivided viewport is accepted and simply has nothing to divide, unlike Viewport.Height on the root, which throws." },

                { "ViewportRoot.Viewports", "Static. The whole drawing surface — the same object as Viewport.Root, and the one member of this class. Written as the bare name Viewports, because the compiler injects `global using static C2VGeometry.ViewportRoot;` into every compilation. Split it with Viewports.Rows and Viewports.Columns, reach a cell with Viewports[row][column] (0-based, row first), and put a shape on one with shape.Place(Viewports[0][1]). On the default 1x1 layout Viewports[0][0] IS this object, which is why a bare Place() and Place(Viewports[0][0]) mean the same thing." },

                { "ViewportLength.Star", "Static readonly. One share of the remaining space — the parsed form of \"*\", and the default every row and column starts at. Value is 1 and IsStar is true. ViewportLength.Parse(\"1*\") == ViewportLength.Star." },
                { "ViewportLength.Value", "The number of SHARES when IsStar is true, otherwise a size in DEVICE PIXELS. Read it together with IsStar — 3 means \"three shares\" for \"3*\" and \"three pixels\" for \"3\", and the two are not comparable." },
                { "ViewportLength.IsStar", "True for the \"*\" forms (a share of whatever is left), false for a fixed pixel size. Shares are relative: \"3*\" beside \"*\" takes three quarters of the space, the same arithmetic a XAML Grid does. Fixed sizes are taken out first, and the shares divide what remains." },
                { "ViewportLength.Parse", "Static. Parse(string text) — reads \"*\", \"3*\", \"1.5*\" or a plain number such as \"240\". Surrounding whitespace is trimmed, and the number is read with the INVARIANT culture, so \"1.5*\" means one and a half shares whatever the machine's decimal separator is. Throws ArgumentException on an empty or null string, on text it cannot read, on a share that is zero or negative, on a negative pixel count, and on \"Auto\" — which is rejected BY NAME, because a canvas has no natural size of its own, so an auto-sized viewport would collapse to nothing and look like the drawing had vanished. Zero pixels is accepted. This is what Viewport.Height / Width and ViewportRow.Height call, so a typo throws where it is written rather than collapsing a cell several layers away in the renderer. Together with the static Star field it is the only way to make one — the constructor is private." },
                { "ViewportLength.ToString", "The canonical spelling — what Height and Width read back after being set. One share prints as \"*\" rather than \"1*\"; other shares print as the number followed by \"*\"; a fixed size prints as the bare number. Formatted \"0.####\" with the invariant culture, so \"1.5*\" round-trips and a trailing zero is not invented." },
                { "ViewportLength.Equals", "Value equality: two lengths are equal when IsStar matches AND Value matches exactly. Exact, not fuzzy — these are authored values, not computed geometry. The == and != operators forward to it, and the object overload returns false for anything that is not a ViewportLength." },
                { "ViewportLength.GetHashCode", "Combines Value and IsStar, so it agrees with Equals and the struct is usable as a dictionary key. \"3*\" and \"3\" hash differently, as they must — one is three shares, the other three pixels." },


                // Sketch (DoodleSharp.Sketching). Reached through DocGenerator.AllowedInternalTypes:
                // the namespace also holds SketchRuntime, which is the host's frame pump.
                //
                // Size, Background, NoLoop and Loop are deliberately absent: they are
                // PROTECTED, and MemberFlags is public-only, so the help page cannot list them
                // and an entry here would be both unreachable and a failure of
                // DocumentationAccuracyTests, which resolves keys with BindingFlags.Public.
                // All four are covered in the type summary and the C# sample instead.
                { "Sketch.Setup", "Override to run one-time initialisation before the frame loop starts — this is where Size() and Background() belong, and where any long-lived shape is created. Called exactly once per run, and the canvas is cleared immediately before it so your shapes register onto a fresh scene. If it throws, the runtime STOPS the sketch and reports the exception to the console rather than letting it reach WPF. The base implementation does nothing, so there is nothing to call through to." },
                { "Sketch.Draw", "Override to run code every frame — the body of the sketch. THE REGISTERED SHAPES ARE CLEARED IMMEDIATELY BEFORE EACH CALL, so geometry built here is fresh every frame and a shape you kept in a field must be Place()d again to reappear. Use DeltaSeconds rather than a fixed step so motion is frame-rate independent. It is not called while the loop is paused by NoLoop(). If it throws, the runtime stops the sketch and reports the exception and the frame number to the console. The base implementation does nothing." },
                { "Sketch.FrameCount", "The frame number, 0 for the first Draw() call and incrementing from there. Set by the runtime before each call — read-only from your code. Useful for doing something every Nth frame, or for stopping after a fixed count." },
                { "Sketch.ElapsedSeconds", "Seconds since Setup() returned, as a double. Set by the runtime before each Draw(). This is WALL-CLOCK time, so it keeps advancing even if frames are slow — it is the value to drive an animation by when you want it to take a real two seconds regardless of frame rate. It does NOT advance while the loop is paused by NoLoop()." },
                { "Sketch.DeltaSeconds", "Seconds since the previous frame. Set by the runtime before each Draw(), and 0 for the Setup() call. THIS IS THE ONE TO MULTIPLY BY: writing angle += 90 * DeltaSeconds turns 90 degrees per second whatever the frame rate, whereas angle += 1.5 turns at whatever speed the machine happens to manage." },
                { "Sketch.Width", "The sketch's logical width in WORLD units — what Size() declared, defaulting to 800. The drawing area is centred on the origin, so the visible span runs from -Width/2 to +Width/2. Read-only from your code; set it through Size()." },
                { "Sketch.Height", "The sketch's logical height in WORLD units — what Size() declared, defaulting to 600. Centred on the origin like Width, so the span runs from -Height/2 to +Height/2. Read-only from your code; set it through Size()." },
                { "Sketch.MouseX", "Last known cursor X in canvas WORLD coordinates (Y-up, origin at the canvas centre). Filled in by the runtime before each Draw() from the same pointer tracking Mouse.X uses, so it works with no handler registered and without putting the canvas into interactive mode. It holds the last position seen, so before the pointer has ever entered the canvas it is 0." },
                { "Sketch.MouseY", "Last known cursor Y in canvas WORLD coordinates — POSITIVE IS UP, unlike screen coordinates. Filled in before each Draw() from the same source as Mouse.Y, so polling it costs nothing and needs no handler." },
                { "Sketch.MousePressed", "True while any mouse button is held over the canvas. Filled in before each Draw() from the same tracking Mouse.IsDown uses. It is a POLLED state, not an event — a click that starts and ends between two frames is missed, so use Mouse.OnClick when every press has to be caught." },
                { "Sketch.KeyPressed", "Declared as \"true while any key is held with the canvas focused\", but NOTHING IN THE APPLICATION EVER WRITES IT: it is false in every sketch, on every frame, always. It is documented here so the gap is visible rather than discovered — do not branch on it. Use the Mouse callbacks for input, or read modifier state from a MouseInfo (e.Shift, e.Ctrl, e.Alt)." },
                { "Sketch.LastKey", "Declared as the name of the last key pressed, but like KeyPressed it has NO WRITER anywhere in the application and stays at the empty string for the whole life of a sketch. Do not branch on it." },

                // ---------------------------------------------------------------------------------
                // Constructors. Keyed by SIGNATURE, not by member name, because every constructor
                // reflects as ".ctor" and a whole overload set would otherwise share one entry --
                // see GetConstructorDescription. The key is exactly what the Type/Signature column
                // prints: the clean type name, then the parameter TYPES in order, comma-space
                // separated, primitives spelled the C# way (double, int, bool, string).
                // Add a constructor to the API, add its line here.
                // ---------------------------------------------------------------------------------

                // Core value types
                { "VXYZ(double, double, double)", "The full 3D coordinate: X, Y and Z. The canvas is 2D and Y-UP with (0, 0) at its centre, so Z is carried through the arithmetic but nothing draws with it. The components are read-only afterwards — every operation returns a new VXYZ, so one can be shared without aliasing bugs. It never registers on the canvas, which is what makes it the right type for intermediate maths." },
                { "VXYZ(double, double)", "The 2D form, with Z set to 0. This is the one to reach for in a 2D drawing: new VXYZ(10, 25) is a point 10 to the right of and 25 ABOVE the centre of the canvas. Constructing it draws nothing — use VPoint when you want a visible marker." },
                { "VXYZ()", "The origin, (0, 0, 0) — the same value as the static VXYZ.Zero, which is the more readable spelling. It exists so a VXYZ can be default-constructed in generic code." },
                { "BoundingBox(VXYZ, VXYZ)", "An axis-aligned box from its lower-left (min) and upper-right (max) corners. The corners are stored exactly as given and are NOT normalised, so passing them the wrong way round yields negative Width and Height rather than an error. You normally receive one of these from Shape.GetBounds() rather than constructing it." },
                { "ControlPoint(ControlPointType, double, double, string)", "One draggable editing handle at (x, y) in world coordinates (Y-up, origin at the canvas centre). label defaults to an empty string and is what the canvas shows beside the handle. Type and Label are read-only once set; X and Y stay settable. Shapes build these inside GetControlPoints(), so you rarely construct one yourself." },
                { "IntersectionResult()", "An empty result: no points and no overlapping curves, so HasIntersection is false. You normally get one back from CurveIntersection rather than constructing it — Points and Curves are read-only properties, so a result is built by adding to them." },
                { "VTransform()", "The identity transform: BasisX/BasisY/BasisZ set to the three unit axes and Origin at zero, so OfPoint and OfVector hand back what they are given. VTransform.Identity says the same thing more clearly. Build a real transform with CreateRotationDegrees, CreateRotationRadians or CreateReflection." },

                // Shapes. Every one of these registers the shape on the canvas as it returns --
                // there is no Draw() call to make. Place() is for results that did NOT come from a
                // plain new (a boolean-op result, a query answer, anything built with
                // Shape.AutoRegister off).
                { "VLine(VXYZ, VXYZ)", "A straight segment from start to end, both in world coordinates (Y-up, origin at the canvas centre). Zero length is allowed and simply draws nothing; Direction on such a line is Zero rather than NaN, because Normalize does not throw. Registers on the canvas as it returns." },
                { "VLine(double, double, double, double)", "The same segment written as four coordinates: (x1, y1) to (x2, y2). Exactly equivalent to the two-VXYZ form, and usually shorter for literal coordinates." },
                { "VLine(VXYZ, double, double)", "A segment of the given length starting at startPoint and heading at angleInDegrees — DEGREES, counter-clockwise from the positive X axis, which is the convention everywhere in this library bar VTransform.CreateRotationRadians. A negative length simply points the segment the other way." },
                { "VCircle(VXYZ, double)", "A circle of the given radius about centre. A radius of 0 is allowed and draws nothing. Default stroke colour is Yellow; the parameter domain runs counter-clockwise from angle 0, the point at (Center.X + Radius, Center.Y). Registers on the canvas as it returns." },
                { "VCircle(double, double, double)", "The same circle with the centre written as two coordinates: new VCircle(0, 0, 50) is a radius-50 circle at the centre of the canvas." },
                { "VCircle(VXYZ, VXYZ, VXYZ)", "The CIRCUMCIRCLE through three points — the one circle passing through all three. Throws ArgumentException when the points are collinear, because no such circle exists. FromCenterDiameter and FromTwoPoints are the other two ways of naming a circle." },
                { "VArc(VXYZ, double, double, double)", "An arc of radius on a circle about centre, sweeping from startAngle to endAngle. Both angles are in DEGREES from the positive X axis, and NEITHER IS NORMALISED: the direction is the sign of endAngle - startAngle, so (0, 90) is a counter-clockwise quarter and (90, 0) is a CLOCKWISE one. A difference beyond 360 wraps past the full circle. Default stroke colour is Orange." },
                { "VArc(double, double, double, double, double)", "The same arc with the centre written as two coordinates: new VArc(0, 0, 50, 0, 90) is a quarter arc from due east round to due north." },
                { "VArc(VXYZ, VXYZ, VXYZ)", "The arc through three points: it starts at start, passes through mid and ends at end, with the centre, radius and sweep direction solved for you. Throws ArgumentException when the three points are collinear, because they define no arc." },
                { "VEllipse(VXYZ, double, double)", "A full ellipse about centre, radiusX along the ellipse's X axis and radiusY along its Y axis. Equal radii give a circle. NO CONSTRUCTOR TAKES THE ORIENTATION: the ellipse is axis-aligned unless you set Rotation, which is a property — new VEllipse(new VXYZ(0, 0), 80, 40) { Rotation = 30 } — or call Rotate(pivot, degrees), which writes it. Default stroke colour is Pink." },
                { "VEllipse(double, double, double, double)", "The same full ellipse with the centre written as two coordinates." },
                { "VEllipse(VXYZ, double, double, double, double)", "An elliptical ARC: the sweep runs from startAngle to endAngle, both in DEGREES and measured in the ELLIPSE'S OWN frame, so setting Rotation afterwards turns the drawn part with the ellipse rather than re-cutting a different part of it. Like VArc, neither angle is normalised: the direction is the sign of endAngle - startAngle, so (90, 0) is a clockwise quarter. A partial sweep encloses no area, so Contains becomes an on-the-curve test rather than an interior one. The curve is ARC-LENGTH parameterised, so Evaluate(t) and Divide(n) space points evenly along the curve rather than evenly in angle — EvaluateByAngle is the angle-linear reading." },
                { "VRectangle(VXYZ, double, double)", "Creates a rectangle from a corner point, width, and height. The corner is the BOTTOM-LEFT one and the rectangle extends right and UP from it. Negative width or height are allowed and simply mirror it across that corner. Inherits VPolygon, so Points, Area, Slice and the boolean ops all apply." },
                { "VRectangle(double, double, double, double)", "The same rectangle with the bottom-left corner written as two coordinates: new VRectangle(-50, -25, 100, 50) is centred on the canvas origin." },
                { "VRectangle(VXYZ, VXYZ)", "Creates a rectangle from two opposite corners, bottom-left and top-right. The corners are taken as given, so the other order produces the mirrored negative-width form rather than an error." },
                { "VPolygon(VXYZ[])", "A closed polygon through the vertices given, as a params list. THE CLOSING EDGE IS IMPLICIT — do not repeat the first point as the last, or the polygon carries a zero-length edge. Self-intersection is computed once, here, and reported by SelfIntersecting. Default stroke colour is LightBlue." },
                { "VPolygon(IEnumerable<VXYZ>)", "The same polygon from any sequence — the overload for a LINQ result or a List<VXYZ> you already hold. Identical behaviour to the params form, implicit closing edge included." },
                { "VPolygon(List<ICurve>)", "Builds a polygon from a list of OPEN curves that together form one closed loop; they are ordered into sequence for you, so the list need not already be in order. Throws ArgumentException when any curve is itself closed, when the curves leave a gap or cannot form a single loop, when more than two endpoints meet at a point (a branch), or when the loop self-intersects. Reach for Region instead when the edges must STAY curved — this flattens them to vertices." },
                { "VPolyline(VXYZ[])", "An OPEN chain of segments through the points given, as a params list. Unlike VPolygon nothing is closed for you — repeat the first point as the last if you want a closed outline. Default stroke colour is LightGreen." },
                { "VPolyline(IEnumerable<VXYZ>)", "The same open chain from any sequence, for a LINQ result or an existing List<VXYZ>." },
                { "VBezier(VXYZ, VXYZ, VXYZ, VXYZ)", "A cubic Bezier: p0 the start, p3 the end, and p1/p2 the two control handles the curve is pulled towards but does not pass through. Segments (default 32) is how finely it is flattened for drawing and for length queries. Default stroke colour is Purple." },
                { "VBezier(double, double, double, double, double, double, double, double)", "The same cubic Bezier with all four control points written as x,y pairs in order: (x0, y0) start, (x1, y1) and (x2, y2) handles, (x3, y3) end." },
                { "VSpline(VXYZ[])", "A smooth Catmull-Rom spline that passes THROUGH every point given — unlike a Bezier, whose handles it only approaches. SegmentsPerSpan (default 16) sets the tessellation between adjacent points and Tension (default 0.5) how loose the curve is. Default stroke colour is Violet." },
                { "VSpline(IEnumerable<VXYZ>)", "The same spline from any sequence of control points." },
                { "VPoint(double, double)", "A drawn dot at (x, y) in world coordinates. THIS IS A SHAPE, NOT A COORDINATE — it registers on the canvas as it returns, so use VXYZ for anything you are merely computing with. Color and FillColor are both set to White outright: VPoint is the one shape that does not honour ShapeDefaults.GlobalColor / GlobalFillColor." },
                { "VPoint(VXYZ)", "A drawn dot at position. The same warning applies — constructing one puts a marker on the canvas, which is exactly why VPoint's arithmetic operators all return a plain VXYZ rather than another VPoint." },
                { "VText(VXYZ, string)", "Draws content at location, at the default Height of 12 world units. location is the anchor point, and which part of the text sits on it is decided by Anchor. Mask is ON by default, so a solid rectangle in the canvas background colour is painted behind the glyphs — set Mask = false where the label sits over a filled shape and you do not want a hole punched in it. Default colour is White." },
                { "VText(VXYZ, string, double)", "The same, with an explicit Height in WORLD units — not points, not pixels — so the text scales with the drawing. MaskOffset, the padding behind the glyphs, is a fraction of this height rather than an absolute distance." },
                { "VText(double, double, string)", "As the two-argument form, with the location written as two coordinates, at the default Height of 12." },
                { "VText(double, double, string, double)", "Location as two coordinates plus an explicit Height in world units." },
                { "VArrow(VXYZ, VXYZ)", "A straight shaft from start to end with a V-shaped head at the END. HeadLength defaults to 15 world units and HeadAngle to 30 degrees, so the head spans 60; set DoubleEnded to put a head on the start as well. VArrow is a plain Shape, not an ICurve. Default colour is Orange." },
                { "VArrow(double, double, double, double)", "The same arrow with both ends written as coordinates: (x1, y1) tail to (x2, y2) tip." },
                { "VArrow(VXYZ, VXYZ, double)", "An arrow of the given length starting at startPoint and pointing along direction. The direction is NORMALISED for you, so its magnitude is irrelevant — length alone decides how long the arrow is." },
                { "VXLine(VXYZ, VXYZ)", "Creates an infinite construction line through basePoint along a DIRECTION — the second argument is a direction vector, NOT a second point on the line. It is normalised for you. If you hold two points write new VXLine(p1, p2 - p1), or use the four-coordinate overload, which IS the through-two-points form." },
                { "VXLine(double, double, double, double)", "Creates an infinite construction line through the two points (x1, y1) and (x2, y2). This overload IS the through-two-points form, unlike the two-VXYZ one, which takes a direction. Horizontal(y) and Vertical(x) are shorter for the axis-aligned cases." },
                { "VRay(VXYZ, VXYZ)", "Creates a ray starting at origin and extending in the given DIRECTION — the second argument is a direction vector, NOT a point the ray passes through. It is normalised for you, so its length is irrelevant. To aim a ray at a target point, use the four-coordinate overload, or subtract: new VRay(origin, target - origin)." },
                { "VRay(double, double, double, double)", "Creates a ray from (originX, originY) THROUGH the point (throughX, throughY) — this overload is the through-point form, unlike the two-VXYZ one, which takes a direction. AtAngle(origin, angleDegrees) and the four Horizontal/Vertical helpers cover the common aims." },
                { "VGroup()", "An empty group. Fill it with Add(shape) or by assigning Shapes; transforms, styling and animations applied to the group reach every child, and a PathAnimation or MoveAnimation on the group moves the children with it. Default colour White, FillColor Transparent." },
                { "VGroup(Shape[])", "A group of the shapes given as a params list: new VGroup(circle, line, text). The children keep their own styling unless you set the group's, and moving or rotating the group moves them together." },
                { "VGroup(IEnumerable<Shape>)", "A group from any sequence — the overload a LINQ result or an ArrayOps result lands in." },
                { "VGroup(List<Shape>)", "A group from a List<Shape>. Behaves identically to the IEnumerable form and is strictly redundant — a List would bind to that overload anyway — so which of the two you reach makes no difference." },
                { "VGrid(VXYZ, int, int, double, Nullable<double>, bool)", "A rectangular grid of VPoint markers: xcount by ycount points, xSpacing apart horizontally and ySpacing apart vertically. ySpacing is a double? whose null default means SAME AS xSpacing, so new VGrid(loc, 5, 5, 10) is a square grid with spacing 10 on both axes. centered defaults to true, which puts location at the middle of the grid; false makes location the bottom-left corner." },
                { "VGrid(VXYZ, int, int, double, bool)", "The uniform-spacing form: one spacing for both axes, with centered stated explicitly. centered deliberately has NO default here — giving it one would make this overload applicable to a four-argument call and make new VGrid(loc, 5, 5, 10) ambiguous. Omit centered and the main constructor handles it." },
                { "VGrid(VXYZ, int, int, bool)", "A grid with the default spacing of 1.0 on both axes, positioned by centered as in the other overloads. Useful as a unit lattice you then Scale()." },
                { "VSpatialGrid(VXYZ, int, int, double)", "A grid of square VCell shapes, xCount by yCount, each cellSize on a side. location is the CENTRE OF THE BOTTOM-LEFT CELL, not a corner of the whole grid — unlike VGrid, which positions by its full extent. Each cell is linked to its four orthogonal neighbours as the grid is built, which is what FindPath then walks." },
                { "VCell(VXYZ, double, int, int, int)", "One square cell of a VSpatialGrid, centred at center with sides of cellSize. uniqueId, column and row are the identity the parent grid assigns, and the grid is also what fills in Neighbours — so a cell constructed by hand is a bare square with no connectivity. Extends VPolygon." },
                { "VDimension(VXYZ, VXYZ)", "A linear dimension measuring between point1 and point2, with the dimension line held Offset away from them and the measured distance drawn as the label. Style is taken from ShapeDefaults' dimension values AT CONSTRUCTION, so set those first if you want a house style. Default colour is Yellow." },
                { "VDimension(double, double, double, double)", "The same linear dimension with both measured points written as coordinates: (x1, y1) to (x2, y2)." },
                { "VRadialDimension(VCircle)", "A radial dimension for a circle, taking its centre and radius AS THEY STAND AT THIS MOMENT — the dimension holds copies, so moving or resizing the circle afterwards does not update it. Set ShowDiameter to label the diameter instead of the radius." },
                { "VRadialDimension(VArc)", "The same for an arc, taking the arc's centre and radius. As with the circle overload the values are copied at construction, not tracked." },
                { "VRadialDimension(VXYZ, double)", "A radial dimension for a centre and radius you name yourself, with no shape to read them from — the overload for a circle you have not drawn, or for a radius you are computing." },
                { "VHatch(VPolygon, BuiltInHatch, double, double)", "Fills a polygon boundary with one of the built-in AutoCAD-style patterns, named by enum. scale defaults to 1 and angle to 0 DEGREES; angle rotates the whole pattern, on top of any rotation the pattern itself defines. The polygon's vertices are COPIED, so the hatch does not follow the polygon if you move it afterwards. Default colour is Cyan." },
                { "VHatch(VPolygon, string, double, double)", "The same, with the pattern named by string — BRICK, ANSI31 and so on. The name is resolved through BuiltInHatches, which hands back a fresh copy, so adjusting the resulting hatch cannot poison later lookups of that name." },
                { "VHatch(VPolygon, HatchType, double, double)", "The same, with a HatchType you built or parsed yourself — from HatchType.Parse of a .pat definition, or by assembling HatchPatternLine families by hand." },
                { "VHatch(List<VXYZ>, BuiltInHatch, double, double)", "Fills the closed boundary described by a list of vertices, with no VPolygon needed. The closing edge is implicit, exactly as in VPolygon — do not repeat the first point." },
                { "VHatch(List<VXYZ>, string, double, double)", "Boundary as a vertex list, pattern by name. The lookup is case-INsensitive, so ANSI31 and ansi31 both work, but an unknown name throws ArgumentException rather than silently drawing nothing." },
                { "VHatch(List<VXYZ>, HatchType, double, double)", "Boundary as a vertex list, pattern as a HatchType. This is the constructor every other overload delegates to, and the one that rejects a null boundary or pattern with ArgumentNullException." },
                { "VHatch(VPolygon, HatchType, double, double, bool)", "IDENTICAL to VHatch(VPolygon, HatchType, double, double) — the trailing bool is an unused discard, ignored entirely. It exists only as an overload disambiguator, and its own XML comment mis-describes it as taking a .pat definition string. Prefer the four-argument form, and use the static VHatch.FromDefinition when what you actually hold IS a .pat definition string." },
                { "Region(List<ICurve>)", "A region bounded by one closed loop of OPEN curves, ordered into sequence for you — so the list need not already be in order. Unlike VPolygon the edges keep their real curvature: an arc stays an arc. Throws ArgumentException when any curve is itself closed, when the curves leave a gap or cannot form a single loop, when more than two endpoints meet at a point, or when the loop self-intersects." },
                { "Region(List<ICurve>, List<List<ICurve>>)", "The same, plus holes: each inner list is one hole's closed loop, validated on the same terms as the outer one. Area is the outer loop minus the holes, and Contains is true only INSIDE the outer loop and OUTSIDE every hole." },
                { "Region(ICurve)", "Builds a region straight from ONE already-closed curve — a VCircle, a full VEllipse, or a closed VPolygon/VPolyline/VSpline/VBezier. NOTE THAT IT CONSUMES THE CURVE: the source is removed from the canvas so its outline is not drawn twice. Throws ArgumentNullException on null, and ArgumentException when the curve is open (start != end) and is not an inherently closed type, or has fewer than three distinct vertices." },
                { "PolygonWithHoles(VPolygon)", "An outer boundary with no holes yet — add them with AddHole. The outer boundary is re-wound COUNTER-CLOCKWISE for you, so winding order is one less thing to get right. You usually receive one of these from a BooleanOps *WithHoles method rather than constructing it." },
                { "PolygonWithHoles(VPolygon, IEnumerable<VPolygon>)", "Outer boundary plus its holes in one call. The outer is re-wound counter-clockwise, and Area is the outer area minus the holes." },

                // Hatch patterns
                { "HatchType()", "An empty pattern: no Name, no Description and no line families, so it draws nothing until you add to Lines. Intended for building a pattern with an object initializer. HatchType.Parse (a .pat definition) and HatchType.GetBuiltIn (a name or a BuiltInHatch value) are the usual ways to get one." },
                { "HatchType(string, string, List<HatchPatternLine>)", "A pattern with a name, a human-readable description and its line families. The list is stored BY REFERENCE, not copied, so mutating it afterwards changes the pattern — use Clone() when you want an independent copy." },
                { "HatchPatternLine()", "An empty line family: Angle, OriginX, OriginY, DeltaX and DeltaY all 0 and an empty Dashes array, which means a continuous line. On its own that generates nothing useful — set at least DeltaY, the perpendicular spacing between the parallel lines. For use with an object initializer." },
                { "HatchPatternLine(double, double, double, double, double, Double[])", "One family of parallel lines in AutoCAD .pat terms: angle in DEGREES, an origin the family is measured from, deltaX (the shift along the line direction between successive rows, which is what staggers a brick pattern) and deltaY (the perpendicular spacing between them). dashes is a params list where a positive number is a dash length, a negative number a gap, 0 a dot, and no dashes at all a continuous line." },

                // Ray casting
                { "RayCaster(IEnumerable<Shape>, int)", "Builds the BVH index over the shapes you hand it — there is no canvas-snapshot constructor, because the geometry library has no canvas; pass CanvasRenderer.Instance.GetShapes().OfType<Shape>() to cast against everything drawn. VPoint, VRay and VXLine are EXCLUDED from the index by type: the first has no area worth hitting, and the other two would only be tested against their bounding box and could return a confidently wrong hit. leafSize (default 8) is how many shapes a leaf node may hold before the builder splits it further, so raising it makes the tree shallower — a cheaper build, and more shapes tested per query. Build once and reuse; Refit() for small movements, a fresh instance for large scene changes." },
                { "RayHit(Shape, VXYZ, double)", "The positional constructor of the readonly record struct: the shape that was hit, the world-space hit Point, and Distance from the ray origin to it. You receive these from RayCaster rather than building them, but the constructor is public so a test or a stub can produce one." },
                { "RayQuery(VXYZ, VXYZ)", "The positional constructor of the readonly record struct describing one ray: an Origin and a Direction. The direction NEED NOT be normalised — RayCaster normalises it — but a zero-length direction has no meaning and will not hit anything." },

                // Charts
                { "ChartOptions()", "The only constructor, and every setting already has a workable default — Origin (0, 0), Width 400, Height 250, auto-fitted axis ranges, grid on — so new ChartOptions() is a usable chart and an object initializer overrides only what you care about. Pass it to a Chart.* method; omit it and these same defaults are used." },

                // Rendering seam (IPrimitiveSink and friends)
                { "ShapeTessellator()", "Creates a tessellator. It holds SCRATCH BUFFERS and is therefore NOT thread-safe — give each thread its own rather than sharing one. Reusing a single instance across many Tessellate calls on one thread is the intended pattern and is what keeps the conversion allocation-light." },
                { "TessellationHints()", "Default hints: Scale 1.0 — screen pixels per world unit, so a 1:1 view — and PreferNative false. Set Scale to the view's actual zoom before tessellating, or curves are flattened as though for a 1:1 view and look faceted once zoomed in. Set PreferNative on a sink that can express a circle as a circle." },
                { "PenSpec(string, string, double, LineType, double, double)", "The explicit constructor, taking colour, fill colour, line weight, line type, line type scale and opacity in that order. PREFER PenSpec.From(shape), which reads all six off a shape and cannot get the order wrong; this overload is for a caller synthesising a pen with no shape behind it." },
                { "BoundsPrimitiveSink()", "An empty measuring sink — nothing measured yet, so HasBounds is false and MinX/MaxX and friends are meaningless until something is. Feed shapes through a ShapeTessellator into it and it accumulates the bounding box of everything it is given. Measuring this way sees exactly what the renderer draws, which is why zoom-to-extents uses it rather than a private type switch." },
                { "PolylineFallbackSink()", "A reducing sink with every callback unset, which means everything is dropped until you assign the ones you want — OnPolyline, OnFilled, OnPoint, OnText. Anything the pass could not express is recorded in Unhandled rather than vanishing silently, which is the failure this type exists to prevent." },

                // Animations. Duration is in SECONDS everywhere; StartTime is assigned by the
                // Animator when the animation is added, not here.
                { "Animator()", "Creates an empty timeline. Add animations with AddToAnimations, which SEQUENCES them one after another by default, insert gaps with Pause(seconds), then call Animate() to play. Set Repeat to loop and Fps to cap the frame rate. Nothing plays until Animate() is called." },
                { "DrawAnimation(Shape, double)", "Progressively draws target over duration SECONDS, by animating DrawFactor from 0 to 1. NOTE THE SIDE EFFECT AT CONSTRUCTION: DrawFactor is set to 0 immediately — recursively, so a VGroup's children go too — so the shape is invisible from the moment this is constructed rather than from the moment the timeline reaches it." },
                { "MoveAnimation(Shape, VXYZ, double)", "Moves target by displacement — a RELATIVE offset in world units, not a destination — over duration seconds. It animates OffsetX/OffsetY rather than the shape's own coordinates, and the starting offset is captured when the animation's turn actually arrives, so it composes correctly after another move." },
                { "PathAnimation(Shape, ICurve, double)", "Moves target along path over duration seconds, matching the shape's bounding-box CENTRE to the point on the curve. The path is any ICurve — line, arc, circle, polyline, spline, bezier — and is sampled by arc length, so motion is even rather than bunching where the curve is tight. The path shape itself stays drawn; remove it if you only wanted the trajectory." },
                { "RotateAnimation(Shape, VXYZ, double, double)", "Rotates target about pivot by angleDegrees — DEGREES, counter-clockwise, and RELATIVE to whatever rotation the shape already has — over duration seconds. It works on every shape type: the rotation is applied once by the renderer rather than per shape, so a polygon, group or text rotates just as a circle does. Note that rotation is a render-time transform, so Contains and hit-testing still use the unrotated geometry." },
                { "FlipAnimation(Shape, VLine, double)", "Mirrors target across mirrorAxis over duration seconds, animating FlipProgress from 0 to 1 so the shape appears to swing through the axis rather than jumping. The axis is a real VLine, so it is drawn unless you remove it; only the infinite line through it matters, not its length." },
                { "FadeInAnimation(Shape, double)", "Fades target from fully transparent to its own Opacity over duration seconds." },
                { "FadeOutAnimation(Shape, double, double)", "Fades target from its current opacity down to targetOpacity over duration seconds. targetOpacity defaults to 0 (fully transparent); pass something like 0.2 to leave a ghost behind instead of vanishing." },
                { "TransformAnimation(Shape, Shape, double)", "Morphs one shape into another over duration seconds. Both outlines are sampled into matched point sets and interpolated through an internally-managed VPolyline proxy, which is the only thing on screen during the transition; the real destination, with its own fill and styling, is revealed at the end. Throws ArgumentNullException on a null shape. A VGroup morphs by its longest child contour, and a non-curve shape (VText, VArrow) falls back to its bounding-box outline." },
                { "TransformAnimation(VText, int, Shape, double)", "Morphs a SINGLE CHARACTER of a VText into another shape over duration seconds — charIndex is 0-based. The rest of the word stays visible and that character is replaced by a space exactly when its morph begins, so it reads as the letter itself transforming. Throws ArgumentException when the character has no outline to lift: whitespace, an index out of range, or no glyph provider set." },
                { "ValueAnimation(T, Expression<Func<T, double>>, double, double, double)", "Animates any double property of a SHAPE between two values over duration seconds — new ValueAnimation<VCircle>(c, x => x.Radius, 10, 60, 2). The property is named by a simple member-access lambda; anything more complicated throws ArgumentException. The property is set to startValue immediately at construction, not when the timeline reaches it." },
                { "ValueAnimation(T, Expression<Func<T, double>>, List<double>, double)", "The keyframe form: the property is driven through the whole sequence of values, EVENLY SPACED across duration. Throws ArgumentException when values is null or holds fewer than two entries. As with the two-value form, the first value is applied at construction." },
                { "ObjectPropertyAnimation(T, Expression<Func<T, double>>, double, double, double)", "The same idea as ValueAnimation but for an object that is NOT a Shape — your own class with a double property — which is why its Target is null. Useful for driving a value that your own per-frame code then reads. The property is set to startValue at construction, so the object is already at the start of the animation before playback begins." },

                // Mouse
                { "MouseInfo(MouseEventKind, VXYZ, VXYZ, double, double, MouseButtonKind, bool, bool, bool, bool, bool, bool, int, int, double, Func<VXYZ, Shape>, Viewport)", "Builds an event payload by hand. THE CANVAS IS WHAT NORMALLY CALLS THIS — you only need it to drive a handler yourself from a test, which is possible precisely because MouseInfo names no WPF type. Everything after screenY has a default, so a minimal call is new MouseInfo(MouseEventKind.Click, p, p, 100, 200). hitTest is the function Target is computed from, called at most once and against RawPosition; viewport is which cell the event came from and is null for an undivided canvas." },

                // Console
                { "ConsoleEntry()", "An empty entry: no message, no module, line 0, not an error. You normally get these back from ConsoleOutput.GetEntries() rather than constructing them — every member is settable, so build one with an object initializer if you are feeding a panel yourself. Set FilePath and a LineNumber above 0 to make it clickable." },

                // Exporters
                { "DxfExporter()", "Creates the exporter; there is nothing to configure on it. Call Export(shapes, filePath) to write a file or ExportToString(shapes) to get the text. Output is R12 ASCII DXF, which has no colour, no background fill and no viewport concept — a masked VText exports as plain text, and a tiled export is flattened into model space with screen distances for coordinates." },
                { "PdfExporter()", "Creates the exporter; page size and scale are arguments to Export rather than properties. The short Export(shapes, filePath) auto-sizes the page to the drawing; the longer overload takes explicit page dimensions in MILLIMETRES and a scale. Output is real vector PDF, not a screenshot." },
                { "GifEncoder(Stream, int, int, int, bool)", "Starts writing an animated GIF into stream, at width by height PIXELS. frameDelayMs (default 100) and repeat (default true, meaning loop forever) are FIXED HERE — they are constructor arguments, not properties, so decide them before the first frame. GIF stores the delay in CENTISECONDS, so the value is divided by 10 and floored at 2: anything under 20 ms becomes 20, and 105 ms and 100 ms are the same file. A null stream throws ArgumentNullException. Then call AddFrame per frame and Dispose() to write the trailer: THERE IS NO Save(), the file is not a valid GIF until it is disposed, and AddFrame after that throws ObjectDisposedException. Dispose does NOT close the stream — that stays yours." },
                { "VideoExporter(string, int, int, int, UInt32)", "Opens an MP4 at filePath for writing, at width by height PIXELS, using the Windows Media Foundation H.264 encoder — nothing external to install. fps defaults to 30 and bitrateMbps to 5. Feed it AddFrame(RenderTargetBitmap) once per frame IN ORDER, then Dispose() to finalise the file; it implements IDisposable, so a using statement is the safe form — an abandoned exporter leaves an unplayable file." },
                { "PdfTile(Rect, double, double, double, IReadOnlyList<IDrawable>)", "The positional constructor of the readonly record struct describing one cell of a divided drawing for PdfExporter.ExportTiled. PageRect is where the cell sits inside the on-screen container in DEVICE PIXELS; Scale is that cell's zoom in screen pixels per world unit (the same quantity as MouseInfo.Scale, not its reciprocal); PanX and PanY are that cell's pan in pixels; Shapes is what is placed on it. In the app these come from the viewport host, not from arithmetic over rows and columns." },
                { "SvgTile(Rect, double, double, double, IReadOnlyList<IDrawable>)", "The positional constructor of the readonly record struct describing one cell for SvgExporter.ExportTiled and SaveTiledToFile. The five members mean exactly what PdfTile's do: PageRect in device pixels, Scale in screen pixels per world unit, PanX/PanY in pixels, and the shapes on that cell. Note that a tiled export renders THE VIEW — each cell at its own zoom and pan — whereas the untiled SvgExporter.Export sizes its viewBox to the shape bounds and ignores the screen entirely." },
                // DoodleSharp.Canvas types reached through AllowedInternalTypes
                { "SnapEngine()", "Creates a snap engine with ALL EIGHT snap types enabled — endpoint, midpoint, centre, intersection, nearest, perpendicular, extension and tangent — and a tolerance of 15 SCREEN PIXELS — screen, not world, so a snap stays equally easy to hit at any zoom. The application owns one of these and drives it from the drawing and measuring tools; construct your own only if you are snapping outside those." },
                { "SnapResult(VXYZ, SnapType, double)", "One snap candidate: the world-coordinate Point to snap to, the Type that produced it, and Distance from the cursor in SCREEN PIXELS (which is what makes results from different snap types comparable). The other members — ExtensionAngle, ReferenceSource and TangentCenter — are settable and are filled in by the engine afterwards, only for the snap types they apply to. ConstraintPoint is [Obsolete] and is always exactly Point." },
                { "DrawingTool()", "Creates a drawing tool in DrawingMode.None — armed for nothing. The application owns one per canvas and switches it with the P/L/C/R keys and the Draw menu; it collects click points, previews the shape being drawn and generates the matching source. There is nothing to configure on construction." },
                { "GlyphOutlineProvider()", "Creates the WPF glyph-outline provider. The application constructs one at startup and assigns it to VText.GlyphOutlineProvider, which is the seam C2VGeometry uses to turn text into contours without owning a font engine. You do not normally construct one — if VText.GlyphOutlineProvider is null, ToCharShape, LiftChar and LiftChars all return null." },
            };
        }

        /// <summary>
        /// True when the member is reached through the type rather than an instance. Enum values
        /// are static fields but reporting them as such would be noise, so they are excluded.
        /// </summary>
        internal static bool IsStaticMember(MemberInfo member) => member switch
        {
            MethodInfo m => m.IsStatic,
            PropertyInfo p => (p.GetMethod ?? p.SetMethod)?.IsStatic == true,
            FieldInfo f => f.IsStatic && !(f.DeclaringType?.IsEnum ?? false),
            // An event's staticness lives on its accessors, same as a property's. Without this a
            // static event (Frame.CallbackFailed, Mouse.CallbackFailed) read as an instance member,
            // which is precisely the "how do I call this?" confusion the static flag exists to answer.
            EventInfo e => (e.AddMethod ?? e.RemoveMethod)?.IsStatic == true,
            _ => false
        };

        /// <summary>
        /// The description for ONE constructor overload, keyed by its signature rather than by name.
        ///
        /// <para>
        /// Every constructor reflects as <c>.ctor</c>, so the name-keyed lookup every other member
        /// uses would give a whole overload set one shared entry — and there is no useful single
        /// sentence covering <c>VRectangle(VXYZ, double, double)</c> and
        /// <c>VRectangle(VXYZ, VXYZ)</c> at once, since which arguments mean what is precisely the
        /// thing a reader needs told. The key is therefore the signature as the Type/Signature column
        /// prints it: <c>"VRay(VXYZ, VXYZ)"</c>, <c>"VRay(double, double, double, double)"</c>.
        /// </para>
        ///
        /// <para>
        /// This is also a defect fix, not just a mechanism: seven such entries had been written —
        /// the <c>VRay</c>/<c>VXLine</c> pairs documenting that the second <c>VXYZ</c> argument is a
        /// DIRECTION rather than a second point, which silently aims the shape elsewhere if you
        /// assume otherwise — and nothing ever built that key, so no reader could reach any of them.
        /// The reflection two-way diff flagged them as keys naming nothing, which is how they were
        /// found; they are real API and the answer was to look them up, not to delete them.
        /// </para>
        ///
        /// <para>
        /// An overload with no entry renders a blank description cell, exactly as before.
        /// </para>
        /// </summary>
        private string GetConstructorDescription(string className, ConstructorInfo constructor)
        {
            if (_memberDescriptions == null) return "";

            var paramTypes = string.Join(", ",
                constructor.GetParameters().Select(p => GetFriendlyTypeName(p.ParameterType)));

            return _memberDescriptions.TryGetValue($"{className}({paramTypes})", out var desc) ? desc : "";
        }

        private string GetMemberDescription(string className, string memberName)
        {
            var key = $"{className}.{memberName}";
            if (_memberDescriptions != null && _memberDescriptions.TryGetValue(key, out var desc))
                return desc;
            return "";
        }

        /// <summary>
        /// The description for a member, falling back to whichever base type or interface actually
        /// declares it. Most inherited members are documented once on the declaring type — Shape's
        /// forty-odd styling and transform members, ICurve's sampling methods, Animation's timing
        /// members, VPolygon's vertex members (which VRectangle and VCell inherit) — and repeating
        /// each of those on every subclass would be a hundred-odd copies to keep in step.
        ///
        /// <para>
        /// The walk is driven by the member's REAL declaring type and the type's interface list, not
        /// by a hard-coded pair of names as it was before. That matters because the previous version
        /// tried "Shape" and "ICurve" for every type regardless of whether it was one, so an
        /// unrelated type with a member of the same name (Name, Color, Move, Intersect — all common)
        /// could pick up a description belonging to something else entirely.
        /// </para>
        /// </summary>
        private string GetInheritedMemberDescription(string className, MemberInfo member)
        {
            var direct = GetMemberDescription(className, member.Name);
            if (!string.IsNullOrEmpty(direct)) return direct;

            // The type that actually declares it, then its bases: VRectangle.AddPoint is
            // VPolygon.AddPoint, DrawAnimation.StartTime is Animation.StartTime.
            for (var t = member.DeclaringType; t != null && t != typeof(object); t = t.BaseType)
            {
                var inherited = GetMemberDescription(GetCleanTypeName(t), member.Name);
                if (!string.IsNullOrEmpty(inherited)) return inherited;
            }

            // Interfaces last: a curve shape's Divide/Measure/Project are documented on ICurve, and
            // an explicit interface implementation is not reflected as declared on the shape at all.
            var owner = member.DeclaringType ?? member.ReflectedType;
            if (owner != null)
            {
                foreach (var iface in owner.GetInterfaces())
                {
                    var fromInterface = GetMemberDescription(GetCleanTypeName(iface), member.Name);
                    if (!string.IsNullOrEmpty(fromInterface)) return fromInterface;
                }
            }

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
            AddListItem(featuresList, "Viewports", "Split the canvas into a grid of independent views - Viewports.Rows / Viewports.Columns, any cell subdividing again - and place shapes per cell with shape.Place(Viewports[0][1])");
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

            // Naming
            AddWelcomeSectionHeader(doc, "Names You Cannot Use");
            doc.Blocks.Add(new Paragraph(new Run(
                "Your project's name becomes the namespace of every file it generates, and C# searches the enclosing " +
                "namespace before any using directive. So a project called Mouse produces \"namespace Mouse\", and inside it " +
                "Mouse.OnMove(...) is looked up in your own namespace instead of DoodleSharp.Animation.Mouse. The same " +
                "applies to anything you declare yourself: a class, field, local, parameter, foreach variable or pattern " +
                "variable called Mouse hides the library's Mouse for the whole of that scope."))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            var namingList = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 15)
            };
            AddListItem(namingList, "Which names",
                "Every type in the namespaces a generated file imports - System, System.Linq, System.Numerics, System.Collections.Generic, C2VGeometry, DoodleSharp.Animation, DoodleSharp.Console, and DoodleSharp.Sketching in a sketch. That includes Mouse, Frame, Canvas, Shape, Sketch, Chart, Region, Console, Math, List and every V-prefixed shape name, plus the C# keywords themselves");
            AddListItem(namingList, "Case matters",
                "The check is case-sensitive, so a lowercase local called mouse, canvas or shape shadows nothing and is left alone");
            AddListItem(namingList, "New projects fix themselves",
                "A project created with a colliding name is given a non-colliding namespace instead - name it Mouse and the namespace is MouseProject. There is nothing to do");
            AddListItem(namingList, "If you hit it",
                "The error is reported on your own declaration and reads \"Mouse is a keyword. try another name\". Rename the declaration. For an existing project, editing the namespace line by hand is enough - Run finds Viz.Main() by scanning when the namespace no longer matches the project name");
            doc.Blocks.Add(namingList);

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
            AddDrawingToolRow(drawingRowGroup, "Circle", "Click center, click a point at the radius", "2", false);
            AddDrawingToolRow(drawingRowGroup, "Rectangle", "Click corner, click opposite corner", "2", true);
            AddDrawingToolRow(drawingRowGroup, "Ellipse", "Click center, click for the two radii", "2", false);
            AddDrawingToolRow(drawingRowGroup, "Arc", "Click start, a point ON the arc, end", "3", true);
            AddDrawingToolRow(drawingRowGroup, "Polygon", "Click vertices, double-click to close", "N", false);
            AddDrawingToolRow(drawingRowGroup, "Polyline", "Click points, double-click to finish", "N", true);
            AddDrawingToolRow(drawingRowGroup, "Bezier", "Click start, ctrl1, ctrl2, end", "4", false);
            AddDrawingToolRow(drawingRowGroup, "Spline", "Click points, double-click to finish", "N", true);
            AddDrawingToolRow(drawingRowGroup, "Arrow", "Click tail, click head", "2", false);
            AddDrawingToolRow(drawingRowGroup, "Text", "Click position, then type the string", "1", true);

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
            AddShortcutRow(rowGroup, "Alt+Shift+F", "Format code", false);
            AddShortcutRow(rowGroup, "Ctrl+/", "Toggle comment", true);
            // Find and Replace
            AddShortcutRow(rowGroup, "Ctrl+F", "Find", false);
            AddShortcutRow(rowGroup, "Ctrl+H", "Find and Replace", true);
            AddShortcutRow(rowGroup, "Ctrl+Shift+F", "Find in Files", false);
            AddShortcutRow(rowGroup, "F3", "Find Next", true);
            AddShortcutRow(rowGroup, "Shift+F3", "Find Previous", false);
            // Line operations
            AddShortcutRow(rowGroup, "Alt+Up/Down", "Move line up/down", true);
            AddShortcutRow(rowGroup, "Shift+Alt+Up", "Copy line up", false);
            AddShortcutRow(rowGroup, "Shift+Alt+Down", "Copy line down", true);
            AddShortcutRow(rowGroup, "Ctrl+Shift+D", "Delete line", false);
            // Selection operations
            AddShortcutRow(rowGroup, "Shift+Alt+Right", "Expand selection", true);
            AddShortcutRow(rowGroup, "Shift+Alt+Left", "Shrink selection", false);
            AddShortcutRow(rowGroup, "Ctrl+D", "Add next occurrence", true);
            AddShortcutRow(rowGroup, "Ctrl+Shift+L", "Select all occurrences", false);
            AddShortcutRow(rowGroup, "Ctrl+Alt+Up", "Add cursor above", true);
            AddShortcutRow(rowGroup, "Ctrl+Alt+Down", "Add cursor below", false);
            // Canvas & Tools
            AddShortcutRow(rowGroup, "Mouse Wheel", "Zoom canvas", true);
            AddShortcutRow(rowGroup, "Middle Click", "Pan canvas", false);
            AddShortcutRow(rowGroup, "Ctrl+G", "Zoom to shape by ID", true);
            AddShortcutRow(rowGroup, "Ctrl+M", "Toggle Measuring Tape tool", false);
            AddShortcutRow(rowGroup, "F4", "Toggle Properties panel", true);
            AddShortcutRow(rowGroup, "F6", "Toggle Global Parameters panel", false);
            AddShortcutRow(rowGroup, "F9", "Toggle Snap to Grid", true);
            AddShortcutRow(rowGroup, "F10", "Toggle the frame-timing readout", false);
            AddShortcutRow(rowGroup, "Ctrl+Shift+M", "Toggle Minimap", true);
            AddShortcutRow(rowGroup, "Ctrl+R", "Reset the panel layout", false);
            // Drawing Tools
            AddShortcutRow(rowGroup, "P", "Point drawing tool", true);
            AddShortcutRow(rowGroup, "L", "Line drawing tool", false);
            AddShortcutRow(rowGroup, "C", "Circle drawing tool", true);
            AddShortcutRow(rowGroup, "R", "Rectangle drawing tool", false);
            AddShortcutRow(rowGroup, "Shift (hold)", "Orthogonal constraint while drawing", true);
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

            // Performance and rendering
            AddWelcomeSectionHeader(doc, "Performance and Rendering");
            doc.Blocks.Add(new Paragraph(new Run(
                "Press F10 to show the frame-timing readout in the top-left corner of the canvas. It is a diagnostic " +
                "and is off by default, because measuring costs something; while it is off it costs nothing. Four lines:"))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            var hudList = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 15)
            };
            AddListItem(hudList, "p50 / p95",
                "Median and 95th-percentile frame time in milliseconds, with the frame rate the p95 figure implies. p95 is the number to judge smoothness by — it is the occasional slow frame you feel, not the average one");
            AddListItem(hudList, "cull / raster / backend",
                "Time spent deciding what is on screen, time spent drawing it, and which renderer drew this frame (\"vector\" or \"raster\")");
            AddListItem(hudList, "visible / examined",
                "Shapes drawn versus shapes considered. The ratio is the one to watch: near 1.0 means the spatial index is doing its job; if \"examined\" tracks the size of the whole drawing, it is not");
            AddListItem(hudList, "alloc / gen0",
                "Bytes allocated per frame and garbage collections so far. Steady allocation per frame in a still scene is the signature of derived data being rebuilt that could be cached");
            doc.Blocks.Add(hudList);

            doc.Blocks.Add(new Paragraph(new Run(
                "Which renderer draws the scene is a setting, RenderBackend, in %APPDATA%\\DoodleSharp\\appsettings.json. " +
                "There is no reason to change it unless you are chasing a specific problem — the default adapts on its own."))
            { FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });

            var backendList = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(20, 0, 0, 15)
            };
            AddListItem(backendList, "Auto (default)",
                "Chooses per frame. A light view is drawn by the vector renderer for exact fidelity; when a frame gets expensive it switches to the rasterizer, and switches back when the scene thins out. This is faster than either fixed choice, because it takes each one where it wins");
            AddListItem(backendList, "Legacy",
                "WPF vector drawing throughout. The most faithful, and what every existing drawing was authored against — annotation and geometry interleave in creation order. Choose it if anything looks wrong under Auto");
            AddListItem(backendList, "Managed",
                "Always use the built-in software rasterizer for hairline geometry, with text, dimensions and canvas chrome drawn over it. Much faster on large drawings; the trade-off is that annotation always composites above geometry regardless of creation order");
            AddListItem(backendList, "GPU",
                "Direct3D 11. Geometry is uploaded once and a pan or zoom only rewrites the camera, so navigation cost is flat and it is the only backend that holds up at 4K. It fails soft: no suitable device, or a device lost to a driver update or sleep/resume, falls back to a CPU path for the rest of the session with the reason recorded in the crash journal — you will never see an error, only the slower renderer");
            doc.Blocks.Add(backendList);

            // Tips
            AddWelcomeSectionHeader(doc, "Tips");
            var tipsList = new List
            {
                MarkerStyle = TextMarkerStyle.Circle,
                Margin = new Thickness(20, 0, 0, 20)
            };
            AddListItem(tipsList, "Colors", "Use color names (\"Red\", \"Cyan\") or hex codes (\"#FF0000\", \"#80FFFFFF\" for semi-transparent)");
            AddListItem(tipsList, "VizConsole", "Use VizConsole.Log() to output debug messages to the console panel");
            AddListItem(tipsList, "Code Runs on F5", "Nothing runs while you type. Press F5 or click Run when you want the canvas rebuilt - typing never triggers a run, and there is no auto-draw setting (shapes still appear on construction, which is Shape.AutoRegister and is on by default)");
            AddListItem(tipsList, "Auto-Run", "Tick Auto-Run beside the Run button to re-run the project every 500 ms instead of pressing F5 - handy while nudging numbers. It is off by default and is saved with the project rather than globally, so it stays armed for that sketch alone and does not follow you into the next one. A tick that arrives while the previous run is still compiling is dropped rather than queued, and a tick whose source has not changed re-invokes the already-compiled assembly instead of recompiling, which is what keeps the canvas from flickering. Note that static state in your own code is not reset by those re-invokes");
            AddListItem(tipsList, "No Placement Call Needed", "Shapes appear automatically when created - Place() is only for shapes that did not come from a plain `new`");
            AddListItem(tipsList, "Show/Hide Shapes", "Use shape.Hide() and shape.Show() to control visibility without removing from canvas");
            AddListItem(tipsList, "ShapeDefaults", "Set ShapeDefaults.GlobalColor to apply colors to all new shapes");
            AddListItem(tipsList, "Animation", "Create an Animator, add animations with AddToAnimations(...), then call Animate(). There is no Timeline type in the scripting API");
            AddListItem(tipsList, "Per-frame Callbacks", "Frame.Request(t => { ... }) runs a callback on the next frame; re-request from inside it to keep going, stop requesting to end. Use it when a timeline is more ceremony than the job needs");
            AddListItem(tipsList, "Frame Timing", "Press F10 for a frame-timing readout on the canvas. The visible/examined ratio is the number worth watching");
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
            AddListItem(tipsList, "Viewports", "Viewports.Rows = 2; Viewports.Columns = 3; then shape.Place(Viewports[0][2]). 0-based, row first. Size rows and columns the XAML way - Viewports[0].Height = \"3*\". The layout resets to 1x1 on every run, so keep those lines in Main()");
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
