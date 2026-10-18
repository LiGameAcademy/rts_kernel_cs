using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MovementRouteReadChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var match = OpenGroundPartyChecks.Create();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        check(match.ReadMovementRoutes(ids).Count == 0, "idle units have no active debug route");
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25, new(ids, new(1490, 2390))));
        match.Step();
        var before = match.ComputeStateHash();
        var routes = match.ReadMovementRoutes(ids.Reverse().Concat(ids).ToArray());
        check(routes.Count == 24 && routes.Select(route => route.EntityId).SequenceEqual(ids.Order()),
            "route diagnostics are unique and ID ordered");
        check(routes.All(route => route.Waypoints[0] == match.Entities.Single(entity => entity.Id == route.EntityId).Position
            && route.PreferredGoal is not null), "route includes actual current position and preferred goal");
        check(before == match.ComputeStateHash(), "route reads leave authority and events unchanged");
        var frozen = routes[0].Waypoints.ToArray();
        match.Step();
        check(frozen.SequenceEqual(routes[0].Waypoints), "route views retain detached waypoint values");
        var id = routes[0].EntityId;
        match.SubmitCommand(CommandEnvelope.Stop(match.Frame + 1, 0, 26, id));
        match.Step();
        check(match.ReadMovementRoutes([id]).Count == 0, "Stop removes the current debug route");
    }
}
