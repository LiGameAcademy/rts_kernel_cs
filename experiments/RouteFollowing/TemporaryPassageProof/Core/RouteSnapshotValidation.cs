namespace Rts.Movement.Prototype;

internal static class RouteSnapshotValidation
{
    internal static void Validate(RouteSnapshot state)
    {
        if (state.Format != RouteSettings.SnapshotFormat || state.Frame < 0 || state.EventSequence < 0 || state.MapRevision < 0 || state.PlacementRevision < 0
            || state.Units is null || state.Tasks is null || state.Commands is null || state.Flags is null
            || state.Units.Length > 500 || state.Commands.Length > 16) throw new InvalidDataException("Invalid experimental snapshot.");
        var ids = state.Units.Select(body => body.Id).ToHashSet();
        var tasks = state.Tasks.Select(task => task.Id).ToHashSet();
        if (ids.Count != state.Units.Length || tasks.Count != state.Tasks.Length
            || ids.Contains(0) || tasks.Contains(0) || state.NextUnit == 0 || state.NextTask == 0
            || ids.Any(id => id >= state.NextUnit) || tasks.Any(id => id >= state.NextTask))
            throw new InvalidDataException("Invalid identities.");
        bool Point(FixedVector2 point) => SimulationDomain.Contains(point.X) && SimulationDomain.Contains(point.Y);
        foreach (var task in state.Tasks)
            if (task.Points is null || task.Points.Count < 2 || task.Points.Count > 16384 || task.Points.Any(p => !Point(p))
                || task.Corners is null || task.Corners.Count > task.Points.Count - 2
                || task.Corners.Any(corner => corner < 1 || corner >= task.Points.Count - 1)
                || !task.Corners.SequenceEqual(task.Corners.Distinct().Order())
                || !Point(task.Anchor) || task.Version < 1 || task.MapRevision < 0 || task.MapRevision > state.MapRevision)
                throw new InvalidDataException("Invalid route.");
        RouteNavigationValidation.Validate(state);
        foreach (var body in state.Units)
        {
            if (!Point(body.Position) || !Point(body.Preference) || !Point(body.Endpoint) || !Point(body.Offset)
                || !Point(body.Velocity) || !Enum.IsDefined(body.Phase) || body.Pending is null || body.Pending.Count > 8
                || body.WaitTicks < 0 || body.RetryAtWait < 0 || body.Recoveries < 0 || body.Recoveries > 4
                || body.TransitQuarters is < 0 or > 4 || body.Task == 0 && body.TransitQuarters != 4
                || body.Cursor is < 0 or > 16384 || body.RouteVersion < 0
                || body.PlacementCheckedRevision < -1 || body.PlacementCheckedRevision > state.PlacementRevision
                || body.Task == 0 && body.PlacementCheckedRevision != -1
                || body.Task != 0 && (body.Endpoint - body.Preference).Length > 156
                || body.ProgressCursor is < 0 or > 16384 || body.ProgressVersion < 0
                || !SimulationDomain.Contains(body.Progress) || body.Progress >= RouteSettings.ProgressThreshold
                || body.ProgressVersion == 0 && (body.ProgressCursor != 0 || body.Progress != 0)
                || body.Task == 0 && (body.Progress != 0 || body.ProgressCursor != 0 || body.ProgressVersion != 0)
                || !SimulationDomain.Contains(body.Facing) || body.Facing < -SimMath.Pi || body.Facing >= SimMath.Pi
                || body.Task == 0 && body.Phase is WalkPhase.Following or WalkPhase.Waiting or WalkPhase.Settled or WalkPhase.Yielding or WalkPhase.YieldHolding
                || body.Task != 0 && body.Phase is WalkPhase.Idle or WalkPhase.Stopped or WalkPhase.Failed || body.Blocker is { } blocker && (!ids.Contains(blocker) || blocker == body.Id)
                || body.Task != 0 && (!tasks.Contains(body.Task)
                    || body.Navigation is null && body.RouteVersion == state.Tasks.Single(task => task.Id == body.Task).Version
                    && body.Cursor >= state.Tasks.Single(task => task.Id == body.Task).Points.Count)
                || body.Pending.Any(goal => !tasks.Contains(goal.Task) || !Point(goal.Preference) || !Point(goal.Offset)))
                throw new InvalidDataException("Invalid member state.");
            if (body.Task != 0 && body.ProgressVersion > 0)
            {
                var route = body.Navigation is null ? state.Tasks.Single(task => task.Id == body.Task) : RouteNavigation.Reference(body);
                if (body.ProgressVersion > route.Version || body.ProgressVersion == route.Version
                    && (body.ProgressCursor < 1 || body.ProgressCursor >= route.Points.Count
                        || body.ProgressCursor != route.Points.Count - 1 && !route.Corners.Contains(body.ProgressCursor)))
                    throw new InvalidDataException("Invalid physical progress segment.");
            }
            if (body.Connection is { } connection && (body.Task == 0 || connection.Points is null
                || connection.Points.Count is < 2 or > 257 || connection.Next < 0 || connection.Next >= connection.Points.Count
                || connection.TransitQuarters is < 0 or > 4 || connection.TransitQuarters > body.TransitQuarters
                || connection.Points.Any(point => !Point(point)) || !Point(connection.Target)
                || connection.Points[^1] != connection.Target || connection.Version < 1 || connection.Cursor < 0
                || (connection.Target - connection.Points[0]).Length > 256))
                throw new InvalidDataException("Invalid bounded connection.");
        }
        foreach (var task in state.Tasks.Where(task => task.Yield is not null))
        {
            var plan = task.Yield!;
            if (plan.Members is null || plan.Members.Count is < 1 or > 2 || plan.Members.Distinct().Count() != plan.Members.Count
                || plan.Next < 0 || plan.Next >= plan.Members.Count || plan.StartedAt < 0 || plan.StartedAt > state.Frame
                || plan.MapRevision < 0 || plan.MapRevision > state.MapRevision || !ids.Contains(plan.Requester)
                || plan.Members.Contains(plan.Requester) || state.Units.Single(b => b.Id == plan.Requester).Task != task.Id
                || state.Units.Single(b => b.Id == plan.Requester).Phase is not (WalkPhase.Following or WalkPhase.Waiting or WalkPhase.Settled)
                || state.Units.Single(b => b.Id == plan.Requester) is { Phase: WalkPhase.Settled, Pending.Count: > 0 }
                || plan.Members.Any(id => !ids.Contains(id) || state.Units.Single(b => b.Id == id).Task != task.Id
                    || state.Units.Single(b => b.Id == id).Pending.Count != 0)
                || plan.Temporary is null && plan.Members.Where((id, index) => index == plan.Next).Any(id => state.Units.Single(b => b.Id == id).Phase != WalkPhase.Yielding)
                || plan.Temporary is null && plan.Members.Where((id, index) => index != plan.Next).Any(id => state.Units.Single(b => b.Id == id).Phase != WalkPhase.Settled))
                throw new InvalidDataException("Invalid bounded yield plan.");
            if (plan.Temporary is { } temporary)
            {
                if (!Enum.IsDefined(temporary.Phase) || temporary.Targets is null || temporary.Origins is null
                    || temporary.Targets.Count != plan.Members.Count || temporary.Origins.Count != plan.Members.Count
                    || temporary.Targets.Any(point => !Point(point)) || temporary.Origins.Any(point => !Point(point))
                    || !Point(temporary.RequesterStart) || !Point(temporary.RequesterTarget))
                    throw new InvalidDataException("Invalid temporary passage.");
                if (temporary.HasRequesterConnection
                    && (temporary.RequesterTarget != temporary.Origins[^1]
                        || (temporary.RequesterTarget - temporary.RequesterStart).Length > 156
                        || temporary.RequesterStart == temporary.RequesterTarget
                        || temporary.Phase == TemporaryYieldPhase.Outward
                            && state.Units.Single(body => body.Id == plan.Requester).Position != temporary.RequesterStart))
                    throw new InvalidDataException("Invalid requester connection.");
                for (var index = 0; index < plan.Members.Count; index++)
                {
                    var body = state.Units.Single(body => body.Id == plan.Members[index]);
                    var expected = temporary.Phase != TemporaryYieldPhase.Passing && index == plan.Next
                        ? WalkPhase.Yielding : WalkPhase.YieldHolding;
                    if (body.Phase != expected || (temporary.Targets[index] - temporary.Origins[index]).Length > 156)
                        throw new InvalidDataException("Invalid temporary member.");
                }
            }
        }
        foreach (var body in state.Units.Where(body => body.Phase is WalkPhase.Yielding or WalkPhase.YieldHolding))
            if (!state.Tasks.Any(task => task.Id == body.Task && task.Yield is { } plan
                && (body.Phase == WalkPhase.YieldHolding ? plan.Temporary is not null && plan.Members.Contains(body.Id)
                    : plan.Members[plan.Next] == body.Id)))
                throw new InvalidDataException("Yielding member has no owner.");
        foreach (var group in RoutePlacements.Claims(state.Units).GroupBy(claim => claim.Task))
        {
            var claims = group.ToArray();
            if (claims.Length > 24 || claims.Select(claim => claim.Member).Distinct().Count() != claims.Length)
                throw new InvalidDataException("Invalid task goal membership.");
            for (var i = 0; i < claims.Length; i++)
            for (var j = i + 1; j < claims.Length; j++)
                if ((claims[i].Point - claims[j].Point).Length < 64)
                    throw new InvalidDataException("Overlapping task goal reservations.");
        }
        for (var i = 0; i < state.Units.Length; i++)
        for (var j = i + 1; j < state.Units.Length; j++)
            if ((state.Units[i].Position - state.Units[j].Position).Length < 62)
                throw new InvalidDataException("Snapshot bodies overlap.");
        foreach (var command in state.Commands)
            if (command.Members is null || command.Members.Count is < 1 or > 24
                || command.Members.Distinct().Count() != command.Members.Count
                || command.Members.Any(id => !ids.Contains(id)) || !Point(command.Goal))
                throw new InvalidDataException("Invalid pending command.");
    }
}
