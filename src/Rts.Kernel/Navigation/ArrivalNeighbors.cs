namespace Rts.Kernel.Navigation;

internal sealed record ArrivalMember(EntityId Id, int Slot, SimVector2 Goal, double Radius);

/// <summary>Derived immutable neighbours of assigned endpoints; active membership is checked by the match.</summary>
internal sealed class ArrivalNeighbors
{
    private readonly Dictionary<EntityId, IReadOnlyList<ArrivalMember>> _prior = [];
    internal ArrivalNeighbors(NavigationState navigation, IReadOnlyList<ArrivalMember> members)
    {
        // Final corridor length plus both bodies: 2 * (rA + rB) + 2 cells.
        var cells = (int)Math.Min(int.MaxValue, Math.Max(1,
            Math.Ceiling(members.Max(member => member.Radius) * 4 / navigation.Grid.CellSize) + 2));
        var buckets = new Dictionary<(int X, int Y), List<ArrivalMember>>();
        foreach (var member in members)
        {
            navigation.TryWorldToCell(member.Goal, out var cell);
            var key = (cell.X / cells, cell.Y / cells);
            if (!buckets.TryGetValue(key, out var bucket)) buckets.Add(key, bucket = []);
            bucket.Add(member);
        }
        foreach (var member in members)
        {
            navigation.TryWorldToCell(member.Goal, out var cell);
            var x = cell.X / cells; var y = cell.Y / cells;
            var prior = new List<ArrivalMember>();
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (buckets.TryGetValue((x + dx, y + dy), out var bucket))
                    foreach (var other in bucket)
                        if (other.Slot < member.Slot && CrowdGeometry.Overlap(member.Goal, member.Goal,
                            member.Radius * 2 + navigation.Grid.CellSize, other.Goal, other.Goal,
                            other.Radius * 2 + navigation.Grid.CellSize)) prior.Add(other);
            _prior.Add(member.Id, prior.OrderBy(other => other.Slot).ToArray());
        }
    }
    internal IReadOnlyList<ArrivalMember> Prior(EntityId id) => _prior[id];

    internal static bool BlocksApproach(SimVector2 body, double radius, SimVector2 current,
        double movingRadius, MoveOrder moving, double cellSize)
    {
        // Only the final approach can require assembly ordering. Distant crossing remains crowd navigation.
        var remaining = radius + movingRadius + cellSize * 2;
        for (var i = moving.Waypoints.Count - 1; i >= moving.NextWaypoint && remaining > 0; i--)
        {
            var to = moving.Waypoints[i];
            var from = i == moving.NextWaypoint ? current : moving.Waypoints[i - 1];
            var dx = to.X - from.X; var dy = to.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(length)) return true;
            if (length > remaining)
                from = new(to.X + (from.X - to.X) * (remaining / length),
                    to.Y + (from.Y - to.Y) * (remaining / length));
            if (CrowdGeometry.Overlap(body, body, radius, from, to, movingRadius)) return true;
            remaining -= length;
        }
        return false;
    }
}
