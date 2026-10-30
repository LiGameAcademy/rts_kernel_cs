using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Ownership, bounded rounds and persisted recovery cursor, independent of performance timing.</summary>
internal static class RouteRecoveryBoundaries
{
    internal static void Run()
    {
        var pending = Started();
        var body = pending.Units.Single(b => b.Id == 23);
        var probe = body.Recovery ?? throw new Exception("Missing pending recovery");
        Check(body.Recoveries == 1 && probe.Next == 1
            && probe.Candidates.SequenceEqual(new ulong[] { 1, 3, 4 }), "Actual contacts and one probe per tick");
        var contactView = body.BlockingContacts ?? throw new Exception("Missing actual contacts");
        Check(contactView.SequenceEqual(new ulong[] { 1, 3, 4 }), "Final reservation contacts retained");
        ((ulong[])contactView)[0] = 499;
        Check(pending.Units.Single(b => b.Id == 23).BlockingContacts![0] == 1, "Public view owns contact array");
        ((ulong[])probe.Candidates)[1] = 499;
        Check(pending.Units.Single(b => b.Id == 23).Recovery!.Candidates[1] == 3, "Public view owns candidate array");
        var restored = RouteWorld.Load(pending.Save());
        pending.Step(); restored.Step();
        Check(pending.Hash() == restored.Hash() && pending.Events.SequenceEqual(restored.Events), "Pending probe restore");
        Check(pending.Units.Single(b => b.Id == 23) is { Recoveries: 1, Recovery.Next: 2 }, "Second root stays in same round");
        pending.Step();
        Check(pending.Units.Single(b => b.Id == 23) is { Recoveries: 2, Recovery: null }
            && pending.Paths.Single(p => p.Id == 1).Yield!.Members.SequenceEqual(new ulong[] { 13, 4 }), "Automatic bounded admission");
        SameGeometry();
        foreach (var action in new[] { "stop", "replacement", "append", "helper_stop", "helper_replacement" }) Control(action);
        var invalid = Started(); var saved = invalid.Save();
        Check(!invalid.Move([23], new(-1, 100)) && invalid.Save() == saved, "Illegal replacement keeps pending round");
        var map = Started();
        Check(map.SetWall(new(3, 3, 1, 1), true) && map.Units.All(b => b.Recovery is null), "Map edit cancels pending rounds");
        var mapCopy = RouteWorld.Load(map.Save());
        map.Step(); mapCopy.Step();
        Check(map.Hash() == mapCopy.Hash() && map.Events.SequenceEqual(mapCopy.Events), "Map first command restore");
        var state = JsonSerializer.Deserialize<RouteSnapshot>(Started().Save())!;
        Reject(state with { Format = 7 });
        foreach (var change in new Func<RecoveryProbe, RecoveryProbe>[]
        {
            probe => probe with { Next = -1 }, probe => probe with { Next = probe.Candidates.Count },
            probe => probe with { Candidates = new ulong[] { 1, 1, 4 } },
            probe => probe with { Candidates = new ulong[] { 1, 23, 4 } },
            probe => probe with { PairWork = 2048 }, probe => probe with { StartedAt = state.Frame + 1 },
            probe => probe with { Goal = default }, probe => probe with { MapRevision = state.MapRevision + 1 }
        })
            Reject(state with { Units = state.Units.Select(b => b.Id == 23 ? b with { Recovery = change(b.Recovery!) } : b).ToArray() });
        Console.WriteLine("recovery_boundaries=PASS rounds,contacts,ownership,Stop,new-orders,queue,map,restore,old-format,corruption");
    }

    private static RouteWorld Started()
    {
        var world = RouteYieldChecks.Load("recovery-blockers-before-388.json");
        world.Step();
        Check(world.Units.Single(b => b.Id == 23).Recovery is { Next: 1 }, "Frozen first probe");
        return world;
    }

    private static void SameGeometry()
    {
        var world = new RouteWorld(); world.Spawn(new(400, 640)); world.Move([1], new(800, 640)); world.Step();
        var body = RouteNavigation.Prepare(world, world.Bodies[1], rebuild: true);
        var route = RouteNavigation.Reference(body);
        body = body with { Phase = WalkPhase.Waiting, WaitTicks = 90, RetryAtWait = 90,
            Progress = 10, ProgressCursor = RouteReference.Nearest(route, body.Position), ProgressVersion = route.Version };
        world.Bodies[1] = body;
        RouteRecovery.Advance(world);
        var after = world.Bodies[1];
        Check(after.Navigation == body.Navigation && after.Progress == body.Progress
            && after.Cursor == body.Cursor && after.RouteVersion == body.RouteVersion, "Same geometry retains version/cursor/progress");
        Check(after.Recoveries == 1 && after.RetryAtWait == 180 && after.Recovery is null
            && world.Events.Any(e => e.Kind == "retry" && e.Detail == "no_recovery_candidate"), "Exhausted no-candidate round remains bounded");
    }

    private static void Control(string action)
    {
        var world = Started(); var old = world.Units.Single(b => b.Id == 23).Task;
        var id = action.StartsWith("helper", StringComparison.Ordinal) ? 4UL : 23UL;
        var stopped = world.Units.Single(b => b.Id == id).Position;
        if (action.EndsWith("stop", StringComparison.Ordinal)) world.Stop([id]);
        else world.Move([id], new(1800, 1100), append: action == "append");
        var switched = false;
        for (var i = 0; i < (action == "append" ? 1200 : 45); i++)
        {
            var copy = RouteWorld.Load(world.Save()); world.Step(); copy.Step();
            Check(world.Hash() == copy.Hash() && world.Events.SequenceEqual(copy.Events), "Control continuation " + action);
            var body = world.Units.Single(b => b.Id == id);
            if (action.EndsWith("stop", StringComparison.Ordinal))
                Check(body.Position == stopped && body.Phase == WalkPhase.Stopped && body.Recovery is null, "Stop stays fixed");
            if (action.EndsWith("replacement", StringComparison.Ordinal))
                Check(body.Task != old && body.Recovery is null, "Old round cannot reclaim replacement");
            if (action == "append" && body.Task != old)
            {
                switched = true;
                Check(body.Recovery is null, "Queued leg drops old round");
                if (body.Task == 0 && body.Pending.Count == 0) break;
            }
        }
        if (action == "append") Check(switched, "Queued command takes over");
    }

    private static void Reject(RouteSnapshot state)
    {
        try { RouteWorld.Load(JsonSerializer.Serialize(state)); }
        catch (InvalidDataException) { return; }
        throw new Exception("Corrupt/old recovery snapshot accepted");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
