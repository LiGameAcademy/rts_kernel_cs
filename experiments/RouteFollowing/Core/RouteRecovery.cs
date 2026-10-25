namespace Rts.Movement.Prototype;

/// <summary>One admitted recovery per frame; queued requests do not consume attempts.</summary>
internal static class RouteRecovery
{
    internal static void Advance(RouteWorld world)
    {
        var waiting = world.Bodies.Values.Where(body => body.Task != 0
            && body.Phase is WalkPhase.Waiting or WalkPhase.Following
            && world.Routes[body.Task].Yield is null && body.WaitTicks >= body.RetryAtWait)
            .OrderBy(body => body.Id).FirstOrDefault();
        if (waiting is null) return;
        if (RouteYielding.TryStart(world, waiting)) return;
        var attempts = waiting.Recoveries + 1;
        if (attempts > RouteSettings.RecoveryLimit)
        {
            world.Bodies[waiting.Id] = waiting with
            {
                Task = 0, Phase = WalkPhase.Failed, Velocity = default, Blocker = null,
                Navigation = null, Passage = null, Connection = null, TransitQuarters = 4,
                Progress = 0, ProgressCursor = 0, ProgressVersion = 0, PlacementCheckedRevision = -1
            };
            world.Publish(waiting.Id, "failed", "no_member_progress");
            return;
        }
        var prepared = RouteNavigation.Prepare(world, waiting, rebuild: true);
        world.Bodies[waiting.Id] = prepared with
            { Recoveries = attempts, RetryAtWait = waiting.WaitTicks + RouteSettings.WaitBeforeRetry };
        world.Publish(waiting.Id, "retry", prepared.Navigation is null ? "member_route_unavailable" : "member_reference_replaced");
    }
}
