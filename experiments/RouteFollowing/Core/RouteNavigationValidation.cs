namespace Rts.Movement.Prototype;

internal static class RouteNavigationValidation
{
    internal static void Validate(RouteSnapshot state)
    {
        foreach (var body in state.Units)
        {
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
