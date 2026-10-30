namespace Rts.Movement.Prototype;

/// <summary>Only signed physical displacement earns progress, never a lookahead or connection cursor jump.</summary>
internal static class RouteProgress
{
    internal static Walker Measure(Walker before, Walker moved, RouteTask route)
    {
        var sameVersion = before.ProgressVersion == route.Version;
        var end = sameVersion ? before.ProgressCursor : RouteReference.Nearest(route, before.Position);
        var progress = sameVersion ? before.Progress : SimScalar.Zero;
        var from = route.Points[RouteReference.Start(route, end)];
        // Project the actual displacement onto one real corridor segment. Changing
        // segment below does not credit its different arc-coordinate origin.
        progress += RouteReference.Project(moved.Position, before.Position,
            before.Position + route.Points[end] - from);
        while (end < route.Points.Count - 1)
        {
            var next = RouteReference.Next(route, end);
            if (RouteReference.Distance(route, next, moved.Position).CompareTo(RouteReference.Distance(route, end, moved.Position)) >= 0) break;
            end = next;
        }
        moved = moved with { Progress = progress, ProgressCursor = end, ProgressVersion = route.Version };
        if (progress >= RouteSettings.ProgressThreshold)
            moved = moved with { Progress = 0, WaitTicks = 0, RetryAtWait = 90, Recoveries = 0, Recovery = null };
        return moved;
    }
}
