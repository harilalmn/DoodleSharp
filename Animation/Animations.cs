using System;
using System.Linq.Expressions;
using System.Reflection;
using C2VGeometry;

namespace DoodleSharp.Animation
{
    /// <summary>
    /// Abstract base class for all animations.
    /// </summary>
    public abstract class Animation
    {
        public Shape? Target { get; }
        public double StartTime { get; internal set; }
        public double Duration { get; }
        public Func<double, double> EasingFunction { get; set; } = EasingFunctions.Linear;

        /// <summary>
        /// Optional name for the animation (e.g., variable name from code).
        /// </summary>
        public string? Name { get; set; }

        protected Animation(Shape target, double duration)
        {
            Target = target;
            Duration = duration;
        }

        /// <summary>
        /// Constructor for animations that don't target a specific Shape (e.g., ObjectPropertyAnimation).
        /// </summary>
        protected Animation(double duration)
        {
            Target = null;
            Duration = duration;
        }

        /// <summary>
        /// Apply the animation at normalized time t (0 to 1).
        /// </summary>
        public abstract void Apply(double t);
    }

    /// <summary>
    /// Animates the DrawFactor property to progressively draw a shape from 0% to 100%.
    /// </summary>
    public class DrawAnimation : Animation
    {
        public DrawAnimation(Shape target, double duration)
            : base(target, duration)
        {
            // Set DrawFactor to 0 so shape starts invisible (including VGroup children)
            SetDrawFactorRecursive(target, 0);
        }

        public override void Apply(double t)
        {
            // Clamp before easing. Timeline.Update deliberately passes a NEGATIVE t to animations
            // whose turn has not come yet (so they can avoid capturing initial state early), and an
            // even-powered easing turns that negative into a positive: EaseInQuad(-0.5) == 0.25.
            // Unclamped, a queued animation therefore applied a quarter of its effect before its
            // start time — a shape appeared part-drawn while earlier animations were still running.
            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            SetDrawFactorRecursive(Target, easedT);
        }

        private static void SetDrawFactorRecursive(Shape shape, double drawFactor)
        {
            shape.DrawFactor = drawFactor;
            if (shape is VGroup group)
            {
                foreach (var child in group.Shapes)
                    SetDrawFactorRecursive(child, drawFactor);
            }
        }
    }

    /// <summary>
    /// Animates moving a shape by a specified vector over time.
    /// </summary>
    public class MoveAnimation : Animation
    {
        private readonly VXYZ _displacement;
        private VXYZ? _initialPosition;
        private bool _hasStarted;

        public MoveAnimation(Shape target, VXYZ displacement, double duration)
            : base(target, duration)
        {
            _displacement = displacement;
        }

        public override void Apply(double t)
        {
            // Only capture initial position when animation actually starts (t >= 0 for first time)
            if (!_hasStarted && t >= 0)
            {
                _initialPosition = new VXYZ(Target.OffsetX, Target.OffsetY, 0);
                _hasStarted = true;
            }

            // Don't apply anything if we haven't started yet
            if (!_hasStarted)
                return;

            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            Target.OffsetX = _initialPosition!.X + _displacement.X * easedT;
            Target.OffsetY = _initialPosition.Y + _displacement.Y * easedT;
        }
    }

    /// <summary>
    /// Animates a shape along any ICurve path (arc, bezier, spline, polyline, etc.).
    /// The shape's center is positioned at the path point for each time step,
    /// so it follows the exact curve from start to end over the duration.
    /// </summary>
    public class PathAnimation : Animation
    {
        private readonly ICurve _path;
        private double _shapeCenterX;
        private double _shapeCenterY;
        private bool _hasStarted;

        public PathAnimation(Shape target, ICurve path, double duration)
            : base(target, duration)
        {
            _path = path;
        }

        public override void Apply(double t)
        {
            // Only capture shape center when animation actually starts (t >= 0 for first time)
            if (!_hasStarted && t >= 0)
            {
                var bounds = Target.GetBounds();
                _shapeCenterX = bounds.Center.X;
                _shapeCenterY = bounds.Center.Y;
                _hasStarted = true;
            }

            // Don't apply anything if we haven't started yet
            if (!_hasStarted)
                return;

            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            VXYZ pathPoint = _path.PointAtParameter(easedT);
            Target.OffsetX = pathPoint.X - _shapeCenterX;
            Target.OffsetY = pathPoint.Y - _shapeCenterY;
        }
    }

    /// <summary>
    /// Animates rotating a shape around a pivot point by a specified angle.
    /// </summary>
    public class RotateAnimation : Animation
    {
        private readonly VXYZ _pivot;
        private readonly double _angleDegrees;
        private double? _initialRotation;
        private bool _hasStarted;

        public RotateAnimation(Shape target, VXYZ pivot, double angleDegrees, double duration)
            : base(target, duration)
        {
            _pivot = pivot;
            _angleDegrees = angleDegrees;
        }

        public override void Apply(double t)
        {
            // Only capture initial rotation when animation actually starts (t >= 0 for first time)
            // Don't capture when being called for future state (t < 0 normalized from timeline)
            if (!_hasStarted && t >= 0)
            {
                _initialRotation = Target.RotationAngle;
                _hasStarted = true;
            }

            // Don't apply anything if we haven't started yet
            if (!_hasStarted)
                return;

            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            Target.RotationAngle = _initialRotation!.Value + _angleDegrees * easedT;
            Target.RotationPivot = _pivot;
        }
    }

    /// <summary>
    /// Animates flipping (mirroring) a shape across a specified axis line.
    /// </summary>
    public class FlipAnimation : Animation
    {
        private readonly VLine _mirrorAxis;
        private double? _initialFlipProgress;
        private bool _hasStarted;

        public FlipAnimation(Shape target, VLine mirrorAxis, double duration)
            : base(target, duration)
        {
            _mirrorAxis = mirrorAxis;
        }

        public override void Apply(double t)
        {
            // Only capture initial flip progress when animation actually starts (t >= 0 for first time)
            if (!_hasStarted && t >= 0)
            {
                _initialFlipProgress = Target.FlipProgress;
                _hasStarted = true;
            }

            // Don't apply anything if we haven't started yet
            if (!_hasStarted)
                return;

            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            Target.FlipProgress = _initialFlipProgress!.Value + (1.0 - _initialFlipProgress.Value) * easedT;
            Target.FlipAxis = _mirrorAxis;
        }
    }

    /// <summary>
    /// Animates one shape morphing (transforming) into another shape over time.
    /// Both shapes' outlines are sampled into matched point sets and interpolated
    /// point-by-point, so e.g. a VLine can smoothly unfurl into a VCircle.
    /// </summary>
    /// <remarks>
    /// The source shape is shown before the transform begins, an internally-managed
    /// morphing outline is shown while it runs, and the destination shape is revealed
    /// (with its own styling/fill) once it completes. The two input shapes are hidden
    /// during the transition so only a single object is ever visible on the canvas.
    /// Curve shapes (VLine, VArc, VCircle, VEllipse, VPolyline, VPolygon, VRectangle,
    /// VBezier, VSpline) are sampled along their geometry; non-curve shapes
    /// (VText, VGroup, etc.) fall back to their bounding-box outline.
    /// </remarks>
    public class TransformAnimation : Animation
    {
        private readonly Shape _from;
        private readonly Shape _to;
        private readonly VXYZ[] _fromPoints;
        private readonly VXYZ[] _toPoints;
        private readonly VPolyline _morph;

        // When morphing a character out of a VText: the source text and character index,
        // blanked to a space the moment this morph starts (not at construction) so the word
        // stays fully visible until it's that character's turn to transform.
        private readonly VText? _sourceText;
        private readonly int _sourceCharIndex;
        private bool _charBlanked;

        /// <summary>
        /// Creates an animation that transforms one shape into another.
        /// </summary>
        /// <param name="from">The shape to morph from (visible before the transform).</param>
        /// <param name="to">The shape to morph into (revealed after the transform).</param>
        /// <param name="duration">Duration of the morph in seconds.</param>
        public TransformAnimation(Shape from, Shape to, double duration)
            : base(from, duration)
        {
            _from = from ?? throw new ArgumentNullException(nameof(from));
            _to = to ?? throw new ArgumentNullException(nameof(to));

            int sampleCount = ComputeSampleCount(from, to);
            _fromPoints = SampleOutline(from, sampleCount);
            _toPoints = SampleOutline(to, sampleCount);

            // The morph proxy is the only thing rendered during the transition.
            var initial = new List<VXYZ>(sampleCount);
            foreach (var p in _fromPoints) initial.Add(p.Clone());
            _morph = new VPolyline(initial);
            // Give it a name so the post-run HideUnnamedShapes pass doesn't strip it
            // (it's created internally, not via a `var x = new V...` declaration).
            _morph.Name = "__transform_morph_" + _morph.Id;
            _morph.Color = from.Color;
            _morph.FillColor = from.FillColor;
            _morph.LineWeight = from.LineWeight;
            _morph.LineType = from.LineType;
            _morph.LineTypeScale = from.LineTypeScale;

            // Start hidden: the source shape stays visible until the morph begins,
            // the destination shape stays hidden until it completes.
            _morph.IsVisible = false;
            _to.IsVisible = false;

            // Make the animation self-sufficient regardless of the "Auto-Draw Shapes"
            // setting (Shape.AutoRegister). The Timeline auto-draws the Target (_from),
            // but the destination shape is never an animation target and the morph
            // proxy is created internally — so neither would reach the canvas when
            // auto-register is off, and nothing would render. Register them explicitly.
            EnsureOnCanvas(_morph);
            EnsureOnCanvas(_to);
            EnsureOnCanvas(_from);
        }

        /// <summary>
        /// Transforms a single character of <paramref name="text"/> (its font outline) into
        /// <paramref name="to"/>. The whole word stays visible; the morph starts from the
        /// character's actual location, and the character is replaced with a space exactly
        /// when this animation begins — so it reads as the character itself transforming.
        /// </summary>
        /// <param name="text">The text to lift a character from (kept visible).</param>
        /// <param name="charIndex">Index of the character to transform.</param>
        /// <param name="to">The shape to morph into.</param>
        /// <param name="duration">Duration of the morph in seconds.</param>
        public TransformAnimation(VText text, int charIndex, Shape to, double duration)
            : this(ExtractGlyphOrThrow(text, charIndex), to, duration)
        {
            _sourceText = text;
            _sourceCharIndex = charIndex;
            // The word still shows this character until the morph begins, so hide the
            // extracted glyph overlay to avoid drawing it twice on top of the word.
            _from.IsVisible = false;
        }

        private static Shape ExtractGlyphOrThrow(VText text, int charIndex)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var glyph = text.ToCharShape(charIndex);
            if (glyph == null)
                throw new ArgumentException(
                    $"Cannot transform character at index {charIndex} of \"{text.Content}\": " +
                    "it has no outline (whitespace, out of range, or no glyph provider set).",
                    nameof(charIndex));
            return glyph;
        }

        public override void Apply(double t)
        {
            // The moment this morph starts, blank the source character so it looks like the
            // character itself is transforming (the rest of the word stays visible). Fires once.
            if (t >= 0 && _sourceText != null && !_charBlanked)
            {
                _sourceText.BlankChar(_sourceCharIndex);
                _charBlanked = true;
            }

            // Before the transform's turn: keep the morph and destination hidden, but
            // leave the source's visibility untouched. (Don't force _from visible here:
            // when transforms are chained — A→B then B→A' — B is the destination of the
            // first and the source of the second, so forcing it visible would make it
            // pop up on top of the first transform's morph.)
            if (t < 0)
            {
                _to.IsVisible = false;
                _morph.IsVisible = false;
                return;
            }

            // Completed: reveal the real destination shape (with its own fill/style)
            // and retire the morph proxy.
            if (t >= 1.0)
            {
                _from.IsVisible = false;
                _morph.IsVisible = false;
                _to.IsVisible = true;
                return;
            }

            // In progress: morph the proxy outline, hide both input shapes.
            double e = EasingFunction(Math.Clamp(t, 0, 1));
            _from.IsVisible = false;
            _to.IsVisible = false;
            _morph.IsVisible = true;

            for (int i = 0; i < _fromPoints.Length; i++)
            {
                var a = _fromPoints[i];
                var b = _toPoints[i];
                _morph.Points[i] = new VXYZ(
                    a.X + (b.X - a.X) * e,
                    a.Y + (b.Y - a.Y) * e,
                    a.Z + (b.Z - a.Z) * e);
            }

            // Swap styling to the destination's at the halfway point so the colour
            // change lands while the geometry is roughly between the two shapes.
            if (e >= 0.5)
            {
                _morph.Color = _to.Color;
                _morph.FillColor = _to.FillColor;
            }
            else
            {
                _morph.Color = _from.Color;
                _morph.FillColor = _from.FillColor;
            }
        }

        /// <summary>
        /// Registers a shape with the canvas if it isn't already, regardless of the
        /// global Shape.AutoRegister flag, so the morph renders even with auto-draw off.
        /// </summary>
        private static void EnsureOnCanvas(Shape shape)
        {
            if (shape != null && !shape.IsPlaced)
                Shape.DefaultRegistry?.Register(shape);
        }

        private static int ComputeSampleCount(Shape from, Shape to)
        {
            int VertexCount(Shape s) =>
                s is ICurve c && c.Vertices != null ? Math.Max(c.Vertices.Count, 2) : 4;
            int verts = Math.Max(VertexCount(from), VertexCount(to));
            return Math.Clamp(verts * 6, 64, 360);
        }

        /// <summary>
        /// Samples a shape's outline into exactly <paramref name="count"/> points,
        /// evenly distributed along its geometry. Closed shapes yield a point set
        /// whose last point coincides with the first.
        /// </summary>
        private static VXYZ[] SampleOutline(Shape shape, int count)
        {
            var points = new VXYZ[count];

            // A group (e.g. a lifted multi-contour glyph like 'O' or 'A') morphs by its
            // dominant outline: sample the child contour with the greatest length.
            if (shape is VGroup group && group.Shapes.Count > 0)
            {
                Shape? dominant = null;
                double bestLength = -1;
                foreach (var child in group.Shapes)
                {
                    double len = child is ICurve cc ? cc.GetLength() : BoundsPerimeter(child);
                    if (len > bestLength) { bestLength = len; dominant = child; }
                }
                if (dominant != null)
                    return SampleOutline(dominant, count);
            }

            if (shape is ICurve curve)
            {
                for (int i = 0; i < count; i++)
                {
                    double p = count == 1 ? 0 : (double)i / (count - 1);
                    points[i] = curve.PointAtParameter(p);
                }
                return points;
            }

            // Fallback for non-curve shapes: trace the bounding-box outline.
            var bounds = shape.GetBounds();
            var loop = new List<VXYZ>
            {
                new VXYZ(bounds.Min.X, bounds.Min.Y),
                new VXYZ(bounds.Max.X, bounds.Min.Y),
                new VXYZ(bounds.Max.X, bounds.Max.Y),
                new VXYZ(bounds.Min.X, bounds.Max.Y),
                new VXYZ(bounds.Min.X, bounds.Min.Y),
            };
            return ResampleByLength(loop, count);
        }

        /// <summary>
        /// Resamples an ordered poly-path into <paramref name="count"/> points spaced
        /// evenly by arc length (endpoints included).
        /// </summary>
        private static VXYZ[] ResampleByLength(List<VXYZ> path, int count)
        {
            var result = new VXYZ[count];

            var cumulative = new double[path.Count];
            cumulative[0] = 0;
            for (int i = 1; i < path.Count; i++)
                cumulative[i] = cumulative[i - 1] + Distance(path[i - 1], path[i]);

            double total = cumulative[path.Count - 1];
            if (total <= 1e-12)
            {
                for (int i = 0; i < count; i++) result[i] = path[0].Clone();
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                double target = (count == 1 ? 0 : (double)i / (count - 1)) * total;
                int seg = 1;
                while (seg < path.Count - 1 && cumulative[seg] < target) seg++;

                double segLen = cumulative[seg] - cumulative[seg - 1];
                double localT = segLen <= 1e-12 ? 0 : (target - cumulative[seg - 1]) / segLen;
                var a = path[seg - 1];
                var b = path[seg];
                result[i] = new VXYZ(
                    a.X + (b.X - a.X) * localT,
                    a.Y + (b.Y - a.Y) * localT,
                    a.Z + (b.Z - a.Z) * localT);
            }
            return result;
        }

        private static double Distance(VXYZ a, VXYZ b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double BoundsPerimeter(Shape shape)
        {
            var b = shape.GetBounds();
            return 2 * ((b.Max.X - b.Min.X) + (b.Max.Y - b.Min.Y));
        }
    }

    /// <summary>
    /// Animates fading in a shape from transparent to opaque.
    /// </summary>
    public class FadeInAnimation : Animation
    {
        public FadeInAnimation(Shape target, double duration)
            : base(target, duration)
        {
            // Set opacity to 0 for fade-in to work (including VGroup children)
            SetOpacityRecursive(target, 0);
        }

        public override void Apply(double t)
        {
            // Clamped: a negative t from the timeline means "not started yet" (see DrawAnimation).
            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            // Fade from 0 to 1
            SetOpacityRecursive(Target, easedT);
        }

        private static void SetOpacityRecursive(Shape shape, double opacity)
        {
            shape.Opacity = opacity;
            if (shape is VGroup group)
            {
                foreach (var child in group.Shapes)
                    SetOpacityRecursive(child, opacity);
            }
        }
    }

    /// <summary>
    /// Animates fading out a shape from opaque to transparent.
    /// </summary>
    public class FadeOutAnimation : Animation
    {
        private double _targetOpacity;

        /// <summary>
        /// Creates a fade out animation.
        /// </summary>
        /// <param name="target">The shape to fade.</param>
        /// <param name="duration">How long the fade takes.</param>
        /// <param name="targetOpacity">The target opacity (default 0 = fully transparent).</param>
        public FadeOutAnimation(Shape target, double duration, double targetOpacity = 0.0)
            : base(target, duration)
        {
            _targetOpacity = targetOpacity;
            // Set opacity to 1 for fade-out to work (including VGroup children)
            SetOpacityRecursive(target, 1);
        }

        public override void Apply(double t)
        {
            // Clamped: a negative t from the timeline means "not started yet" (see DrawAnimation).
            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            // Fade from 1 to target opacity (usually 0)
            double opacity = 1.0 + (_targetOpacity - 1.0) * easedT;
            SetOpacityRecursive(Target, opacity);
        }

        private static void SetOpacityRecursive(Shape shape, double opacity)
        {
            shape.Opacity = opacity;
            if (shape is VGroup group)
            {
                foreach (var child in group.Shapes)
                    SetOpacityRecursive(child, opacity);
            }
        }
    }

    /// <summary>
    /// Animates any numeric (double) property on a shape using an expression to identify the property.
    /// </summary>
    /// <typeparam name="T">The shape type.</typeparam>
    public class ValueAnimation<T> : Animation where T : Shape
    {
        private readonly PropertyInfo _property;
        private readonly double[] _values;

        /// <summary>
        /// Creates a value animation that interpolates a property between start and end values.
        /// </summary>
        /// <param name="target">The shape whose property to animate.</param>
        /// <param name="propertySelector">Expression selecting the property, e.g. c => c.Radius.</param>
        /// <param name="startValue">The value at the beginning of the animation.</param>
        /// <param name="endValue">The value at the end of the animation.</param>
        /// <param name="duration">Duration in seconds.</param>
        public ValueAnimation(T target, Expression<Func<T, double>> propertySelector, double startValue, double endValue, double duration)
            : this(target, propertySelector, new List<double> { startValue, endValue }, duration)
        {
        }

        /// <summary>
        /// Creates a value animation that interpolates a property through a sequence of values.
        /// The values are evenly spaced across the duration.
        /// </summary>
        /// <param name="target">The shape whose property to animate.</param>
        /// <param name="propertySelector">Expression selecting the property, e.g. c => c.Radius.</param>
        /// <param name="values">The sequence of values to animate through. Must contain at least 2 values.</param>
        /// <param name="duration">Duration in seconds.</param>
        public ValueAnimation(T target, Expression<Func<T, double>> propertySelector, List<double> values, double duration)
            : base(target, duration)
        {
            if (values == null || values.Count < 2)
                throw new ArgumentException("values must contain at least 2 elements.", nameof(values));

            _values = values.ToArray();

            // Extract PropertyInfo from the expression
            if (propertySelector.Body is MemberExpression memberExpr &&
                memberExpr.Member is PropertyInfo propInfo)
            {
                _property = propInfo;
            }
            else
            {
                throw new ArgumentException("propertySelector must be a simple property access expression, e.g. c => c.Radius.");
            }

            // Set the initial value
            _property.SetValue(target, _values[0]);
        }

        public override void Apply(double t)
        {
            // Clamped: a negative t from the timeline means "not started yet" (see DrawAnimation).
            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            int segments = _values.Length - 1;
            double scaled = easedT * segments;
            int index = Math.Clamp((int)scaled, 0, segments - 1);
            double localT = scaled - index;
            double value = _values[index] + (_values[index + 1] - _values[index]) * localT;
            _property.SetValue(Target, value);
        }
    }

    /// <summary>
    /// Animates any numeric (double) property on an arbitrary object using an expression to identify the property.
    /// Unlike ValueAnimation, this is not limited to Shape targets.
    /// </summary>
    /// <typeparam name="T">The object type.</typeparam>
    public class ObjectPropertyAnimation<T> : Animation where T : class
    {
        private readonly T _targetObject;
        private readonly PropertyInfo _property;
        private readonly double _startValue;
        private readonly double _endValue;

        /// <summary>
        /// Creates an object property animation that interpolates a property between start and end values.
        /// </summary>
        /// <param name="targetObject">The object whose property to animate.</param>
        /// <param name="propertySelector">Expression selecting the property, e.g. w => w.Rotation.</param>
        /// <param name="startValue">The value at the beginning of the animation.</param>
        /// <param name="endValue">The value at the end of the animation.</param>
        /// <param name="duration">Duration in seconds.</param>
        public ObjectPropertyAnimation(T targetObject, Expression<Func<T, double>> propertySelector, double startValue, double endValue, double duration)
            : base(duration)
        {
            _targetObject = targetObject;
            _startValue = startValue;
            _endValue = endValue;

            // Extract PropertyInfo from the expression
            if (propertySelector.Body is MemberExpression memberExpr &&
                memberExpr.Member is PropertyInfo propInfo)
            {
                _property = propInfo;
            }
            else
            {
                throw new ArgumentException("propertySelector must be a simple property access expression, e.g. w => w.Rotation.");
            }

            // Set the initial value
            _property.SetValue(_targetObject, _startValue);
        }

        public override void Apply(double t)
        {
            // Clamped: a negative t from the timeline means "not started yet" (see DrawAnimation).
            double easedT = EasingFunction(Math.Clamp(t, 0, 1));
            double value = _startValue + (_endValue - _startValue) * easedT;
            _property.SetValue(_targetObject, value);
        }
    }

    /// <summary>
    /// Provides common easing functions.
    /// </summary>
    public static class EasingFunctions
    {
        public static double Linear(double t) => t;

        public static double EaseInQuad(double t) => t * t;

        public static double EaseOutQuad(double t) => t * (2 - t);

        public static double EaseInOutQuad(double t)
        {
            if (t < 0.5)
                return 2 * t * t;
            return -1 + (4 - 2 * t) * t;
        }

        public static double EaseInCubic(double t) => t * t * t;

        public static double EaseOutCubic(double t)
        {
            t--;
            return t * t * t + 1;
        }

        public static double EaseInOutCubic(double t)
        {
            if (t < 0.5)
                return 4 * t * t * t;
            t = 2 * t - 2;
            return (t * t * t + 2) / 2;
        }
    }
}
