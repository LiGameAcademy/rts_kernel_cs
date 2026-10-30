namespace Rts.Movement.Prototype;

/// <summary>Read-only observations for one synchronous search; never shared across frames.</summary>
internal sealed class RouteYieldObservation
{
    private readonly NavigationState _navigation;
    private readonly Walker[] _bodies;
    private readonly Dictionary<FixedVector2, bool> _standing = [];
    private readonly Dictionary<(ulong Member, FixedVector2 Target), bool> _straight = [];
    private readonly Dictionary<(ulong Member, FixedVector2 Target), SimScalar> _distance = [];
    private RouteGoalClaim[]? _claims;
    private RouteYieldTrial? _trial;
    private RouteYieldBodies? _nearby;
    private readonly HashSet<Layout> _layouts = [];
    private readonly record struct Layout(Walker Requester, ulong First, ulong Second, FixedVector2 A, FixedVector2 B);
    internal IReadOnlyList<RouteGoalClaim> Claims => _claims ??= RoutePlacements.Claims(_bodies);

    internal RouteYieldObservation(RouteWorld world)
    {
        _bodies = world.Bodies.Values.ToArray();
        _navigation = world.Navigation;
    }

    internal bool FirstVisit(Walker requester, ulong[] members, FixedVector2[] targets) =>
        _layouts.Add(new(requester, members[0], members.Length == 2 ? members[1] : 0,
            targets[0], targets.Length == 2 ? targets[1] : default));

    internal bool CanAdvance(RouteWorld world, Walker source, ulong[] members, FixedVector2[] targets) =>
        (_trial ??= new(_bodies)).CanAdvance(world, source, members, targets);

    internal bool CanStand(FixedVector2 target)
    {
        if (!_standing.TryGetValue(target, out var allowed))
        {
            allowed = _navigation.CanStandAt(target, 31);
            _standing.Add(target, allowed);
        }
        return allowed;
    }

    internal bool CanTravel(Walker member, FixedVector2 target)
    {
        var key = (member.Id, target);
        if (!_straight.TryGetValue(key, out var allowed))
        {
            allowed = StraightMovePath.TryFind(_navigation, member.Position, target, 1) is not null;
            _straight.Add(key, allowed);
        }
        return allowed;
    }

    internal SimScalar Distance(Walker member, FixedVector2 target)
    {
        var key = (member.Id, target);
        if (!_distance.TryGetValue(key, out var distance))
        {
            distance = (target - member.Position).Length;
            _distance.Add(key, distance);
        }
        return distance;
    }

    internal bool BodyAtGoal(ulong member, FixedVector2 target, ulong relocated, FixedVector2 replacement)
    {
        foreach (var point in ProjectedPositions(member, target, target, relocated, replacement))
            if (RouteYieldGeometry.GoalOverlap(point, target)) return true;
        return false;
    }

    internal bool BodyOnLeg(ulong member, FixedVector2 from, FixedVector2 target, ulong relocated, FixedVector2 replacement)
    {
        foreach (var point in ProjectedPositions(member, from, target, relocated, replacement))
        {
            if (RouteYieldGeometry.Overlap(from, target, 31, point, point, 31)) return true;
        }
        return false;
    }

    internal bool BodyAtGoalOrOnLeg(ulong member, FixedVector2 from, FixedVector2 target,
        ulong relocated, FixedVector2 replacement)
    {
        foreach (var point in ProjectedPositions(member, from, target, relocated, replacement))
        {
            if (RouteYieldGeometry.GoalOverlap(point, target) || RouteYieldGeometry.Overlap(from, target, 31, point, point, 31)) return true;
        }
        return false;
    }

    internal bool Clear(RequesterPassage passage, ulong requester, ulong[] members, FixedVector2[] targets)
    {
        if (passage.ViaVacatedBody && passage.Trace[^1] != passage.Target) return false;
        foreach (var body in _bodies)
        {
            if (body.Id == requester) continue;
            var index = Array.IndexOf(members, body.Id);
            var point = index < 0 ? body.Position : targets[index];
            for (var segment = 1; segment < passage.Trace.Count; segment++)
                if (RouteYieldGeometry.Overlap(passage.Trace[segment - 1], passage.Trace[segment], 31, point, point, 31)) return false;
        }
        return true;
    }

    internal ulong[] Obstacles(ulong member, FixedVector2 from, FixedVector2 target) =>
        (_nearby ??= new(_bodies)).Near(from, target).Where(body => body.Id != member
            && (RouteYieldGeometry.GoalOverlap(body.Position, target)
                || RouteYieldGeometry.Overlap(from, target, 31, body.Position, body.Position, 31)))
            .Select(body => body.Id).Order().ToArray();

    private IEnumerable<FixedVector2> ProjectedPositions(ulong member, FixedVector2 from, FixedVector2 target,
        ulong relocated, FixedVector2 replacement)
    {
        foreach (var body in (_nearby ??= new(_bodies)).Near(from, target))
            if (body.Id != member && body.Id != relocated) yield return body.Position;
        // The helper may have moved into the query from a distant original bucket.
        if (relocated != 0 && relocated != member) yield return replacement;
    }
}
