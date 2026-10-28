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
    internal IReadOnlyList<RouteGoalClaim> Claims => _claims ??= RoutePlacements.Claims(_bodies);

    internal RouteYieldObservation(RouteWorld world)
    {
        _bodies = world.Bodies.Values.ToArray();
        _navigation = world.Navigation;
    }

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
        foreach (var body in _bodies)
            if (body.Id != member && RouteYieldGeometry.GoalOverlap(Position(body, relocated, replacement), target)) return true;
        return false;
    }

    internal bool BodyOnLeg(ulong member, FixedVector2 from, FixedVector2 target, ulong relocated, FixedVector2 replacement)
    {
        foreach (var body in _bodies)
        {
            if (body.Id == member) continue;
            var point = Position(body, relocated, replacement);
            if (CrowdGeometry.Overlap(from, target, 31, point, point, 31)) return true;
        }
        return false;
    }

    internal bool BodyAtGoalOrOnLeg(ulong member, FixedVector2 from, FixedVector2 target,
        ulong relocated, FixedVector2 replacement)
    {
        foreach (var body in _bodies)
        {
            if (body.Id == member) continue;
            var point = Position(body, relocated, replacement);
            if (RouteYieldGeometry.GoalOverlap(point, target) || CrowdGeometry.Overlap(from, target, 31, point, point, 31)) return true;
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
                if (CrowdGeometry.Overlap(passage.Trace[segment - 1], passage.Trace[segment], 31, point, point, 31)) return false;
        }
        return true;
    }

    private static FixedVector2 Position(Walker body, ulong relocated, FixedVector2 replacement) =>
        body.Id == relocated ? replacement : body.Position;
}
