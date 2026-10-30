using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

internal static class RecoveryGoalChecks
{
    internal static void Run()
    {
        using var stream = typeof(RecoveryGoalChecks).Assembly.GetManifestResourceStream("recovery-one-387.json")!;
        using var reader = new StreamReader(stream);
        var seed = reader.ReadToEnd();
        var world = RouteWorld.Load(seed);
        var original = world.Bodies[1];
        Check(RouteNavigation.Prepare(world, original, rebuild: true, allowShared: false,
            avoidSettled: true).Navigation is null, "Original fixed-body route is unavailable");
        var others = world.Bodies.Values.Where(body => body.Id != 1).ToArray();
        Check(RoutePlacements.TryRecoveryGoal(world, original, out var prepared), "A bounded reachable goal exists");
        Check(prepared.Endpoint == new FixedVector2(816, 176) && prepared.Preference == original.Preference,
            "Existing candidate order selects the first reachable claim without changing preference");
        Check(prepared.Position == original.Position && prepared.WaitTicks == original.WaitTicks
            && prepared.Recoveries == original.Recoveries && prepared.Pending == original.Pending,
            "Goal revision does not teleport or reset recovery/queue history");
        Check(world.Bodies.Values.Where(body => body.Id != 1).SequenceEqual(others), "Other bodies remain unchanged");
        Check(RoutePlacements.Claims(world.Bodies.Values).Where(claim => claim.Task == original.Task && claim.Member != 1)
            .All(claim => (claim.Point - prepared.Endpoint).Length >= 64), "Atomic goal has no competing claim");
        var changed = world.Save();
        Check(!RoutePlacements.TryRecoveryGoal(world, original, out _) && world.Save() == changed,
            "Stale source cannot overwrite a newer goal");

        // Exercise automatic recovery, not just the geometry/commit helper.
        world = RouteWorld.Load(seed);
        for (var tick = 0; tick < 4 && world.Bodies[1].Endpoint == original.Endpoint; tick++) Advance(world);
        Check(world.Bodies[1].Endpoint == prepared.Endpoint, "Exhausted cooperation reaches goal repair automatically");
        var revised = world.Save();
        var task = world.Bodies[1].Task;
        var stopped = RouteWorld.Load(revised);
        var position = stopped.Bodies[1].Position;
        Check(stopped.Stop([1]), "Stop accepted");
        Advance(stopped);
        for (var tick = 0; tick < 12; tick++) Advance(stopped);
        Check(stopped.Bodies[1].Phase == WalkPhase.Stopped && stopped.Bodies[1].Position == position,
            "Revised reference cannot move a stopped unit");
        var stoppedSave = stopped.Save();
        Check(!RoutePlacements.TryRecoveryGoal(stopped, stopped.Bodies[1], out _) && stopped.Save() == stoppedSave,
            "Stop cannot acquire a recovery goal");
        var replaced = RouteWorld.Load(revised);
        Check(replaced.Move([1], prepared.Endpoint), "Replacement queued");
        Advance(replaced);
        Check(replaced.Events.Any(e => e.Id == 1 && e.Kind == "accepted") && replaced.Bodies[1].Task != task,
            "New command owns its goal after repair");
        var queued = RouteWorld.Load(revised);
        Check(queued.Move([1], prepared.Endpoint, append: true), "Append queued");
        Advance(queued);
        Check(queued.Bodies[1].Task == task && queued.Bodies[1].Pending.Count == 1, "Append retains active revised order");
        var rejected = RouteWorld.Load(revised);
        Check(!rejected.Move([1], new(-32, -32)) && rejected.Save() == revised, "Illegal replacement preserves repair");
        var changedMap = RouteWorld.Load(revised);
        Check(changedMap.SetWall(new(3, 3, 1, 1), true), "Unoccupied map edit accepted");
        Advance(changedMap);
        Check(changedMap.Bodies[1].Navigation?.MapRevision == changedMap.Revision, "Map edit invalidates repaired reference");
        Console.WriteLine("recovery_goal=PASS automatic_selection_claims_history_stop_replace_queue_map_restore");
    }

    private static void Advance(RouteWorld world)
    {
        var restored = RouteWorld.Load(world.Save());
        world.Step();
        restored.Step();
        Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events), "Per-frame restore matches");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
