namespace C2VGeometry;

public class VEllipse : Shape, ICurve
{
    public VXYZ Center { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }

    public double StartAngle { get; set; } = 0;
    public double EndAngle { get; set; } = 360;

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



    public override List<ControlPoint> GetControlPoints()
    {
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, Center.X, Center.Y, "Center"),
            new ControlPoint(ControlPointType.Radius, Center.X + RadiusX, Center.Y, "RadiusX"),
            new ControlPoint(ControlPointType.Radius, Center.X, Center.Y + RadiusY, "RadiusY")
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
            case 1:
                RadiusX = Math.Abs(newPosition.X - Center.X);
                break;
            case 2:
                RadiusY = Math.Abs(newPosition.Y - Center.Y);
                break;
        }
    }

    public override VEllipse Clone()
    {
        var clone = new VEllipse(Center.Clone(), RadiusX, RadiusY, StartAngle, EndAngle);
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
    }

    public override void Flip(VLine mirrorLine)
    {
        Center = GeometryHelper.FlipPoint(Center, mirrorLine);
    }

    public override void Scale(VXYZ center, double factor)
    {
        Center = GeometryHelper.ScalePoint(Center, center, factor);
        RadiusX *= Math.Abs(factor);
        RadiusY *= Math.Abs(factor);
    }

    public override BoundingBox GetBounds()
    {
        return new BoundingBox(
            new VXYZ(Center.X - RadiusX, Center.Y - RadiusY),
            new VXYZ(Center.X + RadiusX, Center.Y + RadiusY)
        );
    }

    public override string ToString() => $"VEllipse({Center}, RX:{RadiusX}, RY:{RadiusY}, {StartAngle}-{EndAngle})";

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
    public VXYZ Evaluate(double parameter)
    {
        double angleRad = AngleAtArcFraction(parameter) * Math.PI / 180.0;

        double x = Center.X + RadiusX * Math.Cos(angleRad);
        double y = Center.Y + RadiusY * Math.Sin(angleRad);
        return new VXYZ(x, y);
    }

    /// <summary>
    /// Point at a parameter in [0, 1] interpolated linearly through the sweep <b>angle</b>.
    /// Occasionally what you want (drawing radial spokes, say); <see cref="Evaluate"/> is what you
    /// want for anything spaced along the curve.
    /// </summary>
    public VXYZ EvaluateByAngle(double parameter)
    {
        double angleRad = (StartAngle + (EndAngle - StartAngle) * parameter) * Math.PI / 180.0;
        return new VXYZ(Center.X + RadiusX * Math.Cos(angleRad),
                        Center.Y + RadiusY * Math.Sin(angleRad));
    }

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

    public VXYZ NormalAtPoint(VXYZ p)
    {
        double dx = (p.X - Center.X) / (RadiusX * RadiusX);
        double dy = (p.Y - Center.Y) / (RadiusY * RadiusY);
        return new VXYZ(dx, dy, 0).Normalize();
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

    public double GetLength()
    {
        return Measure(RadiusX / 10.0).Count * (RadiusX / 10.0);
    }

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
        return new VEllipse(Center.Clone(), RadiusX + distance, RadiusY + distance, StartAngle, EndAngle);
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

    public (ICurve, ICurve) SplitAtPoint(VXYZ point)
    {
        VXYZ p = Project(point);
        double nx = (p.X - Center.X) / RadiusX;
        double ny = (p.Y - Center.Y) / RadiusY;
        double angle = Math.Atan2(ny, nx) * 180.0 / Math.PI;
        angle = GeometryHelper.NormalizeAngle(angle);

        return (
             new VEllipse(Center, RadiusX, RadiusY, StartAngle, angle),
             new VEllipse(Center, RadiusX, RadiusY, angle, EndAngle)
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
    public double ParameterAtPoint(VXYZ point)
    {
        double angle = Math.Atan2((point.Y - Center.Y) / RadiusY, (point.X - Center.X) / RadiusX);
        double angleDeg = angle * 180.0 / Math.PI;

        if (angleDeg < 0) angleDeg += 360;

        double sweep = EndAngle - StartAngle;
        if (Math.Abs(sweep) < 1e-10) return 0;

        double relativeAngle = angleDeg - StartAngle;
        while (relativeAngle < 0) relativeAngle += 360;
        while (relativeAngle > 360) relativeAngle -= 360;

        return Math.Clamp(relativeAngle / sweep, 0, 1);
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

        double nx = (point.X - Center.X) / RadiusX;
        double ny = (point.Y - Center.Y) / RadiusY;
        return nx * nx + ny * ny <= 1.0;
    }
}
