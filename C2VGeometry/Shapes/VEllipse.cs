namespace C2VGeometry;

public class VEllipse : Shape, ICurve
{
    public VXYZ Center { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    public double StartAngle { get; set; } = 0;
    public double EndAngle { get; set; } = 360;

    /// <summary>
    /// Orientation of the ellipse in degrees, counter-clockwise: the direction its
    /// <see cref="RadiusX"/> axis points. 0 (the default) is the historical axis-aligned ellipse.
    ///
    /// <para>
    /// <see cref="StartAngle"/> and <see cref="EndAngle"/> are measured in the ellipse's <b>own</b>
    /// frame, so turning a half ellipse turns the half with it rather than re-cutting it.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Added because <see cref="Rotate"/> had nothing to write to: it moved the centre and returned,
    /// so rotating an ellipse about its own centre was a silent no-op and rotating it about any
    /// other point sheared the drawing — the ellipse orbited the pivot without turning. Every path
    /// that consumes an ellipse takes a zero-rotation fast path, so an ellipse that has never been
    /// rotated behaves exactly as it always did.
    /// </remarks>
    public double Rotation { get; set; } = 0;

    /// <summary>Gets the area of the ellipse (π * RadiusX * RadiusY).</summary>
    public double Area => Math.PI * RadiusX * RadiusY;

    /// <summary>
    /// Gets the approximate circumference of the ellipse using Ramanujan's formula.
    /// </summary>
    public double Circumference
    {
        get
        {
            double a = RadiusX;
            double b = RadiusY;
            double h = Math.Pow(a - b, 2) / Math.Pow(a + b, 2);
            return Math.PI * (a + b) * (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
        }
    }

    public VEllipse(VXYZ center, double radiusX, double radiusY)
    {
        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
        Color = ShapeDefaults.GlobalColor ?? "Pink";
    }

    public VEllipse(double centerX, double centerY, double radiusX, double radiusY)
    {
        Center = new VXYZ(centerX, centerY);
        RadiusX = radiusX;
        RadiusY = radiusY;
        Color = ShapeDefaults.GlobalColor ?? "Pink";
    }

    public VEllipse(VXYZ center, double radiusX, double radiusY, double startAngle, double endAngle)
        : this(center, radiusX, radiusY)
    {
        StartAngle = startAngle;
        EndAngle = endAngle;
    }



    /// <summary>
    /// The centre plus a handle at the end of each axis.
    /// </summary>
    /// <remarks>
    /// The handles are placed through <see cref="PointAtAngle"/> so they sit on the curve however
    /// the ellipse is turned. Written in world axes -- which is what this did -- a rotated
    /// ellipse's handles floated off the shape entirely.
    /// </remarks>
    public override List<ControlPoint> GetControlPoints()
    {
        var xAxis = PointAtAngle(0);
        var yAxis = PointAtAngle(90);

        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, Center.X, Center.Y, "Center"),
            new ControlPoint(ControlPointType.Radius, xAxis.X, xAxis.Y, "RadiusX"),
            new ControlPoint(ControlPointType.Radius, yAxis.X, yAxis.Y, "RadiusY")
        };
    }

    public override void MoveControlPoint(int index, VXYZ newPosition)
    {
        switch (index)
        {
            case 0:
                var delta = new VXYZ(newPosition.X - Center.X, newPosition.Y - Center.Y, 0);
                Move(delta);
                break;
            // Measured as the distance from the centre rather than along a world axis, so dragging
            // a handle on a rotated ellipse resizes the axis the handle belongs to instead of
            // reading its world-X or world-Y displacement.
            case 1:
                RadiusX = Center.DistanceTo(newPosition);
                break;
            case 2:
                RadiusY = Center.DistanceTo(newPosition);
                break;
        }
    }

    public override VEllipse Clone()
    {
        var clone = new VEllipse(Center.Clone(), RadiusX, RadiusY, StartAngle, EndAngle)
        {
            Rotation = Rotation
        };
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        Center = Center + vector;
    }

    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        Center = GeometryHelper.RotatePoint(Center, pivot, angleDegrees);
        Rotation += angleDegrees;
    }

    /// <summary>
    /// Mirrors the ellipse across <paramref name="mirrorLine"/>.
    /// </summary>
    /// <remarks>
    /// Reflecting across a line at angle t maps a direction a to 2t - a, so the orientation becomes
    /// <c>2t - Rotation</c>. Reflection also reverses the direction of travel, which is why the
    /// sweep is negated about the ellipse's own frame — otherwise mirroring the upper half of an
    /// ellipse would give back the upper half again.
    /// </remarks>
    public override void Flip(VLine mirrorLine)
    {
        Center = GeometryHelper.FlipPoint(Center, mirrorLine);

        double mirrorAngle = Math.Atan2(mirrorLine.End.Y - mirrorLine.Start.Y,
                                        mirrorLine.End.X - mirrorLine.Start.X) * 180.0 / Math.PI;
        Rotation = 2 * mirrorAngle - Rotation;
        (StartAngle, EndAngle) = (-EndAngle, -StartAngle);
    }

    public override void Scale(VXYZ center, double factor)
    {
        Center = GeometryHelper.ScalePoint(Center, center, factor);
        RadiusX *= Math.Abs(factor);
        RadiusY *= Math.Abs(factor);
    }

    /// <summary>
    /// The box that actually contains the ellipse: its two endpoints, plus whichever of the four
    /// axis extremes the sweep passes through — computed in the rotated frame, so it is exact for
    /// a turned ellipse and for a partial one.
    /// </summary>
    /// <remarks>
    /// This used to return the axis-aligned box of the <i>full</i> ellipse, which is wrong twice
    /// over: a half ellipse claimed the space its missing half would have taken, and a rotated one
    /// claimed a box that no longer contained it at all. Zoom-to-fit, the cull index, rubber-band
    /// selection and the tiled export all read this box.
    /// </remarks>
    public override BoundingBox GetBounds()
    {
        var start = PointAtAngle(StartAngle);
        var end = PointAtAngle(EndAngle);

        double minX = Math.Min(start.X, end.X), maxX = Math.Max(start.X, end.X);
        double minY = Math.Min(start.Y, end.Y), maxY = Math.Max(start.Y, end.Y);

        foreach (var angle in ExtremeAngles())
        {
            if (!GeometryHelper.SweepContains(StartAngle, EndAngle, angle)) continue;
            var p = PointAtAngle(angle);
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        return new BoundingBox(new VXYZ(minX, minY), new VXYZ(maxX, maxY));
    }

    /// <summary>
    /// The four ellipse-frame angles at which the world x or y coordinate is stationary — the only
    /// places, besides the endpoints, where a bound can be attained.
    /// </summary>
    /// <remarks>
    /// x(a) = Rx cos(a) cos(t) - Ry sin(a) sin(t) is stationary where tan(a) = -Ry tan(t) / Rx, and
    /// y(a) = Rx cos(a) sin(t) + Ry sin(a) cos(t) where tan(a) = Ry / (Rx tan(t)). Each solution has
    /// a partner half a turn away, giving the minimum as well as the maximum. At t = 0 these
    /// collapse to 0, 90, 180 and 270, which is the axis-aligned case.
    /// </remarks>
    private double[] ExtremeAngles()
    {
        double t = Rotation * Math.PI / 180.0;
        double cos = Math.Cos(t), sin = Math.Sin(t);

        double ax = Math.Atan2(-RadiusY * sin, RadiusX * cos) * 180.0 / Math.PI;
        double ay = Math.Atan2(RadiusY * cos, RadiusX * sin) * 180.0 / Math.PI;

        return new[] { ax, ax + 180.0, ay, ay + 180.0 };
    }

    public override string ToString() => Rotation == 0
        ? $"VEllipse({Center}, RX:{RadiusX}, RY:{RadiusY}, {StartAngle}-{EndAngle})"
        : $"VEllipse({Center}, RX:{RadiusX}, RY:{RadiusY}, {StartAngle}-{EndAngle}, rot {Rotation}°)";

    /// <summary>
    /// The world point at <paramref name="angleDegrees"/> in the ellipse's own frame — the
    /// single place the parametric form and <see cref="Rotation"/> are combined, so nothing can
    /// honour one and forget the other.
    /// </summary>
    public VXYZ PointAtAngle(double angleDegrees)
    {
        double a = angleDegrees * Math.PI / 180.0;
        double lx = RadiusX * Math.Cos(a);
        double ly = RadiusY * Math.Sin(a);

        if (Rotation == 0)
            return new VXYZ(Center.X + lx, Center.Y + ly);

        double r = Rotation * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        return new VXYZ(Center.X + lx * cos - ly * sin,
                        Center.Y + lx * sin + ly * cos);
    }

    /// <summary>
    /// The inverse of <see cref="PointAtAngle"/>: the ellipse-frame angle whose ray through the
    /// centre passes through <paramref name="point"/>. Used to turn a world point back into a
    /// parameter without every caller having to know about <see cref="Rotation"/>.
    /// </summary>
    private double AngleOfPoint(VXYZ point)
    {
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;

        if (Rotation != 0)
        {
            double r = -Rotation * Math.PI / 180.0;   // into the ellipse's frame
            double cos = Math.Cos(r), sin = Math.Sin(r);
            (dx, dy) = (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        double nx = RadiusX == 0 ? 0 : dx / RadiusX;
        double ny = RadiusY == 0 ? 0 : dy / RadiusY;
        return Math.Atan2(ny, nx) * 180.0 / Math.PI;
    }

    /// <summary>
    /// Point at a parameter in [0, 1] along the ellipse, measured by <b>arc length</b>: parameter
    /// 0.5 is the halfway point along the curve, and <see cref="Divide"/> returns evenly spaced
    /// points.
    /// </summary>
    /// <remarks>
    /// This used to interpolate the sweep <i>angle</i> linearly, which is not the same thing on an
    /// eccentric ellipse — equal angles cover less arc near the flat ends, so divisions bunched up
    /// there. Every other <see cref="ICurve"/> is length-parameterised (for a circular
    /// <see cref="VArc"/> angle and arc length are proportional, so it already was), and callers
    /// like <c>Measure</c> and the animation samplers assume it.
    /// </remarks>
    public VXYZ Evaluate(double parameter) => PointAtAngle(AngleAtArcFraction(parameter));

    /// <summary>
    /// Point at a parameter in [0, 1] interpolated linearly through the sweep <b>angle</b>.
    /// Occasionally what you want (drawing radial spokes, say); <see cref="Evaluate"/> is what you
    /// want for anything spaced along the curve.
    /// </summary>
    public VXYZ EvaluateByAngle(double parameter) =>
        PointAtAngle(StartAngle + (EndAngle - StartAngle) * parameter);

    // Cumulative arc-length table over the current sweep, rebuilt whenever the defining values
    // change. A single memo is enough: Divide walks the parameter range in one pass, so every call
    // after the first hits the cache.
    private const int ArcTableSamples = 256;
    private double[]? _arcLengths;
    private double _arcTableRadiusX, _arcTableRadiusY, _arcTableStart, _arcTableEnd;

    private double[] GetArcTable()
    {
        if (_arcLengths != null &&
            _arcTableRadiusX == RadiusX && _arcTableRadiusY == RadiusY &&
            _arcTableStart == StartAngle && _arcTableEnd == EndAngle)
        {
            return _arcLengths;
        }

        var table = new double[ArcTableSamples + 1];
        double startRad = StartAngle * Math.PI / 180.0;
        double sweepRad = (EndAngle - StartAngle) * Math.PI / 180.0;

        double prevX = Center.X + RadiusX * Math.Cos(startRad);
        double prevY = Center.Y + RadiusY * Math.Sin(startRad);
        table[0] = 0;

        for (int i = 1; i <= ArcTableSamples; i++)
        {
            double a = startRad + sweepRad * ((double)i / ArcTableSamples);
            double x = Center.X + RadiusX * Math.Cos(a);
            double y = Center.Y + RadiusY * Math.Sin(a);
            table[i] = table[i - 1] + Math.Sqrt((x - prevX) * (x - prevX) + (y - prevY) * (y - prevY));
            prevX = x;
            prevY = y;
        }

        _arcLengths = table;
        _arcTableRadiusX = RadiusX;
        _arcTableRadiusY = RadiusY;
        _arcTableStart = StartAngle;
        _arcTableEnd = EndAngle;
        return table;
    }

    /// <summary>Sweep angle (degrees) at which <paramref name="fraction"/> of the arc length has been covered.</summary>
    private double AngleAtArcFraction(double fraction)
    {
        var table = GetArcTable();
        double total = table[ArcTableSamples];
        double sweep = EndAngle - StartAngle;

        // Degenerate (zero radii, or no sweep): angle interpolation is all that is meaningful.
        if (total <= GeometryTolerance.Epsilon)
            return StartAngle + sweep * fraction;

        double target = Math.Clamp(fraction, 0.0, 1.0) * total;

        // Binary search for the bracketing samples, then interpolate within that step.
        int lo = 0, hi = ArcTableSamples;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) / 2;
            if (table[mid] <= target) lo = mid; else hi = mid;
        }

        double segment = table[hi] - table[lo];
        double within = segment <= GeometryTolerance.Epsilon ? 0 : (target - table[lo]) / segment;
        double indexFraction = (lo + within) / ArcTableSamples;

        return StartAngle + sweep * indexFraction;
    }

    /// <summary>
    /// The outward normal at <paramref name="p"/>. The gradient is only that simple in the
    /// ellipse's own frame, so the point is taken into that frame and the answer brought back out.
    /// </summary>
    public VXYZ NormalAtPoint(VXYZ p)
    {
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;

        if (Rotation == 0)
            return new VXYZ(dx / (RadiusX * RadiusX), dy / (RadiusY * RadiusY), 0).Normalize();

        double r = Rotation * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);

        double lx = dx * cos + dy * sin;      // into the frame (rotate by -Rotation)
        double ly = -dx * sin + dy * cos;

        double gx = lx / (RadiusX * RadiusX);
        double gy = ly / (RadiusY * RadiusY);

        return new VXYZ(gx * cos - gy * sin, gx * sin + gy * cos, 0).Normalize();
    }

    // ICurve Impl

    public VXYZ Project(VXYZ point)
    {
        VXYZ bestP = Evaluate(0);
        double minD = point.DistanceTo(bestP);

        int steps = 100;
        for (int i = 1; i <= steps; i++)
        {
            VXYZ p = Evaluate((double)i / steps);
            double d = point.DistanceTo(p);
            if (d < minD)
            {
                minD = d;
                bestP = p;
            }
        }

        return bestP;
    }

    public VXYZ PointAtSegmentLength(double segmentLength)
    {
        var points = Measure(segmentLength < 1.0 ? 1.0 : segmentLength / 10.0);
        double dist = 0;
        for(int i=0; i<points.Count-1; i++)
        {
            double d = points[i].DistanceTo(points[i+1]);
            if (dist + d >= segmentLength)
            {
                double rem = segmentLength - dist;
                VXYZ dir = (points[i+1] - points[i]).Normalize();
                return points[i] + dir * rem;
            }
            dist += d;
        }
        return EndPoint;
    }

    /// <summary>
    /// Length of the swept curve.
    /// </summary>
    /// <remarks>
    /// Shares its implementation with the explicit <see cref="ICurve.GetLength"/> below. They used
    /// to differ: this one counted the points <see cref="Measure"/> returned and multiplied by the
    /// step, which overshoots by up to a full step and returns 0 for a zero <see cref="RadiusX"/> —
    /// so <c>ellipse.GetLength()</c> and <c>((ICurve)ellipse).GetLength()</c> gave two different
    /// answers for the same ellipse depending only on the static type at the call site.
    /// </remarks>
    public double GetLength() => GetLengthNumerical();

    private double GetLengthNumerical()
    {
         double len = 0;
         int steps = 100;
         VXYZ prev = Evaluate(0);
         for(int i=1; i<=steps; i++){
             VXYZ curr = Evaluate((double)i/steps);
             len += prev.DistanceTo(curr);
             prev = curr;
         }
         return len;
    }

    double ICurve.GetLength() => GetLengthNumerical();

    public ICurve Offset(double distance)
    {
        return new VEllipse(Center.Clone(), RadiusX + distance, RadiusY + distance, StartAngle, EndAngle)
        {
            Rotation = Rotation
        };
    }

    public List<ICurve> Offset(List<double> distances)
    {
        var list = new List<ICurve>();
        foreach(var d in distances) list.Add(Offset(d));
        return list;
    }

    public List<VXYZ> PointsAtChordLengthFromPoint(VXYZ point, double chordLength)
    {
        var results = new List<VXYZ>();
        int steps = 100;
        VXYZ prev = Evaluate(0);
        double r2 = chordLength;
        VXYZ c2 = Project(point);

        for(int i=1; i<=steps; i++){
             VXYZ curr = Evaluate((double)i/steps);
             double d1 = curr.DistanceTo(c2);
             double d2 = prev.DistanceTo(c2);

             if ((d1 < r2 && d2 > r2) || (d1 > r2 && d2 < r2))
             {
                 results.Add(new VXYZ((curr.X+prev.X)/2, (curr.Y+prev.Y)/2));
             }
             prev = curr;
        }
        return results;
    }

    /// <summary>
    /// Splits at the point on the ellipse nearest <paramref name="point"/>, returning the two halves
    /// in sweep order.
    /// </summary>
    /// <remarks>
    /// The split angle is measured as an offset along the sweep rather than normalised into
    /// [0, 360). Normalising cannot produce an angle that lies between this ellipse's own start and
    /// end unless the sweep happens to be written that way, so splitting a sweep of 350 to 370 — or
    /// any clockwise sweep — produced two pieces that between them covered far more than the
    /// original.
    /// </remarks>
    public (ICurve, ICurve) SplitAtPoint(VXYZ point)
    {
        VXYZ p = Project(point);
        double angle = StartAngle + GeometryHelper.SweepOffset(StartAngle, EndAngle, AngleOfPoint(p));

        return (
             new VEllipse(Center, RadiusX, RadiusY, StartAngle, angle) { Rotation = Rotation },
             new VEllipse(Center, RadiusX, RadiusY, angle, EndAngle) { Rotation = Rotation }
        );
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
        if (segmentLength <= 1e-9) return points;

        double totalLen = GetLengthNumerical();
        int count = (int)(totalLen / segmentLength);
        for(int i=0; i<=count; i++)
        {
             points.Add(Evaluate((double)i * segmentLength / totalLen));
        }
        return points;
    }

    public VXYZ StartPoint => Evaluate(0);
    public VXYZ EndPoint => Evaluate(1);

    /// <summary>An ellipse is never self-intersecting.</summary>
    public bool SelfIntersecting => false;

    /// <summary>Gets the vertices of the ellipse (center point).</summary>
    public List<VXYZ> Vertices => new List<VXYZ> { Center };

    /// <summary>
    /// Computes the intersection between this ellipse and another curve.
    /// </summary>
    public IntersectionResult Intersect(ICurve other)
    {
        return CurveIntersection.Intersect(this, other);
    }

    /// <summary>
    /// Returns a point on the ellipse at the given normalized parameter.
    /// </summary>
    public VXYZ PointAtParameter(double parameter) => Evaluate(parameter);

    /// <summary>
    /// Returns the normalized parameter (0 to 1) for the closest point on the ellipse to the given point.
    /// </summary>
    /// <summary>
    /// Returns the normalized parameter (0 to 1) for the closest point on the ellipse to the given
    /// point.
    /// </summary>
    /// <remarks>
    /// Measured with <see cref="GeometryHelper.SweepOffset"/>, in the ellipse's own frame. It used
    /// to fold the offset into [0, 360) and divide by the sweep, which is the same mistake
    /// <see cref="VArc.ParameterAtPoint"/> was corrected away from and which was simply not carried
    /// across at the time: on an ellipse swept from 90 to 0, the point at 45 degrees reported 0
    /// rather than 0.5. It also read the angle in world axes, so a rotated ellipse answered for a
    /// point that was not the one asked about.
    /// </remarks>
    public double ParameterAtPoint(VXYZ point)
    {
        double sweep = EndAngle - StartAngle;
        if (Math.Abs(sweep) < 1e-10) return 0;

        return Math.Clamp(GeometryHelper.SweepOffset(StartAngle, EndAngle, AngleOfPoint(point)) / sweep, 0, 1);
    }

    /// <summary>
    /// Trims this ellipse in place so that the parameter range [startParameter, endParameter]
    /// becomes the new [0, 1] range. StartAngle and EndAngle are rescaled to span the new range.
    /// </summary>
    public void SetBounds(double startParameter, double endParameter)
    {
        double s = Math.Clamp(startParameter, 0.0, 1.0);
        double e = Math.Clamp(endParameter, 0.0, 1.0);
        if (s > e) (s, e) = (e, s);

        // Trim by arc length, matching Evaluate: SetBounds(0.25, 0.75) has to keep the middle half
        // of the *curve*, not of the sweep angle.
        double newStart = AngleAtArcFraction(s);
        double newEnd = AngleAtArcFraction(e);
        StartAngle = newStart;
        EndAngle = newEnd;
    }

    /// <summary>
    /// Shortest distance from <paramref name="point"/> to the ellipse's curve, by sampling. Honours
    /// the sweep: on a partial ellipse a point past either end measures to the nearer endpoint.
    /// </summary>
    public override double DistanceTo(VXYZ point) => CurveGeometry.DistanceToCurve(point, this);

    /// <summary>
    /// For a full ellipse, whether <paramref name="point"/> is inside it. For a partial sweep — which
    /// encloses no area — whether the point lies on the curve.
    /// </summary>
    public override bool Contains(VXYZ point)
    {
        bool isFullSweep = Math.Abs(Math.Abs(EndAngle - StartAngle) - 360.0) < 1e-9;

        if (!isFullSweep)
            return CurveGeometry.IsOnStroke(DistanceTo(point), Math.Max(RadiusX, RadiusY));

        if (RadiusX <= GeometryTolerance.Epsilon || RadiusY <= GeometryTolerance.Epsilon)
            return false;

        // In the ellipse's OWN frame, because the implicit equation below divides by the radii and
        // therefore only means anything along the ellipse's axes. Reading it in world axes made
        // Contains the one member that did not follow Rotation: on a 100x20 ellipse turned a
        // quarter turn, the point (0, 80) -- plainly inside it -- came back false, while GetBounds,
        // DistanceTo, NormalAtPoint, the ray caster and every renderer all agreed it was inside.
        double dx = point.X - Center.X;
        double dy = point.Y - Center.Y;

        if (Rotation != 0)
        {
            double r = -Rotation * Math.PI / 180.0;
            double cos = Math.Cos(r), sin = Math.Sin(r);
            (dx, dy) = (dx * cos - dy * sin, dx * sin + dy * cos);
        }

        double nx = dx / RadiusX;
        double ny = dy / RadiusY;
        return nx * nx + ny * ny <= 1.0;
    }
}
