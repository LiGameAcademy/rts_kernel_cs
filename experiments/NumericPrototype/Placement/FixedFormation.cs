using FixPointCS;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal enum FixedFormationKind { Compact, Rectangle, Wedge, Circle }

internal static class FixedFormation
{
    internal static FixedPoint[] Create(FixedFormationKind kind, int count, long spacing,
        FixedPoint anchor, long heading)
    {
        if (!Enum.IsDefined(kind) || count is < 1 or > 500 || spacing <= 0 || spacing > 256 * FixedMath.One)
            throw new ArgumentException("Experimental formation dimensions outside audited range.");
        var local = kind switch
        {
            FixedFormationKind.Compact => Compact(count, spacing),
            FixedFormationKind.Rectangle => Rectangle(count, spacing),
            FixedFormationKind.Wedge => Wedge(count, spacing),
            FixedFormationKind.Circle => Circle(count, spacing),
            _ => throw new ArgumentException("Unknown formation.")
        };
        var angle = FixedMath.Normalize(heading);
        if (angle == 0) return local.Select(point => anchor + point).ToArray();
        var cosine = Fixed64.Cos(angle);
        var sine = Fixed64.Sin(angle);
        // One widened accumulation followed by one truncation toward zero per rotated coordinate.
        return local.Select(point => new FixedPoint(
            checked(anchor.X + (long)(((Int128)point.X * cosine - (Int128)point.Y * sine) / FixedMath.One)),
            checked(anchor.Y + (long)(((Int128)point.X * sine + (Int128)point.Y * cosine) / FixedMath.One)))).ToArray();
    }

    private static FixedPoint[] Compact(int count, long spacing)
    {
        var points = new List<FixedPoint> { default };
        var rootThree = PrototypeScalar.Sqrt(PrototypeScalar.FromInt(3)).Raw;
        (int X, int Y)[] directions = [(1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1)];
        for (var ring = 1; points.Count < count; ring++)
        {
            var x = 0;
            var y = -ring;
            foreach (var direction in directions)
                for (var step = 0; step < ring && points.Count < count; step++)
                {
                    points.Add(new(checked((long)((Int128)spacing * (2 * x + y) / 2)),
                        checked((long)((Int128)spacing * y * rootThree / (2 * FixedMath.One)))));
                    x += direction.X;
                    y += direction.Y;
                }
        }
        return points.ToArray();
    }

    private static FixedPoint[] Rectangle(int count, long spacing)
    {
        var columns = 1;
        while (columns * columns < count) columns++;
        if (columns % 2 == 0) columns++;
        return Enumerable.Range(0, count).Select(index =>
        {
            var column = index % columns;
            var lateral = column == 0 ? 0 : (column + 1) / 2 * (column % 2 == 1 ? -1 : 1);
            return new FixedPoint(-(index / columns) * spacing, lateral * spacing);
        }).ToArray();
    }

    private static FixedPoint[] Wedge(int count, long spacing) => Enumerable.Range(0, count).Select(index =>
        index == 0 ? default : new FixedPoint(-((index + 1) / 2) * spacing,
            ((index + 1) / 2) * spacing * (index % 2 == 1 ? -1 : 1))).ToArray();

    private static FixedPoint[] Circle(int count, long spacing)
    {
        var points = new FixedPoint[count];
        if (count == 1) return points;
        var followers = count - 1;
        var radius = followers < 3 ? spacing : Math.Max(spacing,
            FixedMath.Div(spacing, 2 * Fixed64.Sin(Fixed64.Pi / followers)));
        for (var index = 0; index < followers; index++)
        {
            var angle = FixedMath.Normalize(checked((long)((Int128)Fixed64.Pi2 * index / followers)));
            points[index + 1] = new(FixedMath.Mul(Fixed64.Cos(angle), radius), FixedMath.Mul(Fixed64.Sin(angle), radius));
        }
        return points;
    }
}
