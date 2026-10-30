using Rts.Movement.Prototype;

/// <summary>Ownership while the265/275 permanent placement executes; restore every observed step.</summary>
internal static class SettledYieldChecks
{
    internal static void Run()
    {
        using var stream = typeof(SettledYieldChecks).Assembly.GetManifestResourceStream("multi-before-288-265.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        var task = world.Bodies[265].Task;
        Advance(world);
        Check(world.Routes[task].Yield is { Requester: 265, Temporary: null }, "Automatic contact chooses permanent cooperation");
        var seed = world.Save();
        AwaitHelpers(RouteWorld.Load(seed), task);
        foreach (var id in new ulong[] { 265, 275 })
        {
            var stopped = RouteWorld.Load(seed);
            var position = stopped.Bodies[id].Position;
            Check(stopped.Stop([id]), "Stop accepted");
            Advance(stopped);
            for (var tick = 0; tick < 12; tick++)
            {
                var body = stopped.Bodies[id];
                Check(body.Task == 0 && body.Phase == WalkPhase.Stopped && body.Position == position,
                    "Old cooperation never moves a stopped unit");
                Advance(stopped);
            }
            var replaced = RouteWorld.Load(seed);
            Check(replaced.Move([id], position + new Rts.Kernel.Numerics.FixedVector2(1, 0)), "Replacement queued");
            Advance(replaced);
            Check(replaced.Events.Any(e => e.Id == id && e.Kind == "accepted"), "Replacement applied");
            for (var tick = 0; tick < 12; tick++)
            {
                Check(replaced.Bodies[id].Task != task, "Old task cannot regain command ownership");
                Advance(replaced);
            }
            var appended = RouteWorld.Load(seed);
            Check(appended.Move([id], position + new Rts.Kernel.Numerics.FixedVector2(1, 0), append: true), "Append queued");
            Advance(appended);
            Check(appended.Bodies[id].Pending.Count == 1 && appended.Bodies[id].Task == task, "Append preserves active order");
            Check((appended.Routes[task].Yield is not null) == (id == 265), "Only helper append cancels recruitment");
            for (var tick = 0; tick < 12; tick++) Advance(appended);
        }
        var rejected = RouteWorld.Load(seed);
        Check(!rejected.Move([265], new Rts.Kernel.Numerics.FixedVector2(-32, -32)) && rejected.Save() == seed,
            "Invalid replacement preserves the ongoing cooperation");
        var changed = RouteWorld.Load(seed);
        Check(changed.SetWall(new(3, 3, 1, 1), true), "Map edit accepted");
        Advance(changed);
        Check(changed.Routes[task].Yield is null, "Map revision invalidates old cooperation");
        Console.WriteLine("settled_yield_boundaries=PASS automatic_contact_stop_replace_append_reject_map_restore");
    }

    private static void Advance(RouteWorld world)
    {
        var restored = RouteWorld.Load(world.Save());
        world.Step();
        restored.Step();
        Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events), "Each frame restores identically");
    }

    private static void AwaitHelpers(RouteWorld world, ulong task)
    {
        var plan = world.Routes[task].Yield!;
        var original = world.Bodies[plan.Requester];
        for (var tick = 0; tick < 90; tick++)
        {
            Advance(world);
            if (world.Events.Any(e => e.Id == original.Id && e.Kind == "yield_finished")) return;
            Check(world.Routes[task].Yield is not null, "Helpers finish rather than expire");
            var body = world.Bodies[original.Id];
            Check(body.Position == original.Position && body.Facing == original.Facing
                && body.Passage == original.Passage, "Requester preserves the admitted pose while helpers move");
            Check(body.Recoveries == original.Recoveries, "Cooperative waiting is not another failed recovery");
        }
        throw new InvalidOperationException("Permanent helpers did not finish");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
