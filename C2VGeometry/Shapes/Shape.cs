using System;
using System.Collections.Generic;

namespace C2VGeometry;

/// <summary>
/// Abstract base class for all 2D geometry shapes.
/// Shapes can optionally auto-register with a canvas/rendering system via IShapeRegistry.
/// </summary>
public abstract class Shape : IDrawable
{
    private static long _idCounter = 0;

    /// <summary>
    /// Unique identifier for this shape instance.
    /// </summary>
    public long Id { get; } = System.Threading.Interlocked.Increment(ref _idCounter);

    /// <summary>
    /// Resets the shape ID counter back to 0. Called before each code execution.
    /// </summary>
    public static void ResetIdCounter() => System.Threading.Interlocked.Exchange(ref _idCounter, 0);

    /// <summary>
    /// Optional name/label for this shape.
    /// </summary>
    public string Name { get; set; } = "";

    #region Static Configuration

    /// <summary>
    /// Optional registry for shape auto-registration.
    /// Set this to receive callbacks when shapes are created.
    /// If null, shapes are created without registration (standalone mode).
    /// </summary>
    public static IShapeRegistry? DefaultRegistry { get; set; }

    /// <summary>
    /// When false, shapes will not auto-register with DefaultRegistry on construction.
    /// Use this for algorithms that create many temporary shapes.
    /// Default is true for normal usage.
    /// </summary>
    public static bool AutoRegister { get; set; } = true;

    /// <summary>
    /// Suspends auto-registration until the returned scope is disposed, restoring whatever the
    /// flag was before. For the handful of query methods that have to build a <see cref="Shape"/>
    /// to express their answer — <c>GeometryHelper.IntersectLineLine</c> and friends — so that
    /// asking a question does not leave a shape on the canvas.
    ///
    /// <para>
    /// <b>Not for hot paths.</b> The flag is global and process-wide, so anything on another
    /// thread constructing a shape inside the scope silently fails to register. It is fine for
    /// one-shot user-facing calls; it is not a substitute for a non-registering constructor
    /// (see <c>VLine.Internal</c>) in loops.
    /// </para>
    /// </summary>
    internal static AutoRegisterScope SuspendAutoRegistration() => AutoRegisterScope.Suspend();

    internal readonly struct AutoRegisterScope : IDisposable
    {
        private readonly bool _previous;

        private AutoRegisterScope(bool previous) => _previous = previous;

        internal static AutoRegisterScope Suspend()
        {
            var previous = AutoRegister;
            AutoRegister = false;
            return new AutoRegisterScope(previous);
        }

        public void Dispose() => AutoRegister = _previous;
    }

    /// <summary>
    /// Default stroke color for new shapes.
    /// </summary>
    public static string DefaultColor { get; set; } = "Cyan";

    /// <summary>
    /// Default fill color for new shapes.
    /// </summary>
    public static string DefaultFillColor { get; set; } = "Transparent";

    /// <summary>
    /// Default stroke weight for new shapes.
    /// </summary>
    public static double DefaultLineWeight { get; set; } = 2.0;

    /// <summary>
    /// Default line type for new shapes.
    /// </summary>
    public static LineType DefaultLineType { get; set; } = LineType.Continuous;

    /// <summary>
    /// Default line type scale for new shapes.
    /// </summary>
    public static double DefaultLineTypeScale { get; set; } = 1.0;

    /// <summary>
    /// Resets all static defaults to their initial values.
    /// </summary>
    public static void ResetDefaults()
    {
        DefaultColor = "Cyan";
        DefaultFillColor = "Transparent";
        DefaultLineWeight = 2.0;
        DefaultLineType = LineType.Continuous;
        DefaultLineTypeScale = 1.0;
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Base constructor that auto-registers the shape with the registry (if AutoRegister is true and DefaultRegistry is set).
    /// Shapes are automatically displayed when created - no need to call Draw().
    /// </summary>
    protected Shape()
    {
        Color = ShapeDefaults.GlobalColor ?? DefaultColor;
        FillColor = ShapeDefaults.GlobalFillColor ?? DefaultFillColor;
        LineWeight = ShapeDefaults.GlobalLineWeight ?? DefaultLineWeight;
        LineType = ShapeDefaults.GlobalLineType ?? DefaultLineType;
        LineTypeScale = ShapeDefaults.GlobalLineTypeScale ?? DefaultLineTypeScale;

        // Auto-register with registry if configured
        if (AutoRegister && DefaultRegistry != null)
        {
            DefaultRegistry.Register(this);
        }
    }

    /// <summary>
    /// Protected constructor that allows skipping auto-registration.
    /// Used internally by geometry classes for intermediate calculations.
    /// </summary>
    /// <param name="register">If false, the shape will not be auto-registered with the registry.</param>
    protected Shape(bool register)
    {
        Color = ShapeDefaults.GlobalColor ?? DefaultColor;
        FillColor = ShapeDefaults.GlobalFillColor ?? DefaultFillColor;
        LineWeight = ShapeDefaults.GlobalLineWeight ?? DefaultLineWeight;
        LineType = ShapeDefaults.GlobalLineType ?? DefaultLineType;
        LineTypeScale = ShapeDefaults.GlobalLineTypeScale ?? DefaultLineTypeScale;

        if (register && AutoRegister && DefaultRegistry != null)
        {
            DefaultRegistry.Register(this);
        }
    }

    #endregion

    #region Styling Properties

    /// <summary>
    /// The stroke color name (e.g., "Cyan", "Red", "#FF0000").
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// The fill color name (e.g., "Transparent", "Blue").
    /// </summary>
    public string FillColor { get; set; }

    /// <summary>
    /// The stroke thickness.
    ///
    /// <para>
    /// <b>Device pixels by default</b>, so a stroke keeps the same on-screen width at any zoom.
    /// When the host's <i>Display Line Weight</i> setting is on it is read as <b>world units</b>
    /// instead, so strokes thicken as you zoom in the way a CAD package shows true widths. This doc
    /// used to say "in pixels" flatly, which stopped being true when that setting was added.
    /// </para>
    ///
    /// <para>
    /// It is not a plot width. AutoCAD's lineweight is an ink width in millimetres and its display
    /// is zoom-independent in model space; this is a screen quantity, and the exporters treat it as
    /// one — SVG pins it to device pixels via <c>vector-effect</c>, PDF converts it from DIPs to
    /// points at 96 DPI, and DXF does not carry it at all.
    /// </para>
    /// </summary>
    public double LineWeight { get; set; }

    /// <summary>
    /// The line pattern style (solid, dashed, dotted, etc.).
    /// </summary>
    public LineType LineType { get; set; }

    /// <summary>
    /// Multiplier on the dash and gap lengths of <see cref="LineType"/>. Default is 1.0.
    ///
    /// <para>
    /// The pattern itself is defined in <b>device pixels</b> (see
    /// <c>C2VGeometry.Rendering.LineTypePatterns</c>), and this scales it. Dash lengths are always a
    /// fixed on-screen size: unlike <see cref="LineWeight"/> they do not follow the Display Line
    /// Weight setting, and unlike AutoCAD's <c>LTSCALE</c> they are not measured in drawing units,
    /// so zooming does not change how long a dash looks. They are also independent of
    /// <see cref="LineWeight"/> — a heavy line and a hairline of the same type dash identically.
    /// </para>
    /// </summary>
    public double LineTypeScale { get; set; }

    #endregion

    #region Animation Properties

    /// <summary>
    /// The eight animation values, allocated only once one of them is set away from its default.
    ///
    /// <para>
    /// Almost no shape in a drawing is animated, but every shape used to carry all eight fields —
    /// five doubles and two references, about 56 bytes — whether or not it ever moved. On a
    /// million-shape document that is 56 MB of memory whose only purpose is to hold defaults.
    /// Moving them behind a lazily-allocated object costs one reference on the shape and one null
    /// check per read, and the properties below keep the same names and types, so nothing that
    /// reads or writes them changes at all.
    /// </para>
    /// </summary>
    private sealed class AnimationState
    {
        public double DrawFactor = 1.0;
        public double OffsetX;
        public double OffsetY;
        public double RotationAngle;
        public VXYZ? RotationPivot;
        public double FlipProgress;
        public VLine? FlipAxis;
        public double Opacity = 1.0;
    }

    private AnimationState? _animation;

    /// <summary>Allocates the animation state on first write. Reads never allocate.</summary>
    private AnimationState Animation => _animation ??= new AnimationState();

    /// <summary>
    /// Draw factor for progressive drawing animation (0 = invisible, 1 = fully drawn).
    /// </summary>
    public double DrawFactor
    {
        get => _animation?.DrawFactor ?? 1.0;
        set => Animation.DrawFactor = value;
    }

    /// <summary>
    /// X offset for translation animation.
    /// </summary>
    public double OffsetX
    {
        get => _animation?.OffsetX ?? 0;
        set => Animation.OffsetX = value;
    }

    /// <summary>
    /// Y offset for translation animation.
    /// </summary>
    public double OffsetY
    {
        get => _animation?.OffsetY ?? 0;
        set => Animation.OffsetY = value;
    }

    /// <summary>
    /// Rotation angle in degrees, counter-clockwise. Applied as a render transform about
    /// <see cref="RotationPivot"/> for most shapes, and written by <c>RotateAnimation</c>.
    /// </summary>
    /// <remarks>
    /// Virtual so a shape that rotates by rebuilding its own geometry can hook the setter.
    /// <see cref="VRectangle"/> does exactly that, and used to <c>new</c>-shadow this property
    /// instead — which meant the renderer (holding a <c>VRectangle</c>) read the intrinsic angle
    /// while <c>RotateAnimation</c> (holding a <c>Shape</c>) wrote the animation one, so rotation
    /// animations on rectangles silently did nothing.
    /// </remarks>
    public virtual double RotationAngle
    {
        get => _animation?.RotationAngle ?? 0;
        set => Animation.RotationAngle = value;
    }

    /// <summary>
    /// Pivot point for rotation animation.
    /// </summary>
    public VXYZ? RotationPivot
    {
        get => _animation?.RotationPivot;
        set => Animation.RotationPivot = value;
    }

    /// <summary>
    /// Progress for flip animation (0 = original, 1 = fully flipped).
    /// </summary>
    public double FlipProgress
    {
        get => _animation?.FlipProgress ?? 0;
        set => Animation.FlipProgress = value;
    }

    /// <summary>
    /// Axis line for flip animation.
    /// </summary>
    public VLine? FlipAxis
    {
        get => _animation?.FlipAxis;
        set => Animation.FlipAxis = value;
    }

    /// <summary>
    /// Opacity for fade animation (0 = fully transparent, 1 = fully opaque).
    /// </summary>
    public double Opacity
    {
        get => _animation?.Opacity ?? 1.0;
        set => Animation.Opacity = value;
    }

    #endregion

    #region State Properties

    /// <summary>
    /// Indicates whether this shape has been added to a registry/canvas via Draw().
    /// </summary>
    public bool IsPlaced { get; set; } = false;

    /// <summary>
    /// Indicates whether this shape is visible on the canvas.
    /// Hidden shapes are not rendered but remain in the shape collection.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Indicates whether this shape was explicitly drawn by the user calling .Draw()
    /// </summary>
    public bool IsExplicitlyDrawn { get; set; } = false;

    /// <summary>
    /// Indicates whether this shape is currently selected.
    /// </summary>
    public bool IsSelected { get; set; } = false;

    #endregion

    #region Change tracking

    private uint _revision;

    /// <summary>
    /// Bumped whenever geometry that a cache could depend on changes. A renderer keeps derived data
    /// — flattened curves, generated hatch lines, tessellated region loops — keyed by this, and
    /// recomputes only when it moves.
    ///
    /// <para>
    /// <b>Honest limitation:</b> this tracks *assignment*, not mutation. Vertex collections such as
    /// <c>VPolygon.Points</c> are exposed as mutable lists, so editing one in place changes the
    /// shape without changing its revision, and a cache would go stale. Call
    /// <see cref="Invalidate"/> after any such edit. Encapsulating those collections is the real
    /// fix and is the one genuinely source-breaking change on the roadmap; until then this is the
    /// documented escape hatch rather than a silent trap.
    /// </para>
    /// </summary>
    public uint Revision => _revision;

    /// <summary>
    /// Marks derived data stale. Increment is unchecked on purpose: wrapping is harmless because
    /// the value is only ever compared for equality against a previously stored copy, and a false
    /// match would need 2^32 intervening changes to the same shape.
    /// </summary>
    public void Invalidate()
    {
        unchecked { _revision++; }
    }

    /// <summary>Shorthand for <see cref="Invalidate"/>, for use from derived shapes' setters.</summary>
    protected void BumpRevision()
    {
        unchecked { _revision++; }
    }

    #endregion

    #region Core Methods

    /// <summary>
    /// Puts this shape on the canvas and keeps it there.
    ///
    /// <para>
    /// Most shapes need no such call: constructing one registers it. <c>Place</c> is for the two
    /// cases where that did not happen or is not enough — a shape that arrived <b>unregistered</b>
    /// (the result of a query method such as <c>GeometryHelper.IntersectLineLine</c>, or anything
    /// built while <see cref="AutoRegister"/> was off), and a shape that <b>is</b> registered but
    /// would be swept away after <c>Main()</c> by the unnamed-shape cleanup — the results of
    /// boolean operations and array operations are the usual examples.
    /// </para>
    ///
    /// <para>
    /// It is safe to call on a shape that is already placed, and safe to call twice.
    /// <see cref="Remove"/> is the inverse.
    /// </para>
    /// </summary>
    public void Place()
    {
        IsExplicitlyDrawn = true;
        DefaultRegistry?.Register(this);
    }

    /// <summary>
    /// The historical name for <see cref="Place"/>, and exactly equivalent to it.
    ///
    /// <para>
    /// Kept because it appears throughout existing projects, samples and documentation. New code
    /// should prefer <c>Place()</c>, which says what actually happens: the old name suggested a
    /// rendering call, which this has never been — shapes render because they are registered, not
    /// because anything was "drawn".
    /// </para>
    /// </summary>
    public virtual void Draw() => Place();

    /// <summary>
    /// Removes this shape from the registry — the inverse of <see cref="Place"/>.
    /// </summary>
    public void Remove()
    {
        DefaultRegistry?.Unregister(this);
    }


    /// <summary>
    /// Moves this shape above the specified shape in the draw order (renders on top).
    /// </summary>
    public void BringAbove(Shape otherShape)
    {
        DefaultRegistry?.MoveAbove(this, otherShape);
    }

    /// <summary>
    /// Moves this shape behind the specified shape in the draw order (renders underneath).
    /// </summary>
    public void SendBehind(Shape otherShape)
    {
        DefaultRegistry?.MoveBehind(this, otherShape);
    }

    /// <summary>
    /// Shows this shape on the canvas (sets IsVisible to true).
    /// </summary>
    public void Show()
    {
        IsVisible = true;
    }

    /// <summary>
    /// Hides this shape from the canvas (sets IsVisible to false).
    /// The shape remains in the shape collection but is not rendered.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Creates a deep copy of this shape.
    /// </summary>
    public abstract Shape Clone();

    /// <summary>
    /// Moves this shape by the given vector.
    /// </summary>
    public abstract void Move(VXYZ vector);

    /// <summary>
    /// Rotates this shape around a pivot point by the given angle.
    /// </summary>
    public abstract void Rotate(VXYZ pivot, double angleDegrees);

    /// <summary>
    /// Flips (mirrors) this shape across the given line.
    /// </summary>
    public abstract void Flip(VLine mirrorLine);

    /// <summary>
    /// Scales this shape around a center point.
    /// </summary>
    /// <param name="center">The center point to scale around.</param>
    /// <param name="factor">Scale factor (1.0 = no change, 2.0 = double size).</param>
    public abstract void Scale(VXYZ center, double factor);

    /// <summary>
    /// Gets the bounding box of this shape.
    /// </summary>
    /// <returns>A BoundingBox with Min and Max points defining the axis-aligned bounding box.</returns>
    public abstract BoundingBox GetBounds();

    #endregion

    #region Virtual Methods

    /// <summary>
    /// Gets the control points for interactive editing.
    /// </summary>
    /// <returns>List of control points with their types and positions.</returns>
    public virtual List<ControlPoint> GetControlPoints()
    {
        // Default implementation returns bounding box center
        var bounds = GetBounds();
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, (bounds.Min.X + bounds.Max.X) / 2, (bounds.Min.Y + bounds.Max.Y) / 2, "Center")
        };
    }

    /// <summary>
    /// Moves a control point to a new position.
    /// </summary>
    /// <param name="index">Index of the control point.</param>
    /// <param name="newPosition">New position for the control point.</param>
    public virtual void MoveControlPoint(int index, VXYZ newPosition)
    {
        // Default implementation moves the entire shape
        if (index == 0)
        {
            var bounds = GetBounds();
            var centerX = (bounds.Min.X + bounds.Max.X) / 2;
            var centerY = (bounds.Min.Y + bounds.Max.Y) / 2;
            var delta = new VXYZ(newPosition.X - centerX, newPosition.Y - centerY, 0);
            Move(delta);
        }
    }

    /// <summary>
    /// Calculates the intersection of this shape with another shape.
    /// Returns the resulting Shape (VPoint, VLine, VRectangle, etc.) or null if no intersection.
    ///
    /// <para>
    /// When both shapes are curves this defers to <see cref="CurveIntersection"/> — the same engine
    /// <c>ICurve.Intersect(ICurve)</c> uses. Before that it returned null for every pair the four
    /// overrides did not name (line/line, line/rectangle, rectangle/rectangle, point, group), so
    /// ray-vs-circle, circle-vs-circle and polyline-vs-anything all reported "no intersection"
    /// while <c>ICurve.Intersect</c> on the very same pair returned real points.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="Intersect(ICurve)"/> on the curve types is the richer API</b> and is what to
    /// reach for: it returns an <see cref="IntersectionResult"/> carrying every point and every
    /// overlapping curve, where this can only hand back one shape. A single point comes back as a
    /// <c>VPoint</c>, anything else as a <c>VGroup</c>. Nothing built here is registered — a query
    /// must not draw its own answer — so call <c>Place()</c> on the result to see it.
    /// </para>
    /// </summary>
    public virtual Shape? Intersect(Shape other)
    {
        if (this is ICurve self && other is ICurve otherCurve)
            return ToShape(CurveIntersection.Intersect(self, otherCurve));

        return null;
    }

    /// <summary>
    /// Materialises an <see cref="IntersectionResult"/> as a single shape, without registering
    /// anything: <c>GeometryHelper</c>'s query methods follow the same rule (see the
    /// <c>SuspendAutoRegistration</c> uses there) because asking a question should not litter the
    /// canvas with its answer.
    /// </summary>
    private static Shape? ToShape(IntersectionResult result)
    {
        if (!result.HasIntersection) return null;

        using var _ = SuspendAutoRegistration();

        if (result.IsSinglePoint)
            return new VPoint(result.Points[0]);

        var parts = new List<Shape>(result.Count);
        foreach (var point in result.Points) parts.Add(new VPoint(point));
        foreach (var curve in result.Curves) if (curve is Shape s) parts.Add(s);

        if (parts.Count == 0) return null;
        if (parts.Count == 1) return parts[0];

        return new VGroup(parts);
    }

    /// <summary>
    /// Checks if this shape intersects with another shape.
    /// </summary>
    public virtual bool DoesIntersect(Shape other)
    {
        // Asked of the curve engine directly rather than through Intersect(Shape): the answer is a
        // boolean, and this is written inside loops over every shape in a scene, so materialising
        // VPoint/VGroup results only to throw them away would be pure waste.
        if (this is ICurve self && other is ICurve otherCurve)
            return CurveIntersection.Intersect(self, otherCurve).HasIntersection;

        if (Intersect(other) != null) return true;
        // VText has a custom DoesIntersect (OBB-vs-AABB SAT); delegate so the check is symmetric.
        if (other is VText) return other.DoesIntersect(this);
        return false;
    }

    /// <summary>
    /// Calculates the minimum distance from this shape to a point.
    /// </summary>
    public virtual double DistanceTo(VXYZ point)
    {
        // Default implementation uses bounding box center
        var bounds = GetBounds();
        var centerX = (bounds.Min.X + bounds.Max.X) / 2;
        var centerY = (bounds.Min.Y + bounds.Max.Y) / 2;
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Checks if a point is inside this shape (for filled shapes).
    /// </summary>
    public virtual bool Contains(VXYZ point)
    {
        // Default implementation checks bounding box
        var bounds = GetBounds();
        return point.X >= bounds.Min.X && point.X <= bounds.Max.X &&
               point.Y >= bounds.Min.Y && point.Y <= bounds.Max.Y;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Copies this shape's styling — <see cref="Color"/>, <see cref="FillColor"/>,
    /// <see cref="LineWeight"/>, <see cref="LineType"/> and <see cref="LineTypeScale"/> — onto
    /// another shape. Geometry, name, id and visibility are not touched.
    ///
    /// <para>
    /// This existed as a <c>protected</c> helper for <c>Clone()</c> implementations and was public
    /// only in the documentation. It is genuinely useful from user code — restyling a boolean
    /// result to match its input is the common case — so it is now public in fact as well.
    /// </para>
    /// </summary>
    /// <param name="target">The shape to restyle. Null and self are no-ops.</param>
    /// <returns><paramref name="target"/>, so the call can be chained.</returns>
    public Shape? CopyStyleTo(Shape? target)
    {
        if (target == null || ReferenceEquals(target, this)) return target;

        target.Color = Color;
        target.FillColor = FillColor;
        target.LineWeight = LineWeight;
        target.LineType = LineType;
        target.LineTypeScale = LineTypeScale;

        return target;
    }

    #endregion
}
