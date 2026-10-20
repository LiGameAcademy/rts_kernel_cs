namespace Rts.Movement.Prototype;

internal static class RouteSnapshotValidation
{
    internal static void Validate(RouteSnapshot state)
    {
        if (state.Format != 1 || state.Frame < 0 || state.EventSequence < 0 || state.MapRevision < 0
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
                || !Point(task.Anchor) || task.Version < 1 || task.MapRevision < 0 || task.MapRevision > state.MapRevision)
                throw new InvalidDataException("Invalid route.");
        foreach (var body in state.Units)
        {
            if (!Point(body.Position) || !Point(body.Preference) || !Point(body.Endpoint) || !Point(body.Offset)
                || !Point(body.Velocity) || !Enum.IsDefined(body.Phase) || body.Pending is null || body.Pending.Count > 8
                || body.WaitTicks < 0 || body.RetryAtWait < 0 || body.Recoveries < 0 || body.Recoveries > 4
                || body.Cursor is < 0 or > 16384 || body.RouteVersion < 0
                || !SimulationDomain.Contains(body.Facing) || body.Facing < -SimMath.Pi || body.Facing >= SimMath.Pi
                || body.Task == 0 && body.Phase is WalkPhase.Following or WalkPhase.Waiting or WalkPhase.Settled
                || body.Task != 0 && body.Phase is WalkPhase.Idle or WalkPhase.Stopped or WalkPhase.Failed || body.Blocker is { } blocker && (!ids.Contains(blocker) || blocker == body.Id)
                || body.Task != 0 && (!tasks.Contains(body.Task)
                    || body.RouteVersion == state.Tasks.Single(task => task.Id == body.Task).Version
                    && body.Cursor >= state.Tasks.Single(task => task.Id == body.Task).Points.Count)
                || body.Pending.Any(goal => !tasks.Contains(goal.Task) || !Point(goal.Preference) || !Point(goal.Offset)))
                throw new InvalidDataException("Invalid member state.");
            if (body.Connection is { } connection && (body.Task == 0 || connection.Points is null
                || connection.Points.Count is < 2 or > 257 || connection.Next < 0 || connection.Next >= connection.Points.Count
                || connection.Points.Any(point => !Point(point)) || !Point(connection.Target)
                || connection.Points[^1] != connection.Target || connection.Version < 1 || connection.Cursor < 0
                || (connection.Target - connection.Points[0]).Length > 256))
                throw new InvalidDataException("Invalid bounded connection.");
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
