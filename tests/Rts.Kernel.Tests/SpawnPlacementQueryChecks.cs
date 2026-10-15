using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class SpawnPlacementQueryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var flags = new byte[16]; flags[10] = 2;
        var grid = new PathingGrid(4, 4, 10, default, flags);
        var definitions = new[] { new MovementDefinition(1, 30, 5) };
        var match = new RtsMatch(MatchConfig.Default, 91, grid, movementDefinitions: definitions);
        var hash = match.ComputeStateHash();
        var state = match.CaptureSnapshot();
        check(match.CanSpawn(new(5, 5), 1), "valid body is queryable before spawn");
        check(!match.CanSpawn(new(2, 5), 1) && !match.CanSpawn(new(25, 25), 1), "boundary and blocked ground are unavailable");
        check(!match.CanSpawn(new(5, 5), 99), "unknown movement definition cannot spawn");
        check(!match.CanSpawn(new(SimScalar.MaxValue, 0), 0), "diagnostic placement also rejects out-of-domain coordinates");
        check(match.CanSpawn(new(5, 5), 0), "diagnostic point entity semantics are preserved");
        check(hash == match.ComputeStateHash() && state.NextEntityId == match.CaptureSnapshot().NextEntityId
            && state.RngState == match.CaptureSnapshot().RngState && match.DrainEvents().Count == 0,
            "queries do not mutate state, consume IDs/randomness or emit events");
        match.SubmitCommand(CommandEnvelope.Spawn(2, 0, 0, new(5, 5), 1));
        var queuedHash = match.ComputeStateHash();
        check(match.CanSpawn(new(5, 5), 1) && queuedHash == match.ComputeStateHash(), "future commands are not implicit reservations");
        match.Step(); match.Step();
        check(match.DrainEvents().Single().Kind == MatchEventKind.EntitySpawned && !match.CanSpawn(new(5, 5), 1),
            "actual command uses the same rule and committed body changes the next query");
        check(match.CanSpawn(new(15, 5), 1), "touching circles are queryable");
        match.SubmitCommand(CommandEnvelope.Spawn(3, 0, 1, new(15, 5), 1)); match.Step();
        check(match.DrainEvents().Single().Kind == MatchEventKind.EntitySpawned && match.Entities.Count == 2,
            "touching placement remains valid during execution");
        check(match.CanSpawn(new(15, 15), 1), "target is available before an obstacle command");
        match.SubmitCommand(CommandEnvelope.SetObstacle(4, 0, 2, 1, new(1, 1, 1, 1))); match.Step();
        check(!match.CanSpawn(new(15, 15), 1), "executed dynamic footprint is observed by placement query");
        check(RtsMatch.Restore(match.CaptureSnapshot(), grid, movementDefinitions: definitions).ComputeStateHash() == match.ComputeStateHash(),
            "query API does not add snapshot authority or change the format");
        var large = new RtsMatch(MatchConfig.Default, 91, grid,
            movementDefinitions: new[] { new MovementDefinition(2, 30, 15) });
        check(!large.CanSpawn(new(15, 15), 2), "frozen radius clearance shares execution checks near a blocked cell");
    }
}
