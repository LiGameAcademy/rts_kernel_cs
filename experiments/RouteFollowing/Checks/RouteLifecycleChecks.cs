using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Text.Json;

internal static class RouteLifecycleChecks
{
    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var test in new (string Name, Action Check)[]
        {
            ("intermittent_clearance", IntermittentClearance),
            ("settled_requester", SettledRequester),
            ("requester_takeover", RequesterTakeover),
            ("helper_and_deadline", HelperAndDeadline)
        })
        {
            try { test.Check(); Console.WriteLine(test.Name + "=PASS"); }
            catch (InvalidOperationException error)
            {
                failures.Add(test.Name + ": " + error.Message);
                Console.WriteLine(test.Name + "=FAIL " + error.Message);
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
        Console.WriteLine("lifecycle_checks=PASS");
    }

    private static void IntermittentClearance()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 400)); world.Spawn(new(400, 480));
        world.Move([1], new(1400, 400)); world.Step();
        var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        state = state with { Units = state.Units.Select(body => body.Id == 1
            ? body with { Passage = new(2, -1) } : body).ToArray() };
        world = RouteWorld.Load(JsonSerializer.Serialize(state));
        var resumed = RouteWorld.Load(world.Save());
        world.Step(); resumed.Step();
        Check(world.Units[0].Position.X > state.Units[0].Position.X, "Direct movement remains allowed");
        Check(world.Units[0].Passage == new PassageState(2, -1), "A clear tick must not forget the nearby encounter");
        Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "Passage restores identically");
        var separated = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        separated = separated with { Units = separated.Units.Select(body => body.Id == 2
            ? body with { Position = new(400, 700) } : body).ToArray() };
        var distant = RouteWorld.Load(JsonSerializer.Serialize(separated));
        distant.Step();
        Check(distant.Units[0].Passage is null, "Distant neighbour releases passage preference");
        world.Stop([1]); world.Step();
        Check(world.Units[0].Passage is null && world.Units[0].Phase == WalkPhase.Stopped, "Stop releases encounter ownership");
    }

    private static void SettledRequester()
    {
        var world = RouteYieldChecks.Load("passage-cancel-before-476.json");
        world.Step();
        Check(world.Units.Single(body => body.Id == 4).Phase == WalkPhase.Settled, "Requester actually arrives");
        Check(world.Paths.Single(task => task.Id == 5).Yield is { Next: 1 }, "Retained task owns the remaining helper leg");
        Check(world.Events.All(ev => ev.Kind != "yield_cancelled"), "Settlement is not a new command or task release");
        var resumed = RouteWorld.Load(world.Save());
        world.Step(); resumed.Step();
        Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "Settled requester plan restores");
        var position = world.Units.Single(body => body.Id == 4).Position;
        world.Stop([4]); world.Step();
        Check(world.Paths.All(task => task.Yield is null), "Explicit Stop still cancels old cooperation");
        Check(world.Units.Single(body => body.Id == 4).Position == position, "Stop cannot be moved by helper plan");
    }

    private static RouteWorld SettledWorld()
    {
        var world = RouteYieldChecks.Load("passage-cancel-before-476.json");
        world.Step();
        Check(world.Paths.Single(task => task.Id == 5).Yield is not null, "Helper plan still active after arrival");
        return world;
    }

    private static void RequesterTakeover()
    {
        foreach (var append in new[] { false, true })
        {
            var world = SettledWorld();
            Check(world.Move([4], new(1850, 1050), append), "Requester takeover accepted");
            world.Step();
            Check(world.Paths.All(task => task.Yield is null), "New requester ownership cancels helper plan");
            var restored = RouteWorld.Load(world.Save());
            for (var i = 0; i < 10; i++)
            {
                world.Step(); restored.Step();
                Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events), "Takeover JSON continuation");
                Check(world.Units.Single(body => body.Id == 4).Task != 5, "Old task cannot reclaim requester");
            }
        }
    }

    private static void HelperAndDeadline()
    {
        var stopped = SettledWorld();
        var position = stopped.Units.Single(body => body.Id == 7).Position;
        stopped.Stop([7]); stopped.Step();
        Check(stopped.Paths.All(task => task.Yield is null), "Helper Stop cancels plan");
        for (var i = 0; i < 10; i++)
        {
            stopped.Step();
            Check(stopped.Units.Single(body => body.Id == 7).Position == position, "Helper Stop stays fixed");
        }
        var changed = SettledWorld();
        Check(changed.SetWall(new(3, 3, 1, 1), true), "Map revision accepted");
        changed.Step();
        Check(changed.Paths.All(task => task.Yield is null), "Map revision cancels plan");
        var world = SettledWorld();
        var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        state = state with { Tasks = state.Tasks.Select(task => task.Yield is { } plan
            ? task with { Yield = plan with { StartedAt = state.Frame - 90 } } : task).ToArray() };
        var expired = RouteWorld.Load(JsonSerializer.Serialize(state));
        expired.Step();
        Check(expired.Paths.All(task => task.Yield is null), "Original 90-tick plan deadline remains enforced");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
