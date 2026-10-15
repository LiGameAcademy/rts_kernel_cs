namespace Rts.Kernel.Navigation;

public enum FormationKind { Compact = 0, Rectangle = 1, Wedge = 2, Circle = 3 }

/// <summary>Ideal geometry only; world placement must check navigation and body separation.</summary>
public static class FormationLayout
{
    public const int MaximumMembers = 500;

    public static IReadOnlyList<FixedVector2> Create(FormationKind kind, int count, SimScalar spacing,
        FixedVector2 anchor, SimScalar heading)
    {
        if (!Enum.IsDefined(kind) || count is < 1 or > MaximumMembers || !SimulationDomain.Contains(spacing) || spacing <= 0
            || !SimulationDomain.Contains(anchor.X) || !SimulationDomain.Contains(anchor.Y))
            throw new ArgumentException("Invalid formation geometry.");
        var local = kind switch
        {
            FormationKind.Compact => Compact(count, spacing),
            FormationKind.Rectangle => Rectangle(count, spacing),
            FormationKind.Wedge => Wedge(count, spacing),
            FormationKind.Circle => Circle(count, spacing),
            _ => throw new ArgumentException("Unknown formation."),
        };
        var cos = SimMath.Cos(heading);
        var sin = SimMath.Sin(heading);
        var result = local.Select(p => new FixedVector2(anchor.X + p.X * cos - p.Y * sin,
            anchor.Y + p.X * sin + p.Y * cos)).ToArray();
        if (result.Any(p => !SimulationDomain.Contains(p.X) || !SimulationDomain.Contains(p.Y)))
            throw new ArgumentException("Formation geometry overflow.");
        return Array.AsReadOnly(result);
    }

    private static List<FixedVector2> Compact(int count, SimScalar spacing)
    {
        var points = new List<FixedVector2> { FixedVector2.Zero };
        var directions = new (int X, int Y)[] { (1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1) };
        for (var ring = 1; points.Count < count; ring++)
        {
            var x = 0;
            var y = -ring;
            foreach (var direction in directions)
            for (var step = 0; step < ring && points.Count < count; step++)
            {
                points.Add(new FixedVector2(spacing * (x + (SimScalar)y / 2), spacing * SimMath.Sqrt(3) * y / 2));
                x += direction.X;
                y += direction.Y;
            }
        }
        return points;
    }

    private static List<FixedVector2> Rectangle(int count, SimScalar spacing)
    {
        var columns = 1;
        while (columns * columns < count) columns++;
        if (columns % 2 == 0) columns++;
        var points = new List<FixedVector2>();
        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var lateral = column == 0 ? 0 : (column + 1) / 2 * (column % 2 == 1 ? -1 : 1);
            points.Add(new FixedVector2(-(i / columns) * spacing, lateral * spacing));
        }
        return points;
    }

    private static List<FixedVector2> Wedge(int count, SimScalar spacing)
    {
        var points = new List<FixedVector2> { FixedVector2.Zero };
        for (var i = 1; i < count; i++)
        {
            var row = (i + 1) / 2;
            points.Add(new FixedVector2(-row * spacing, row * spacing * (i % 2 == 1 ? -1 : 1)));
        }
        return points;
    }

    private static List<FixedVector2> Circle(int count, SimScalar spacing)
    {
        var points = new List<FixedVector2> { FixedVector2.Zero };
        if (count == 1) return points;
        var followers = count - 1;
        var radius = followers < 3 ? spacing : SimMath.Max(spacing, spacing / (2 * SimMath.Sin(SimMath.Pi / followers)));
        for (var i = 0; i < followers; i++)
        {
            var angle = 2 * SimMath.Pi * i / followers;
            points.Add(new FixedVector2(SimMath.Cos(angle) * radius, SimMath.Sin(angle) * radius));
        }
        return points;
    }

    /// <summary>Minimum total squared ideal-slot distance, with one explicit anchor. Equal costs are stable.</summary>
    public static IReadOnlyList<int> Match(IReadOnlyList<FixedVector2> positions,
        IReadOnlyList<FixedVector2> slots, int anchorIndex)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(slots);
        if (positions.Count != slots.Count || positions.Count is < 1 or > MaximumMembers
            || anchorIndex < 0 || anchorIndex >= positions.Count)
            throw new ArgumentException("Invalid matching members or anchor.");
        return Array.AsReadOnly(SlotMatching.Assign(positions, slots, anchorIndex));
    }
}
