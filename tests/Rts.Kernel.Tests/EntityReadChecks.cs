using Rts.Kernel;

internal static class EntityReadChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var match = new RtsMatch(MatchConfig.Default, 7);
        var live = match.Entities;
        var emptyCopy = match.ReadEntities();
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(5, 5)));
        match.Step();
        check(live.Count == 1 && emptyCopy.Count == 0, "live collection observes spawn; detached empty copy stays empty");
        var copy = match.ReadEntities();
        var captured = copy[0];
        match.SubmitCommand(CommandEnvelope.SetVelocity(2, 0, 1, captured.Id, new SimVector2(30, 0)));
        match.SubmitCommand(CommandEnvelope.Spawn(2, 0, 2, new SimVector2(25, 5)));
        match.Step();
        check(live.Count == 2 && copy.Count == 1 && copy[0] == captured,
            "detached entity count and state survive later spawn and movement");
        check(live.First().Position != captured.Position, "live entity state observes movement");
        check(match.ReadEntities().Select(e => e.Id.Value).SequenceEqual(new[] { 1UL, 2UL }),
            "detached view keeps stable entity ID order");
        var hash = match.ComputeStateHash();
        try
        {
            ((IList<EntityState>)copy)[0] = captured with { Position = new SimVector2(999, 999) };
            check(false, "detached list rejects mutation");
        }
        catch (NotSupportedException)
        {
            check(true, "detached list rejects mutation");
        }
        check(hash == match.ComputeStateHash(), "read and attempted mutation do not alter authority");
    }
}
