using Rts.Movement.Prototype;

/// <summary>Recovery and lookahead must agree about settled bodies; departed bodies must not keep a detour alive.</summary>
internal static class FixedBodyReferenceChecks
{
    internal static void Run()
    {
        using var stream = typeof(FixedBodyReferenceChecks).Assembly.GetManifestResourceStream("multi-before-127-20.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        var source = world.Bodies[20];
        var before = world.Save();
        var blockers = world.Bodies.Values.Where(body => body.Id != source.Id && RoutePlacements.IsFixed(body)).ToArray();
        var prepared = RouteNavigation.Prepare(world, source, rebuild: true, allowShared: false, avoidSettled: true);
        Check(prepared.Navigation is { Kind: MemberReferenceKind.Independent }, "Recovery finds an independent reference");
        var points = prepared.Navigation!.Points;
        Check(points.Zip(points.Skip(1)).All(edge => RoutePlanning.TryDirect(world, edge.First, edge.Second, blockers) is not null),
            "Every reference leg clears terrain and fixed bodies");
        var (followed, target, _) = RouteNavigation.Follow(world, prepared);
        Check(target != source.Position && RoutePlanning.TryDirect(world, source.Position, target, blockers) is not null,
            "Lookahead cannot cut the detour through settled bodies");
        Check(followed.Endpoint == source.Endpoint && followed.Preference == source.Preference
            && followed.WaitTicks == source.WaitTicks && followed.Recoveries == source.Recoveries,
            "Planning preserves goal ownership and cannot manufacture progress");
        Check(world.Save() == before, "Navigation queries do not mutate authority");

        var empty = new RouteWorld();
        var fixedBody = source with { Position = new(610, 700) };
        Check(!RoutePlanning.TryRoute(empty, new(580, 640), new(600, 640), [fixedBody], out _, out _),
            "A same-cell result cannot bypass collision on the exact final leg");

        // Controlled visibility case: an old independent reference survives while its obstacles have left.
        var clear = new RouteWorld();
        clear.Spawn(new(400, 640));
        Check(clear.Move([1], new(900, 640)), "Single member command accepted");
        clear.Step();
        var body = clear.Bodies[1];
        var detour = new Rts.Kernel.Numerics.FixedVector2[] { body.Position, new(500, 800), body.Endpoint };
        clear.Bodies[1] = body with
        {
            Navigation = new(body.Endpoint, clear.Revision, 2, MemberReferenceKind.Independent, detour, [1]),
            RouteVersion = 2, Cursor = 0
        };
        var saved = clear.Save();
        var (direct, _, _) = RouteNavigation.Follow(clear, clear.Bodies[1]);
        Check(direct.Navigation is { Kind: MemberReferenceKind.Direct } && direct.Navigation.Points[^1] == body.Endpoint,
            "A now-clear direct leg releases the stale detour");
        Check(clear.Save() == saved, "Shortcut query preserves authority");
        for (var tick = 0; tick < 100; tick++) Advance(clear);
        Check(clear.Bodies[1].Phase == WalkPhase.Idle && clear.Bodies[1].Position == body.Endpoint,
            "Shortcut actually completes after restoring every frame");
        RouteBoundaryChecks.Run();
        Console.WriteLine("fixed_body_reference=PASS geometry_lookahead_shortcut_ownership_stop_replace_queue_map_restore");
    }

    private static void Advance(RouteWorld world)
    {
        var restored = RouteWorld.Load(world.Save());
        world.Step();
        restored.Step();
        Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events), "Restored continuation matches");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
