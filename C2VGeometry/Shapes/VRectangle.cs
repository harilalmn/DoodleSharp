using System;
using System.Collections.Generic;

namespace C2VGeometry;

public class VRectangle : VPolygon
{
    private VXYZ _corner;
    private double _width;
    private double _height;
    private double _rotationAngle;

    public VXYZ Corner
    {
        get => _corner;
        set
        {
            _corner = value;
            UpdatePoints();
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            _width = value;
            UpdatePoints();
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            _height = value;
            UpdatePoints();
        }
    }

    /// <summary>
    /// Rotation angle in degrees (counter-clockwise), about the rectangle's centre.
    /// </summary>
    /// <remarks>
    /// Overrides <see cref="Shape.RotationAngle"/> rather than shadowing it. A rectangle rotates by
    /// rebuilding its four corners, so setting this recomputes the geometry; because it is now a
    /// genuine override, <c>RotateAnimation</c> — which writes through a <c>Shape</c> reference —
    /// reaches the same property, and rotating a rectangle by animation works. While this was a
    /// <c>new</c> member, the writer and the reader resolved to two different properties and the
    /// animation had no visible effect.
    /// </remarks>
    public override double RotationAngle
    {
        get => _rotationAngle;
        set
        {
            _rotationAngle = value;
            UpdatePoints();
        }
    }

    public VRectangle(VXYZ corner, double width, double height)
        : base(ComputeCorners(corner, width, height, 0))
    {
        _corner = corner;
        _width = width;
        _height = height;
        _rotationAngle = 0;
        Color = ShapeDefaults.GlobalColor ?? "Magenta";
        FillColor = ShapeDefaults.GlobalFillColor ?? "Transparent";
    }

    public VRectangle(double x, double y, double width, double height)
        : this(new VXYZ(x, y), width, height)
    {
    }

    /// <summary>
    /// Creates a rectangle from two corner points (bottom-left and top-right).
    /// </summary>
    public VRectangle(VXYZ bottomLeft, VXYZ topRight)
        : this(bottomLeft, topRight.X - bottomLeft.X, topRight.Y - bottomLeft.Y)
    {
    }

    private static VXYZ[] ComputeCorners(VXYZ corner, double width, double height, double rotationAngle)
    {
        var p0 = new VXYZ(corner.X, corner.Y);
        var p1 = new VXYZ(corner.X + width, corner.Y);
        var p2 = new VXYZ(corner.X + width, corner.Y + height);
        var p3 = new VXYZ(corner.X, corner.Y + height);

        if (Math.Abs(rotationAngle) >= 1e-9)
        {
            var center = new VXYZ(corner.X + width / 2, corner.Y + height / 2);

            p0 = GeometryHelper.RotatePoint(p0, center, rotationAngle);
            p1 = GeometryHelper.RotatePoint(p1, center, rotationAngle);
            p2 = GeometryHelper.RotatePoint(p2, center, rotationAngle);
            p3 = GeometryHelper.RotatePoint(p3, center, rotationAngle);
        }

        return new[] { p0, p1, p2, p3 };
    }

    private void UpdatePoints()
    {
        var corners = ComputeCorners(_corner, _width, _height, _rotationAngle);
        Points.Clear();
        Points.AddRange(corners);
        BuildCurvesFromPoints();
    }

    public override List<ControlPoint> GetControlPoints()
    {
        double cx = _corner.X + _width / 2;
        double cy = _corner.Y + _height / 2;
        return new List<ControlPoint>
        {
            new ControlPoint(ControlPointType.Move, cx, cy, "Center"),
            new ControlPoint(ControlPointType.Vertex, _corner.X, _corner.Y, "Corner"),
            new ControlPoint(ControlPointType.Vertex, _corner.X + _width, _corner.Y + _height, "Opposite")
        };
    }

    public override void MoveControlPoint(int index, VXYZ newPosition)
    {
        switch (index)
        {
            case 0: // Move center
                double cx = _corner.X + _width / 2;
                double cy = _corner.Y + _height / 2;
                var delta = new VXYZ(newPosition.X - cx, newPosition.Y - cy, 0);
                Move(delta);
                break;
            case 1: // Bottom-left corner - resize keeping opposite corner fixed
                double oppX = _corner.X + _width;
                double oppY = _corner.Y + _height;
                _corner = new VXYZ(Math.Min(newPosition.X, oppX), Math.Min(newPosition.Y, oppY));
                _width = Math.Abs(oppX - newPosition.X);
                _height = Math.Abs(oppY - newPosition.Y);
                UpdatePoints();
                break;
            case 2: // Top-right corner - resize keeping corner fixed
                _width = Math.Abs(newPosition.X - _corner.X);
                _height = Math.Abs(newPosition.Y - _corner.Y);
                if (newPosition.X < _corner.X)
                    _corner = new VXYZ(newPosition.X, _corner.Y);
                if (newPosition.Y < _corner.Y)
                    _corner = new VXYZ(_corner.X, newPosition.Y);
                UpdatePoints();
                break;
        }
    }

    public override VRectangle Clone()
    {
        var clone = new VRectangle(_corner.Clone(), _width, _height);
        clone._rotationAngle = _rotationAngle;
        clone.UpdatePoints();
        CopyStyleTo(clone);
        return clone;
    }

    public override void Move(VXYZ vector)
    {
        _corner = _corner + vector;
        UpdatePoints();
    }

    /// <summary>
    /// The centre of the rectangle — the point <see cref="ComputeCorners"/> rotates its four
    /// corners about, and therefore the only point on the shape that a transform can be applied to
    /// directly.
    /// </summary>
    /// <remarks>
    /// <see cref="Corner"/> is the <b>unrotated</b> bottom-left: an artefact of how the rectangle is
    /// parameterised, not a point that stays put as the shape turns. Transforming it and rebuilding
    /// the box from it is what broke <see cref="Rotate"/> and <see cref="Flip"/> below.
    /// </remarks>
    private VXYZ Centre => new VXYZ(_corner.X + _width / 2, _corner.Y + _height / 2);

    /// <summary>Moves the rectangle so its centre lands on <paramref name="centre"/>.</summary>
    private void SetCentre(VXYZ centre) =>
        _corner = new VXYZ(centre.X - _width / 2, centre.Y - _height / 2);

    /// <summary>
    /// Rotates the rectangle about <paramref name="pivot"/>: the centre travels, and the rectangle
    /// turns by the same amount about its new centre.
    /// </summary>
    /// <remarks>
    /// This used to rotate <see cref="Corner"/> and rebuild the box from it, which is wrong for
    /// every pivot including the rectangle's own centre — the rebuilt box grows from the rotated
    /// corner in unrotated axes, so its centre ends up somewhere else entirely. A 10x4 rectangle at
    /// (2, 1) turned a quarter turn about the origin landed with its corners at (6, -1)..(2, 9)
    /// instead of (-1, 2)..(-5, 12): correctly oriented, and nowhere near where it belonged.
    /// </remarks>
    public override void Rotate(VXYZ pivot, double angleDegrees)
    {
        SetCentre(GeometryHelper.RotatePoint(Centre, pivot, angleDegrees));
        _rotationAngle += angleDegrees;
        UpdatePoints();
    }

    /// <summary>
    /// Mirrors the rectangle across <paramref name="mirrorLine"/>.
    /// </summary>
    /// <remarks>
    /// Two separate faults, both from transforming <see cref="Corner"/>. The rotation was left
    /// alone, so a rectangle drawn at 30 degrees came back still at 30 rather than at its mirror
    /// image; and even an unrotated one landed in the wrong place, because the mirrored corner is
    /// the mirror of the bottom-left, and the box was then grown to the right and upward from it —
    /// a rectangle spanning x from 2 to 12, mirrored about the Y axis, came back spanning -2 to 8
    /// instead of -12 to -2.
    ///
    /// <para>
    /// Reflecting across a line at angle t maps a direction a to 2t - a, so the orientation becomes
    /// <c>2t - RotationAngle</c>. The height axis of the mirrored box points the opposite way from
    /// the one that convention rebuilds, but a rectangle is symmetric about its centre, so the two
    /// describe the same four corners.
    /// </para>
    /// </remarks>
    public override void Flip(VLine mirrorLine)
    {
        SetCentre(GeometryHelper.FlipPoint(Centre, mirrorLine));

        double mirrorAngle = Math.Atan2(mirrorLine.End.Y - mirrorLine.Start.Y,
                                        mirrorLine.End.X - mirrorLine.Start.X) * 180.0 / Math.PI;

        // Folded into [0, 180) because a rectangle is symmetric about its centre, so t and t + 180
        // describe the same four corners. Without it, mirroring an axis-aligned rectangle about an
        // axis reports a rotation of 180 for a rectangle that is plainly still square to the page —
        // right shape, alarming number.
        double turned = (2 * mirrorAngle - _rotationAngle) % 180.0;
        if (turned < 0) turned += 180.0;
        _rotationAngle = turned;

        UpdatePoints();
    }

    public override void Scale(VXYZ center, double factor)
    {
        _corner = GeometryHelper.ScalePoint(_corner, center, factor);
        _width *= Math.Abs(factor);
        _height *= Math.Abs(factor);
        UpdatePoints();
    }

    public override bool Contains(VXYZ point)
    {
        // For axis-aligned check (no rotation), use simple bounds
        if (Math.Abs(_rotationAngle) < 1e-9)
        {
            return point.X >= _corner.X && point.X <= _corner.X + _width &&
                   point.Y >= _corner.Y && point.Y <= _corner.Y + _height;
        }
        // Otherwise, use polygon containment from base class
        return IsPointInPolygon(point);
    }

    private bool IsPointInPolygon(VXYZ point)
    {
        // Ray casting algorithm
        bool inside = false;
        int j = Points.Count - 1;

        for (int i = 0; i < Points.Count; i++)
        {
            if ((Points[i].Y > point.Y) != (Points[j].Y > point.Y) &&
                point.X < (Points[j].X - Points[i].X) * (point.Y - Points[i].Y) / (Points[j].Y - Points[i].Y) + Points[i].X)
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    public override Shape? Intersect(Shape other)
    {
        if (other is VRectangle otherRect)
        {
            return GeometryHelper.IntersectRectRect(this, otherRect);
        }
        else if (other is VLine line)
        {
            return GeometryHelper.IntersectLineRect(line, this);
        }
        return base.Intersect(other);
    }

    public override string ToString() => $"VRectangle({_corner}, W:{_width}, H:{_height})";

    public new double GetLength()
    {
        return 2 * (Math.Abs(_width) + Math.Abs(_height));
    }

    public new ICurve Offset(double distance)
    {
        if (Math.Abs(_rotationAngle) < 1e-9)
        {
            double newWidth = _width + 2 * distance;
            double newHeight = _height + 2 * distance;
            return new VRectangle(
                new VXYZ(_corner.X - distance, _corner.Y - distance),
                newWidth, newHeight
            );
        }
        return base.Offset(distance);
    }

    public new List<ICurve> Offset(List<double> distances)
    {
        var list = new List<ICurve>();
        foreach (var d in distances) list.Add(Offset(d));
        return list;
    }
}
