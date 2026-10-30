namespace Rts.Movement.Prototype;

internal static class RouteNavigationValidation
{
    internal static void Validate(RouteSnapshot state)
    {
        foreach (var body in state.Units)
        {
            if (body.BlockingContacts is { } contacts && (contacts.Count > 499
                || contacts.Distinct().Count() != contacts.Count || !contacts.SequenceEqual(contacts.Order())
                || contacts.Any(id => id == body.Id || !state.Units.Any(other => other.Id == id))))
                throw new InvalidDataException("Invalid recorded collision contacts.");
            if (body.Recovery is { } probe && (body.Task == 0
                || body.Phase is not (WalkPhase.Following or WalkPhase.Waiting)
                || probe.MapRevision != state.MapRevision || probe.Goal != body.Endpoint
                || body.Recoveries >= RouteSettings.RecoveryLimit || body.WaitTicks < body.RetryAtWait
                || probe.StartedAt < 0 || probe.StartedAt > state.Frame
                || !SimulationDomain.Contains(probe.Goal.X) || !SimulationDomain.Contains(probe.Goal.Y)
                || probe.Candidates is null || probe.Candidates.Count is < 1 or > 23
                || probe.Candidates.Distinct().Count() != probe.Candidates.Count
                || probe.Candidates.Any(id => id == body.Id || !state.Units.Any(other => other.Id == id))
                || probe.Next < 1 || probe.Next >= probe.Candidates.Count || probe.PairWork is < 0 or >= 2048))
                throw new InvalidDataException("Invalid bounded recovery probe.");
            if (body.Passage is { } passage && (body.Task == 0 || passage.Side is not (-1 or 1)
                || passage.Blocker == body.Id || !state.Units.Any(other => other.Id == passage.Blocker)))
                throw new InvalidDataException("Invalid local passage relation.");
            if (body.Navigation is not { } reference) continue;
            if (body.Task == 0 || reference.Points is null || reference.Corners is null
                || !Enum.IsDefined(reference.Kind) || reference.Version < 1
                || reference.MapRevision < 0 || reference.MapRevision > state.MapRevision
                || reference.Points.Count is < 2 or > 16384 || body.Cursor >= reference.Points.Count
                || reference.Points[^1] != reference.Goal
                || reference.Points.Any(point => !SimulationDomain.Contains(point.X) || !SimulationDomain.Contains(point.Y))
                || !reference.Corners.SequenceEqual(RouteReference.FindCorners(reference.Points))
                || body.RouteVersion != reference.Version)
                throw new InvalidDataException("Invalid member navigation reference.");
        }
    }
}
