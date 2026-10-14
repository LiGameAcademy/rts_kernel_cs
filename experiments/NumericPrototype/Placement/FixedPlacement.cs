using NumericPrototype.Groups;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal sealed record PlacementBody(FixedPoint Position, long Radius);
internal sealed record PlacementMember(ulong Id, FixedPoint Start, long Radius, FixedPoint[] Retained);
internal sealed record PlacementOutcome(ulong Member, int Slot, FixedPoint? Goal, bool Adjusted);

internal static class FixedPlacement
{
    internal static long Spacing(MovementWorld world, IReadOnlyList<PlacementMember> members) =>
        Math.Max(world.CellSize, checked(2 * members.Max(member => member.Radius) + world.CellSize / 10));

    internal static PlacementOutcome[] Plan(MovementWorld world, PlacementMember[] members,
        FixedPoint[] ideals, IReadOnlyDictionary<ulong, int> assignments, PlacementBody[] externalBodies)
    {
        if (members.Length == 0 || members.Length > 500 || members.Any(member => member is null
            || member.Id == 0 || member.Radius <= 0 || member.Radius > 4 * world.CellSize || member.Retained is null)
            || members.Select(member => member.Id).Distinct().Count() != members.Length
            || members.Any(member => !assignments.TryGetValue(member.Id, out var slot) || slot < 0 || slot >= ideals.Length)
            || assignments.Values.Distinct().Count() != assignments.Count
            || externalBodies.Any(body => body is null || body.Radius <= 0 || body.Radius > 4 * world.CellSize))
            throw new ArgumentException("Invalid experimental placement members/slots/bodies.");
        var reachability = new PlacementReachability(world);
        var candidates = new Dictionary<int, CandidateCell[]>();
        var failed = new HashSet<ulong>();
        while (true)
        {
            var before = failed.Count;
            var occupied = externalBodies.Concat(members.Where(member => failed.Contains(member.Id))
                .SelectMany(member => member.Retained.Select(point => new PlacementBody(point, member.Radius)))).ToList();
            var result = new List<PlacementOutcome>();
            foreach (var member in members.OrderBy(member => assignments[member.Id]))
            {
                var slot = assignments[member.Id];
                FixedPoint? goal = null;
                if (!failed.Contains(member.Id))
                {
                    if (!candidates.TryGetValue(slot, out var cells))
                    {
                        cells = GroupPlanningJob.Candidates(world, ideals[slot]);
                        candidates.Add(slot, cells);
                    }
                    foreach (var cell in cells)
                    {
                        var point = world.Center(cell.X, cell.Y);
                        if (!reachability.Connected(member.Start, point, member.Radius)
                            || occupied.Any(body => Overlaps(point, member.Radius, body))) continue;
                        goal = point;
                        occupied.Add(new(point, member.Radius));
                        break;
                    }
                }
                if (goal is null) failed.Add(member.Id);
                result.Add(new(member.Id, slot, goal, goal.HasValue && goal.Value != ideals[slot]));
            }
            // Failed units keep old orders/retained positions. Repeat until no new failure is introduced.
            if (failed.Count == before) return result.OrderBy(item => item.Member).ToArray();
        }
    }

    internal static bool Overlaps(FixedPoint point, long radius, PlacementBody body)
    {
        var dx = (Int128)point.X - body.Position.X;
        var dy = (Int128)point.Y - body.Position.Y;
        var separation = (Int128)radius + body.Radius;
        return checked(dx * dx + dy * dy) < checked(separation * separation);
    }
}
