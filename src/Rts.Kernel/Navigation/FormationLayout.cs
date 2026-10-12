namespace Rts.Kernel.Navigation;

public enum FormationKind { Compact = 0, Rectangle = 1, Wedge = 2, Circle = 3 }

/// <summary>Ideal geometry only; world placement must check navigation and body separation.</summary>
public static class FormationLayout
{
    public const int MaximumMembers = 500;

    public static IReadOnlyList<SimVector2> Create(FormationKind kind, int count, double spacing,
        SimVector2 anchor, double heading)
    {
        if (!Enum.IsDefined(kind) || count is < 1 or > MaximumMembers || !double.IsFinite(spacing) || spacing <= 0
            || !double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || !double.IsFinite(heading))
            throw new ArgumentException("Invalid formation geometry.");
        var local = kind switch
        {
            FormationKind.Compact => Compact(count, spacing),
            FormationKind.Rectangle => Rectangle(count, spacing),
            FormationKind.Wedge => Wedge(count, spacing),
            FormationKind.Circle => Circle(count, spacing),
            _ => throw new ArgumentException("Unknown formation."),
        };
        var cos = Math.Cos(heading);
        var sin = Math.Sin(heading);
        var result = local.Select(p => new SimVector2(anchor.X + p.X * cos - p.Y * sin,
            anchor.Y + p.X * sin + p.Y * cos)).ToArray();
        if (result.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("Formation geometry overflow.");
        return Array.AsReadOnly(result);
    }

    private static List<SimVector2> Compact(int count, double spacing)
    {
        var points = new List<SimVector2> { SimVector2.Zero };
        var directions = new (int X, int Y)[] { (1, 0), (0, 1), (-1, 1), (-1, 0), (0, -1), (1, -1) };
        for (var ring = 1; points.Count < count; ring++)
        {
            var x = 0;
            var y = -ring;
            foreach (var direction in directions)
            for (var step = 0; step < ring && points.Count < count; step++)
            {
                points.Add(new SimVector2(spacing * (x + y * 0.5), spacing * Math.Sqrt(3) * y * 0.5));
                x += direction.X;
                y += direction.Y;
            }
        }
        return points;
    }

    private static List<SimVector2> Rectangle(int count, double spacing)
    {
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        if (columns % 2 == 0) columns++;
        var points = new List<SimVector2>();
        for (var i = 0; i < count; i++)
        {
            var column = i % columns;
            var lateral = column == 0 ? 0 : (column + 1) / 2 * (column % 2 == 1 ? -1 : 1);
            points.Add(new SimVector2(-(i / columns) * spacing, lateral * spacing));
        }
        return points;
    }

    private static List<SimVector2> Wedge(int count, double spacing)
    {
        var points = new List<SimVector2> { SimVector2.Zero };
        for (var i = 1; i < count; i++)
        {
            var row = (i + 1) / 2;
            points.Add(new SimVector2(-row * spacing, row * spacing * (i % 2 == 1 ? -1 : 1)));
        }
        return points;
    }

    private static List<SimVector2> Circle(int count, double spacing)
    {
        var points = new List<SimVector2> { SimVector2.Zero };
        if (count == 1) return points;
        var followers = count - 1;
        var radius = followers < 3 ? spacing : Math.Max(spacing, spacing / (2 * Math.Sin(Math.PI / followers)));
        for (var i = 0; i < followers; i++)
        {
            var angle = 2 * Math.PI * i / followers;
            points.Add(new SimVector2(Math.Cos(angle) * radius, Math.Sin(angle) * radius));
        }
        return points;
    }

    /// <summary>Minimum total squared ideal-slot distance, with one explicit anchor. Equal costs are stable.</summary>
    public static IReadOnlyList<int> Match(IReadOnlyList<SimVector2> positions,
        IReadOnlyList<SimVector2> slots, int anchorIndex)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(slots);
        if (positions.Count != slots.Count || positions.Count is < 1 or > MaximumMembers
            || anchorIndex < 0 || anchorIndex >= positions.Count)
            throw new ArgumentException("Invalid matching members or anchor.");
        return Array.AsReadOnly(SlotMatching.Assign(positions, slots, anchorIndex));
    }
}
