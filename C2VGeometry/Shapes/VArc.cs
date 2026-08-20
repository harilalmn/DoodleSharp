namespace C2VGeometry;

public class VArc : Shape, ICurve
{
    public VXYZ Center { get; set; }
    public double Radius { get; set; }
    public double StartAngle { get; set; }  // In degrees
    public double EndAngle { get; set; }    // In degrees

    /// <summary>Gets the start point of the arc.</summary>
    public VXYZ StartPoint => Evaluate(0);

    /// <summary>Gets the end point of the arc.</summary>
    public VXYZ EndPoint => Evaluate(1);

    /// <summary>An arc is never self-intersecting.</summary>
    public bool SelfIntersecting => false;

    /// <summary>Gets the vertices of the arc (center, start point, end point).</summary>
    public List<VXYZ> Vertices => new List<VXYZ> { Center, StartPoint, EndPoint };

    /// <summary>Gets the midpoint of the arc.</summary>
    public VXYZ MidPoint => Evaluate(0.5);

    public VArc(VXYZ center, double radius, double startAngle, double endAngle)
    {
        Center = center;
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        Color = ShapeDefaults.GlobalColor ?? "Orange";
    }

    public VArc(double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        Center = new VXYZ(centerX, centerY);
        Radius = radius;
        StartAngle = startAngle;
        EndAngle = endAngle;
        Color = ShapeDefaults.GlobalColor ?? "Orange";
    }

    /// <summary>
    /// Creates an arc passing through three points.
    /// </summary>
    public VArc(VXYZ start, VXYZ mid, VXYZ end)
    {
        // Check collinearity via determinant (2 * signed area)
        double D = 2 * (start.X * (mid.Y - end.Y) + mid.X * (end.Y - start.Y) + end.X * (start.Y - mid.Y));

        if (GeometryTolerance.IsZero(D))
        {
            throw new ArgumentException("Points are collinear, cannot define a unique arc.");
        }

        // Calculate Center
        double s1 = start.X * start.X + start.Y * start.Y;
        double s2 = mid.X * mid.X + mid.Y * mid.Y;
        double s3 = end.X * end.X + end.Y * end.Y;

        double cx = (s1 * (mid.Y - end.Y) + s2 * (end.Y - start.Y) + s3 * (start.Y - mid.Y)) / D;
        double cy = (s1 * (end.X - mid.X) + s2 * (start.X - end.X) + s3 * (mid.X - start.X)) / D;

        Center = new VXYZ(cx, cy);
        Radius = Center.DistanceTo(start);
        Color = ShapeDefaults.GlobalColor ?? "Orange";

        // Calculate Angles
        double a1 = Math.Atan2(start.Y - cy, start.X - cx) * 180.0 / Math.PI;
        double a2 = Math.Atan2(mid.Y - cy, mid.X - cx) * 180.0 / Math.PI;
        double a3 = Math.Atan2(end.Y - cy, end.X - cx) * 180.0 / Math.PI;

        StartAngle = a1;

        double sweep1, sweep2;

        if (D > 0) // CCW
        {
            sweep1 = NormalizePositive(a2 - a1);
            sweep2 = NormalizePositive(a3 - a2);
        }
        else // CW
        {
            sweep1 = NormalizeNegative(a2 - a1);
            sweep2 = NormalizeNegative(a3 - a2);
        }

        EndAngle = StartAngle + sweep1 + sweep2;
    }

    /// <summary>
    /// Creates an arc from start point, center, and end point.
    /// </summary>
    public static VArc FromStartCenterEnd(VXYZ start, VXYZ center, VXYZ end)
    {
        double radius = center.DistanceTo(start);
        double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X) * 180.0 / Math.PI;
        double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X) * 180.0 / Math.PI;
        return new VArc(new VXYZ(center.X, center.Y), radius, startAngle, endAngle);
    }

    /// <summary>
    /// Creates an arc from center, start point, and end point.
    /// </summary>
    public static VArc FromCenterStartEnd(VXYZ center, VXYZ start, VXYZ end)
    {
        return FromStartCenterEnd(start, center, end);
    }

    /// <summary>
    /// Creates an arc from start point, center, and sweep angle (in degrees).
    /// </summary>
    public static VArc FromStartCenterAngle(VXYZ start, VXYZ center, double sweepAngleDegrees)
    {
        double radius = center.DistanceTo(start);
        double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X) * 180.0 / Math.PI;
        double endAngle = startAngle + sweepAngleDegrees;
        return new VArc(new VXYZ(center.X, center.Y), radius, startAngle, endAngle);
    }

    /// <summary>
    /// Creates an arc from center, start point, and sweep angle (in degrees).
    /// </summary>
    public static VArc FromCenterStartAngle(VXYZ center, VXYZ start, double sweepAngleDegrees)
    {
        return FromStartCenterAngle(start, center, sweepAngleDegrees);
    }

    /// <summary>
    /// Creates an arc from start point, center, and arc length.
    /// </summary>
    public static VArc FromStartCenterLength(VXYZ start, VXYZ center, double arcLength)
    {
        double radius = center.DistanceTo(start);
        double sweepAngleRad = arcLength / radius;
        double sweepAngleDeg = sweepAngleRad * 180.0 / Math.PI;
        return FromStartCenterAngle(start, center, sweepAngleDeg);
    }

    /// <summary>
    /// Creates an arc from center, start point, and arc length.
    /// </summary>
    public static VArc FromCenterStartLength(VXYZ center, VXYZ start, double arcLength)
    {
        return FromStartCenterLength(start, center, arcLength);
    }

    /// <summary>
    /// Creates an arc from start point, end point, and radius.
    /// </summary>
    /// <param name="largeArc">If true, creates the larger arc; otherwise the smaller arc.</param>
    public static VArc FromStartEndRadius(VXYZ start, VXYZ end, double radius, bool largeArc = false)
    {
        double d = start.DistanceTo(end);
        if (d > 2 * radius)
            throw new ArgumentException("Radius too small for the given points.");

        double midX = (start.X + end.X) / 2.0;
        double midY = (start.Y + end.Y) / 2.0;

        double h = Math.Sqrt(radius * radius - (d / 2.0) * (d / 2.0));

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double perpX = -dy / d;
        double perpY = dx / d;

        double cx1 = midX + h * perpX;
        double cy1 = midY + h * perpY;
        double cx2 = midX - h * perpX;
        double cy2 = midY - h * perpY;

        VXYZ center = largeArc ? new VXYZ(cx2, cy2) : new VXYZ(cx1, cy1);

        double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X) * 180.0 / Math.PI;
        double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X) * 180.0 / Math.PI;

        return new VArc(center, radius, startAngle, endAngle);
    }

    /// <summary>
    /// Creates an arc from start point, end point, and sweep angle.
    /// </summary>
    public static VArc FromStartEndAngle(VXYZ start, VXYZ end, double sweepAngleDegrees)
    {
        double chordLength = start.DistanceTo(end);
        double sweepRad = Math.Abs(sweepAngleDegrees) * Math.PI / 180.0;
        double radius = chordLength / (2 * Math.Sin(sweepRad / 2));

        return FromStartEndRadius(start, end, radius, Math.Abs(sweepAngleDegrees) > 180);
    }

    /// <summary>
    /// Creates an arc tangent to a previous curve, continuing from its end point.
    /// </summary>
    public static VArc Continue(ICurve previous, double arcLength)
    {
        var start = previous.EndPoint;
        var tangent = previous.NormalAtPoint(start);
        var direction = new VXYZ(-tangent.Y, tangent.X, 0);

        double radius = arcLength / Math.PI;
        var center = new VXYZ(start.X - direction.X * radius, start.Y - direction.Y * radius);

        return FromStartCenterLength(start, center, arcLength);
    }

    private double NormalizePositive(double angle)
    {
        angle %= 360;
        if (angle <= 0) angle += 360;
        return angle;
    }

    private double NormalizeNegative(double angle)
    {
        angle %= 360;
        if (angle >= 0) angle -= 360;
        return angle;
    }

    /// <summary>
    /// Evaluates a point along the arc at the given normalized parameter.
    /// </summary>
    public VXYZ Evaluate(double parameter)
    {
        double startRad = StartAngle * Math.PI / 180.0;
        double endRad = EndAngle * Math.PI / 180.0;

        double angleRad = startRad + (endRad - startRad) * parameter;

        double x = Center.X + Radius * Math.Cos(angleRad);
        double y = Center.Y + Radius * Math.Sin(angleRad);
        return new VXYZ(x, y);
    }

    public VXYZ NormalAtPoint(VXYZ p)
    {
        return new VXYZ(p.X - Center.X, p.Y - Center.Y, 0).Normalize();
    }

    public double GetLength()
    {
        double angleDiff = Math.Abs(EndAngle - StartAngle);
        return Radius * angleDiff * Math.PI / 180.0;
    }

    public List<VXYZ> Divide(int numberOfSegments)
    {
        var points = new List<VXYZ>();
        if (numberOfSegments <= 0) return points;

        for (int i = 0; i <= numberOfSegments; i++)
        {
            points.Add(Evaluate((double)i / numberOfSegments));
        }
        return points;
    }

    public List<VXYZ> Measure(double segmentLength)
    {
        var points = new List<VXYZ>();
        if (segmentLength <= 0) return points;

        double totalLength = GetLength();
        if (totalLength < 1e-9)
        {
             points.Add(StartPoint);
             return points;
        }

        points.Add(StartPoint);

        double currentLength = segmentLength;
        while (currentLength <= totalLength)
        {
             points.Add(Evaluate(currentLength / totalLength));
             currentLength += segmentLength;
        }

        return points;
    }

    public VXYZ Project(VXYZ point)
    {
        VXYZ cp = point - Center;
        if (cp.IsZeroLength()) cp = new VXYZ(1, 0, 0);

        double angle = Math.Atan2(cp.Y, cp.X) * 180.0 / Math.PI;

        if (!SweepReaches(angle))
        {
            double distStart = GeometryHelper.AngleDifference(angle, StartAngle);
            double distEnd = GeometryHelper.AngleDifference(angle, EndAngle);
            angle = (distStart < distEnd) ? StartAngle : EndAngle;
        }

        double rad = angle * Math.PI / 180.0;
        return new VXYZ(Center.X + Radius * Math.Cos(rad), Center.Y + Radius * Math.Sin(rad));
    }

    /// <summary>
    /// True when the arc's sweep passes through <paramref name="angle"/>.
    /// </summary>
    /// <remarks>
    /// Kept as a name because the projection path reads better for it, but it is no longer a
    /// separate rule: it forwards to <see cref="SweepReaches"/>, and through that to
    /// <see cref="GeometryHelper.SweepContains"/>. It used to normalise all three angles into
    /// [0, 360) and compare them, which cannot distinguish a 20-degree arc written as 350 to 370
    /// from the 340-degree arc that 350 to 10 describes, and got the direction of every clockwise
    /// arc backwards — an arc from 90 to 0 was reported as *not* containing 45.
    /// </remarks>
    private bool IsAngleInArc(double angle) => SweepReaches(angle);

    public VXYZ PointAtSegmentLength(double segmentLength)
    {
        double angleRad = segmentLength / Radius;
        double angleDeg = angleRad * 180.0 / Math.PI;

        double totalSweep = EndAngle - StartAngle;
        double dir = Math.Sign(totalSweep);
        if (dir == 0) dir = 1;

        double targetAngle = StartAngle + dir * angleDeg;

        if (Math.Abs(targetAngle - StartAngle) > Math.Abs(EndAngle - StartAngle))
            targetAngle = EndAngle;

        double rad = targetAngle * Math.PI / 180.0;
        return new VXYZ(Center.X + Radius * Math.Cos(rad), Center.Y + Radius * Math.Sin(rad));
    }

    public ICurve Offset(double distance)
    {
        double newRadius = Radius + distance;
        if (newRadius < 0) newRadius = 0;
        return new VArc(new VXYZ(Center.X, Center.Y), newRadius, StartAngle, EndAngle);
    }

    public List<ICurve> Offset(List<double> distances)
    {
        var result = new List<ICurve>();
        foreach (var d in distances) result.Add(Offset(d));
        return result;
    }

    public List<VXYZ> PointsAtChordLengthFromPoint(VXYZ point, double chordLength)
    {
        var projected = Project(point);
        var points = GeometryHelper.IntersectCircleCircle(Center, Radius, projected, chordLength);

        var results = new List<VXYZ>();
        foreach (var p in points)
        {
            VXYZ cp = p - Center;
            double angle = Math.Atan2(cp.Y, cp.X) * 180.0 / Math.PI;
            if (IsAngleInArc(angle)) results.Add(p);
        }
        return results;
    }

    /// <summary>
    /// Splits the arc at the point on it nearest <paramref name="point"/>, returning the two halves
    /// in sweep order.
    /// </summary>
    /// <remarks>
    /// The split angle is expressed <b>relative to <see cref="StartAngle"/></b> rather than as the
    /// raw <c>Atan2</c> value. <c>Atan2</c> answers in (-180, 180], which need not lie between this
    /// arc's own start and end: splitting an arc written as 350 to 370 at (r, 0) produced the pair
    /// [350, 0] and [0, 370] — two arcs together 36 times longer than the one they replaced.
    /// </remarks>
    public (ICurve, ICurve) SplitAtPoint(VXYZ point)
    {
        var proj = Project(point);
        VXYZ cp = proj - Center;
        double angle = Math.Atan2(cp.Y, cp.X) * 180.0 / Math.PI;
        double splitAngle = StartAngle + RelativeSweepAngle(angle);

        return (
            new VArc(Center, Radius, StartAngle, splitAngle),
            new VArc(Center, Radius, splitAngle, EndAngle)
        );
    }

    /// <summary>
    /// How far <paramref name="angleDegrees"/> lies along this arc's sweep, measured from
    /// <see cref="StartAngle"/> in the direction the arc travels and clamped to the sweep. Signed:
    /// negative for a clockwise arc, so <c>StartAngle + result</c> is always a valid angle on the
    /// arc regardless of how the arc was written.
    /// </summary>
    internal double RelativeSweepAngle(double angleDegrees) =>
        GeometryHelper.SweepOffset(StartAngle, EndAngle, angleDegrees);



    public override List<ControlPoint> GetControlPoints()
    {
        var startPt = StartPoint;
        var endPt = EndPoint;
        double midAngleRad = (StartAngle + EndAngle) / 2.0 * Math.PI / 180.0;
        double radiusHandleX = Center.X + Radius * Math.Cos(midAngleRad);
        double radiusHandleY = Center.Y + Radius * Math.Sin(midAngleRad);

        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, Center.X, Center.Y, "Center"),
            new ControlPoint(ControlPointType.Radius, radiusHandleX, radiusHandleY, "Radius"),
            new ControlPoint(ControlPointType.Vertex, startPt.X, startPt.Y, "Start"),
            new ControlPoint(ControlPointType.Vertex, endPt.X, endPt.Y, "End")
        };
    }

    public override void MoveControlPoint(int index, VXYZ newPosition)
    {
        switch (index)
        {
            case 0: // Move center
                var delta = new VXYZ(newPosition.X - Center.X, newPosition.Y - Center.Y, 0);
                Move(delta);
                break;
            case 1: // Radius handle
                Radius = Center.DistanceTo(newPosition);
                break;
            case 2: // Start point - update start angle
                StartAngle = Math.Atan2(newPosition.Y - Center.Y, newPosition.X - Center.X) * 180.0 / Math.PI;
                Radius = Center.DistanceTo(newPosition);
                break;
            case 3: // End point - update end angle
                EndAngle = Math.Atan2(newPosition.Y - Center.Y, newPosition.X - Center.X) * 180.0 / Math.PI;
                Radius = Center.DistanceTo(newPosition);
                break;
        }
    }

    public override VArc Clone()
    {
        var clone = new VArc(Center.Clone(), Radius, StartAngle, EndAngle);
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        Center = Center + vector;
    }

    /// <summary>
    /// Rotates the arc about <paramref name="pivot"/>: the centre moves and both ends turn by the
    /// same amount, so the sweep is untouched.
    /// </summary>
    /// <remarks>
    /// The two angles are shifted, <b>not</b> normalised. Normalising them independently — which is
    /// what this used to do — folds each into [0, 360) separately, and that silently rewrites any
    /// arc whose sweep crosses zero: 350 degrees to 370 degrees is a 20-degree arc, but normalising
    /// gives 350 to 10, which reads as a 340-degree arc going the other way. Rotating by zero was
    /// enough to turn a short arc into its complement, and every consumer of the sweep — length,
    /// bounds, hit testing, the DXF writer — then agreed on the wrong answer.
    /// </remarks>
    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        Center = GeometryHelper.RotatePoint(Center, pivot, angleDegrees);
        StartAngle += angleDegrees;
        EndAngle += angleDegrees;
    }

    /// <summary>
    /// Mirrors the arc across <paramref name="mirrorLine"/>.
    /// </summary>
    /// <remarks>
    /// Reflecting a direction across a line at angle t maps an angle a to 2t - a, and reflection
    /// reverses orientation, so the ends swap as well: a counter-clockwise arc comes back
    /// clockwise. The previous implementation hardcoded <c>2t - a</c> for <c>t = 0</c> — it mirrored
    /// about the horizontal through the centre no matter which line was passed, so mirroring about
    /// a vertical axis moved the arc's midpoint to the reflection through the *wrong* axis while
    /// the centre moved correctly, leaving the arc facing backwards.
    /// </remarks>
    public override void Flip(VLine mirrorLine)
    {
        Center = GeometryHelper.FlipPoint(Center, mirrorLine);

        double mirrorAngle = Math.Atan2(mirrorLine.End.Y - mirrorLine.Start.Y,
                                        mirrorLine.End.X - mirrorLine.Start.X) * 180.0 / Math.PI;
        double twice = 2 * mirrorAngle;

        double newStart = twice - EndAngle;
        double newEnd = twice - StartAngle;
        StartAngle = newStart;
        EndAngle = newEnd;
    }

    public override void Scale(VXYZ center, double factor)
    {
        Center = GeometryHelper.ScalePoint(Center, center, factor);
        Radius *= Math.Abs(factor);
    }

    /// <summary>
    /// The box that actually contains the arc — its two endpoints, plus whichever of the four
    /// compass extremes (0, 90, 180, 270 degrees) the sweep passes through.
    /// </summary>
    /// <remarks>
    /// This used to return the bounding box of the whole circle, which is correct only for a full
    /// turn and is four times too large for a quarter arc. Everything downstream reads this box:
    /// zoom-to-fit framed a circle that was not there, the cull index reserved space for it, the
    /// selection rectangle claimed the arc when it was nowhere near, and the tiled export sized its
    /// sheet from it.
    /// </remarks>
    public override BoundingBox GetBounds()
    {
        var start = StartPoint;
        var end = EndPoint;

        double minX = Math.Min(start.X, end.X);
        double maxX = Math.Max(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y);
        double maxY = Math.Max(start.Y, end.Y);

        // A compass extreme only widens the box if the sweep actually reaches it.
        for (int quarter = 0; quarter < 4; quarter++)
        {
            double angle = quarter * 90.0;
            if (!SweepReaches(angle)) continue;

            switch (quarter)
            {
                case 0: maxX = Math.Max(maxX, Center.X + Radius); break;
                case 1: maxY = Math.Max(maxY, Center.Y + Radius); break;
                case 2: minX = Math.Min(minX, Center.X - Radius); break;
                default: minY = Math.Min(minY, Center.Y - Radius); break;
            }
        }

        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }

    /// <summary>
    /// True when the arc's sweep passes through <paramref name="angleDegrees"/>, honouring both the
    /// direction of travel and sweeps longer than a full turn.
    /// </summary>
    /// <remarks>
    /// Works on the offset from <see cref="StartAngle"/> rather than on normalised absolute angles,
    /// which is what keeps it right for an arc written as 350 to 370 (or as -10 to 10, or as 0 to
    /// 720). <see cref="IsAngleInArc"/> normalises all three angles and so cannot tell a 20-degree
    /// arc from a 340-degree one; it is kept for the projection path, which is tolerant of that.
    /// </remarks>
    internal bool SweepReaches(double angleDegrees) =>
        GeometryHelper.SweepContains(StartAngle, EndAngle, angleDegrees);

    public override string ToString() => $"VArc(Center: {Center}, R: {Radius}, {StartAngle}° to {EndAngle}°)";

    /// <summary>
    /// Computes the intersection between this arc and another curve.
    /// </summary>
    public IntersectionResult Intersect(ICurve other)
    {
        return CurveIntersection.Intersect(this, other);
    }

    /// <summary>
    /// Returns a point on the arc at the given normalized parameter.
    /// </summary>
    public VXYZ PointAtParameter(double parameter) => Evaluate(parameter);

    /// <summary>
    /// Returns the normalized parameter (0 to 1) for the closest point on the arc to the given point.
    /// </summary>
    /// <remarks>
    /// Measured with <see cref="RelativeSweepAngle"/>, which travels in the arc's own direction.
    /// Folding the offset into [0, 360) first — as this used to — is only right for a counter-
    /// clockwise arc: on an arc from 90 to 0, the midpoint at 45 degrees came back as offset 315
    /// against a sweep of -90 and clamped to parameter 1, so the middle of the arc reported itself
    /// as the end of it.
    /// </remarks>
    public double ParameterAtPoint(VXYZ point)
    {
        double sweep = EndAngle - StartAngle;
        if (Math.Abs(sweep) < GeometryTolerance.Epsilon) return 0;

        double angle = Math.Atan2(point.Y - Center.Y, point.X - Center.X) * 180.0 / Math.PI;
        return Math.Clamp(RelativeSweepAngle(angle) / sweep, 0, 1);
    }

    /// <summary>
    /// Trims this arc in place so that the parameter range [startParameter, endParameter]
    /// becomes the new [0, 1] range. StartAngle and EndAngle are rescaled to span the new range.
    /// </summary>
    public void SetBounds(double startParameter, double endParameter)
    {
        double s = Math.Clamp(startParameter, 0.0, 1.0);
        double e = Math.Clamp(endParameter, 0.0, 1.0);
        if (s > e) (s, e) = (e, s);

        double sweep = EndAngle - StartAngle;
        double newStart = StartAngle + sweep * s;
        double newEnd = StartAngle + sweep * e;
        StartAngle = newStart;
        EndAngle = newEnd;
    }

    /// <summary>
    /// Shortest distance from <paramref name="point"/> to the arc, honouring its sweep: a point
    /// beyond either end measures to the nearer endpoint, not to the full circle.
    /// </summary>
    /// <remarks>
    /// Computed exactly rather than by sampling. Sampling a curve into chords places every chord
    /// slightly inside the true arc, so the distance from the centre of a half-circle came out as
    /// 9.9987 rather than 10 — small, but wrong in a way that compounds in downstream geometry.
    /// </remarks>
    public override double DistanceTo(VXYZ point)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;
        double distanceToCentre = Math.Sqrt(dx * dx + dy * dy);

        // A point exactly at the centre is equidistant from every point of the arc.
        if (distanceToCentre <= GeometryTolerance.Epsilon)
            return Radius;

        // Does the ray from the centre through the point pass through the swept sector? If so the
        // nearest point on the arc is radially in line and the distance is purely radial.
        double startRad = StartAngle * Math.PI / 180.0;
        double endRad = EndAngle * Math.PI / 180.0;
        double sweep = endRad - startRad;
        double angle = Math.Atan2(dy, dx);

        // Express the point's angle as a fraction of the sweep, allowing for either direction and
        // for sweeps that wrap past a full turn.
        double relative = angle - startRad;
        double twoPi = 2 * Math.PI;
        if (sweep >= 0)
        {
            while (relative < 0) relative += twoPi;
            while (relative > twoPi) relative -= twoPi;
            if (relative <= sweep)
                return Math.Abs(distanceToCentre - Radius);
        }
        else
        {
            while (relative > 0) relative -= twoPi;
            while (relative < -twoPi) relative += twoPi;
            if (relative >= sweep)
                return Math.Abs(distanceToCentre - Radius);
        }

        // Outside the sweep: the nearest point is whichever endpoint is closer.
        return Math.Min(point.DistanceTo(StartPoint), point.DistanceTo(EndPoint));
    }

    /// <summary>True when <paramref name="point"/> lies on the arc.</summary>
    public override bool Contains(VXYZ point) => CurveGeometry.IsOnStroke(DistanceTo(point), Radius);
}
