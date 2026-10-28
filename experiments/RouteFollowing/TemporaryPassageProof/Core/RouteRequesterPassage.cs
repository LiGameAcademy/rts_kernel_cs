namespace Rts.Movement.Prototype;

internal sealed record RequesterPassage(FixedVector2 Target, IReadOnlyList<FixedVector2> Trace, bool ViaVacatedBody);

/// <summary>Admits and executes one bounded connection through a helper's vacated position.</summary>
internal static class RouteRequesterPassage
{
    internal static RequesterPassage Predict(RouteWorld world, Walker body, FixedVector2 target, bool reachTarget)
    {
        var start = body.Position;
        var trace = new List<FixedVector2> { start };
        for (var tick = 0; tick < 90 && body.Position != target
            && (reachTarget || RouteReference.Project(body.Position, start, target) < 32); tick++)
        {
            var step = RouteMotionCandidates.Compute(world, body, target, RouteSettings.Speed);
            trace.AddRange(step.Trace.Skip(1));
            if (step.Position == body.Position) break;
            body = body with { Position = step.Position, Facing = step.Facing,
                Velocity = (step.Position - body.Position) * RouteSettings.TickRate };
        }
        return new(target, trace, reachTarget);
    }

    internal static bool Hold(RouteWorld world, Walker body) => Connection(world, body) is { Phase: TemporaryYieldPhase.Outward };

    internal static bool TryTarget(RouteWorld world, Walker body, out FixedVector2 target)
    {
        var connection = Connection(world, body);
        target = connection?.RequesterTarget ?? body.Endpoint;
        return connection?.Phase == TemporaryYieldPhase.Passing;
    }

    private static TemporaryYield? Connection(RouteWorld world, Walker body) =>
        world.Routes.TryGetValue(body.Task, out var route) && route.Yield is { Temporary.HasRequesterConnection: true } plan
            && plan.Requester == body.Id ? plan.Temporary : null;
}
