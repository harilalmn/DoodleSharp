namespace C2VGeometry;

/// <summary>
/// An arrow (line with arrowhead at the end).
/// </summary>
public class VArrow : Shape
{
    public VXYZ Start { get; set; }
    public VXYZ End { get; set; }

    /// <summary>Size of the arrowhead (length from tip to base)</summary>
    public double HeadLength { get; set; } = 15;

    /// <summary>Angle of the arrowhead wings in degrees</summary>
    public double HeadAngle { get; set; } = 30;

    /// <summary>Whether to draw arrowhead at start as well</summary>
    public bool DoubleEnded { get; set; } = false;

    public VXYZ MidPoint => new VXYZ((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

    public VArrow(VXYZ start, VXYZ end)
    {
        Start = start;
        End = end;
        Color = ShapeDefaults.GlobalColor ?? "Orange";
    }

    public VArrow(double x1, double y1, double x2, double y2)
    {
        Start = new VXYZ(x1, y1);
        End = new VXYZ(x2, y2);
        Color = ShapeDefaults.GlobalColor ?? "Orange";
    }

    public VArrow(VXYZ startPoint, VXYZ direction, double length)
    {
        Start = startPoint;
        var normalizedDir = direction.Normalize();
        End = new VXYZ(startPoint.X + normalizedDir.X * length, startPoint.Y + normalizedDir.Y * length);
        Color = ShapeDefaults.GlobalColor ?? "Orange";
    }

    /// <summary>
    /// Gets the arrowhead points for the end of the arrow.
    /// </summary>
    public (VXYZ wing1, VXYZ wing2) GetEndArrowhead()
    {
        return GetArrowheadPoints(End, Start);
    }

    /// <summary>
    /// Gets the arrowhead points for the start of the arrow (if double-ended).
    /// </summary>
    public (VXYZ wing1, VXYZ wing2) GetStartArrowhead()
    {
        return GetArrowheadPoints(Start, End);
    }

    /// <summary>
    /// The two wing tips of an arrowhead pointing at <paramref name="tip"/>, coming from
    /// <paramref name="from"/>. Each wing is <see cref="HeadLength"/> long and sits
    /// <see cref="HeadAngle"/> degrees off the shaft, so the head spans twice
    /// <see cref="HeadAngle"/> at the tip.
    ///
    /// <para>
    /// This is the single source of the arrowhead's geometry, and every renderer and exporter must
    /// use it. It used to be duplicated three ways that disagreed: this method and
    /// <c>RenderCanvas.DrawArrow</c> both hard-coded <c>HeadLength / 6</c> (a ≈9.5° half-angle) and
    /// never read <see cref="HeadAngle"/> at all, while <c>ShapeTessellator</c> and the PDF exporter
    /// honoured it — so setting <see cref="HeadAngle"/> did nothing on screen but did change the
    /// raster, GPU and PDF output, and the head was a different width depending on which backend
    /// drew the frame. Same failure as the rotation bug in note 68: per-renderer geometry means a
    /// property can be honoured in one path and silently dropped in another.
    /// </para>
    /// </summary>
    public (VXYZ wing1, VXYZ wing2) GetArrowheadPoints(VXYZ tip, VXYZ from)
        => ArrowheadWings(tip, from, HeadLength, HeadAngle);

    /// <summary>
    /// The wing tips of an arrowhead of the given length and half-angle, pointing at
    /// <paramref name="tip"/> and opening back towards <paramref name="from"/>. Returns
    /// <c>(tip, tip)</c> for a degenerate direction.
    ///
    /// <para>
    /// Static and public because arrowheads are drawn for dimensions and radial dimensions too, at
    /// their own <c>ArrowSize</c>, and those had drifted apart in exactly the same way: the
    /// tessellator drew dimension heads at a hard-coded 20° while the canvas used a fixed
    /// <c>ArrowSize / 6</c> (≈9.5°). Every arrowhead in the application now comes from here.
    /// </para>
    /// </summary>
    public static (VXYZ wing1, VXYZ wing2) ArrowheadWings(
        VXYZ tip, VXYZ from, double headLength, double headAngleDegrees)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (!double.IsFinite(length) || length < GeometryTolerance.Epsilon) return (tip, tip);

        // Normalize direction (pointing from -> tip)
        dx /= length;
        dy /= length;

        // Each wing is the shaft direction rotated by ±headAngle and walked back from the tip.
        double a = headAngleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(a);
        double sin = Math.Sin(a);

        var wing1 = new VXYZ(tip.X - headLength * (dx * cos + dy * sin),
                             tip.Y - headLength * (dy * cos - dx * sin));
        var wing2 = new VXYZ(tip.X - headLength * (dx * cos - dy * sin),
                             tip.Y - headLength * (dy * cos + dx * sin));

        return (wing1, wing2);
    }



    public override List<ControlPoint> GetControlPoints()
    {
        var mid = MidPoint;
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, mid.X, mid.Y, "Center"),
            new ControlPoint(ControlPointType.Vertex, Start.X, Start.Y, "Start"),
            new ControlPoint(ControlPointType.Vertex, End.X, End.Y, "End")
        };
    }

    public override void MoveControlPoint(int index, VXYZ newPosition)
    {
        switch (index)
        {
            case 0:
                var mid = MidPoint;
                var delta = new VXYZ(newPosition.X - mid.X, newPosition.Y - mid.Y, 0);
                Move(delta);
                break;
            case 1:
                Start = new VXYZ(newPosition.X, newPosition.Y);
                break;
            case 2:
                End = new VXYZ(newPosition.X, newPosition.Y);
                break;
        }
    }

    public override VArrow Clone()
    {
        var clone = new VArrow(Start.Clone(), End.Clone())
        {
            HeadLength = HeadLength,
            HeadAngle = HeadAngle,
            DoubleEnded = DoubleEnded
        };
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        Start = Start + vector;
        End = End + vector;
    }

    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        Start = GeometryHelper.RotatePoint(Start, pivot, angleDegrees);
        End = GeometryHelper.RotatePoint(End, pivot, angleDegrees);
    }

    public override void Flip(VLine mirrorLine)
    {
        Start = GeometryHelper.FlipPoint(Start, mirrorLine);
        End = GeometryHelper.FlipPoint(End, mirrorLine);
    }

    public override void Scale(VXYZ center, double factor)
    {
        Start = GeometryHelper.ScalePoint(Start, center, factor);
        End = GeometryHelper.ScalePoint(End, center, factor);
        HeadLength *= Math.Abs(factor);
    }

    public override BoundingBox GetBounds()
    {
        return new BoundingBox(
            new VXYZ(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y)),
            new VXYZ(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y))
        );
    }

    public override string ToString() => $"VArrow({Start} -> {End})";
}
