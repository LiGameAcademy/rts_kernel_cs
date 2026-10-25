namespace Rts.Movement.Prototype;

/// <summary>All proposals read frame-start bodies; only accepted timed reservations may commit positions.</summary>
internal static class RouteMotion
{
    internal static void Advance(RouteWorld world)
    {
        RouteRecovery.Advance(world);
        var reservations = new CrowdReservations(64);
        foreach (var body in world.Bodies.Values)
            reservations.Put(CrowdBody.Stationary(new(body.Id), 31, body.Position));
        var proposals = new List<MotionProposal>();
        foreach (var source in world.Bodies.Values.Where(body => body.Task != 0
            && body.Phase is WalkPhase.Following or WalkPhase.Waiting or WalkPhase.Yielding))
        {
            var (body, target, _) = source.Phase == WalkPhase.Yielding
                ? (source, source.Endpoint, 4) : RouteNavigation.Follow(world, source);
            body = body with { Passage = RouteMotionCandidates.RetainPassage(world, body) };
            var step = RouteMotionCandidates.Compute(world, body, target, RouteSettings.Speed);
            proposals.Add(new(body, target, step));
            reservations.Put(new(new(body.Id), 31, step.Trace, step.Times));
        }
        var ordered = proposals.OrderByDescending(item => item.Body.WaitTicks).ThenBy(item => item.Body.Id).ToArray();
        var held = CrowdMotionResolver.Resolve(reservations, ordered.Select(item => new EntityId(item.Body.Id)).ToArray());
        var selected = new List<(MotionProposal Proposal, MovementStep Step, ulong? Blocker, PassageState? Passage)>();
        var alternatives = 0;
        foreach (var proposal in ordered)
        {
            var step = proposal.Step;
            var body = proposal.Body;
            var blocker = held.Contains(new(body.Id))
                ? reservations.FirstMotionConflict(new(new(body.Id), 31, step.Trace, step.Times))?.Value : null;
            var passage = body.Passage;
            // A cancelled proposal whose dependencies are now safe may resume immediately.
            if (blocker is { } id && alternatives++ < 16
                && RouteMotionCandidates.TryAlternative(world, proposal, id, reservations, out var alternative, out passage))
            {
                step = alternative;
                blocker = null;
            }
            if (blocker is null) reservations.Put(new(new(body.Id), 31, step.Trace, step.Times));
            else passage = body.Passage?.Blocker == blocker ? body.Passage : null;
            selected.Add((proposal, step, blocker, passage));
        }
        // Selection may update reservations, but authority remains untouched until every choice is fixed.
        foreach (var (proposal, step, blocker, passage) in selected)
            Commit(world, proposal, step, blocker, passage);
    }

    private static void Commit(RouteWorld world, MotionProposal proposal, MovementStep step,
        ulong? blocker, PassageState? passage)
    {
        var body = proposal.Body;
        var moved = blocker is null && step.Position != body.Position;
        var updated = body with
        {
            Position = blocker is null ? step.Position : body.Position,
            Facing = step.Facing,
            Velocity = moved ? (step.Position - body.Position) * RouteSettings.TickRate : default,
            Phase = body.Phase == WalkPhase.Yielding ? WalkPhase.Yielding : moved ? WalkPhase.Following : WalkPhase.Waiting,
            Blocker = blocker, Passage = passage, Connection = null, TransitQuarters = 4,
            WaitTicks = body.WaitTicks + (moved ? 0 : 1)
        };
        if (body.Phase != WalkPhase.Yielding && body.Navigation is not null)
            updated = RouteProgress.Measure(body, updated, RouteNavigation.Reference(body));
        if (blocker is null && step.Position == body.Endpoint)
            updated = updated with { Phase = WalkPhase.Settled, Velocity = default, Blocker = null, Passage = null };
        world.Bodies[body.Id] = updated;
    }
}
