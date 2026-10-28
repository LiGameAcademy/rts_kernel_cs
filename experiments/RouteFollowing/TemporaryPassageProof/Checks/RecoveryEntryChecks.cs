using System.Text.Json;
using Rts.Movement.Prototype;

/// <summary>Normal motion must observe a new rejection before spending a fresh recovery round.</summary>
internal static class RecoveryEntryChecks
{
    private static int _assertions;

    internal static void Run()
    {
        using var stream = typeof(RecoveryEntryChecks).Assembly.GetManifestResourceStream("before-442-22-retry.json")!;
        using var reader = new StreamReader(stream);
        var state = JsonSerializer.Deserialize<RouteSnapshot>(reader.ReadToEnd())!;
        var world = RouteWorld.Load(JsonSerializer.Serialize(state with { Format = RouteSettings.SnapshotFormat }));
        while (world.Frame < 514) Advance(world);
        var prior = world.Units.Single(body => body.Id == 23);
        Advance(world);
        var resumed = world.Units.Single(body => body.Id == 23);
        Check(resumed.Phase == WalkPhase.Following && resumed.BlockingContacts is { Count: 0 },
            "Real helper release resumes unobstructed motion");
        Check(resumed.WaitTicks == prior.WaitTicks && resumed.Recoveries == prior.Recoveries
            && resumed.RetryAtWait == prior.RetryAtWait, "Resume does not erase waits, reset rounds or invent a delay");
        Check(world.Events.All(ev => ev.Id != 23 || ev.Kind != "retry"), "Resume does not spend a round before current motion");
        Scheduler(world.Save());
        while (world.Frame < 519) Advance(world);
        var blocked = world.Units.Single(body => body.Id == 23);
        Check(blocked.Phase == WalkPhase.Waiting && blocked.BlockingContacts!.Contains(11UL),
            "The actual motion supplies the new blocking contact");
        Advance(world);
        Check(world.Paths.Any(task => task.Yield is { Requester: 23 } plan && plan.Members.SequenceEqual(new ulong[] { 24, 11 })),
            "The retained final round starts actual existing cooperation");
        Console.WriteLine("recovery_entry_assertions=" + _assertions);
    }

    private static void Scheduler(string seed)
    {
        // Synthetic scheduler boundaries, separate from unchanged complete-world acceptance inputs.
        var state = JsonSerializer.Deserialize<RouteSnapshot>(seed)!;
        RouteWorld Case(bool historical, bool pending) => RouteWorld.Load(JsonSerializer.Serialize(state with
        {
            Units = state.Units.Select(body => body.Id is 1 or 2 ? body with
            {
                Phase = body.Id == 1 ? WalkPhase.Following : WalkPhase.Waiting,
                WaitTicks = 90, RetryAtWait = 90, Recoveries = 0, Blocker = null,
                BlockingContacts = historical && body.Id == 1 ? null : [],
                Recovery = pending && body.Id == 1 ? new(body.Endpoint, state.MapRevision, state.Frame, [2, 3], Next: 1) : null
            } : body).ToArray()
        }));
        var skipped = Case(historical: false, pending: false);
        RouteRecovery.Advance(skipped);
        Check(skipped.Units.Single(body => body.Id == 1).Recoveries == 0,
            "Following with explicit empty contacts does not start a new round");
        Check(skipped.Events.Any(ev => ev.Id == 2 && ev.Kind == "retry"),
            "Filtering before sorting cannot stall another waiting member");
        foreach (var retained in new[] { Case(historical: true, pending: false), Case(historical: false, pending: true) })
        {
            RouteRecovery.Advance(retained);
            Check(retained.Events.Any(ev => ev.Id == 1 && ev.Kind is "retry" or "yield_started"),
                "Historical missing contacts and pending recovery retain the previous eligibility");
        }
    }

    private static void Advance(RouteWorld world)
    {
        var restored = RouteWorld.Load(world.Save());
        world.Step();
        restored.Step();
        Check(world.Save() == restored.Save() && world.Events.SequenceEqual(restored.Events), "Per-frame restore agrees");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _assertions++;
    }
}
