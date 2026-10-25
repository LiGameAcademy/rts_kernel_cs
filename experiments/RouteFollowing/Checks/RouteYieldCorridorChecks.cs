using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Text.Json;

/// <summary>Checks execution permission independently of physical collision and navigation.</summary>
internal static class RouteYieldCorridorChecks
{
    private static int _checks;

    internal static void Run()
    {
        _checks = 0;
        var world = World();
        var saved = world.Save();
        var corridors = new RouteYieldCorridors(world);
        var mover = world.Units.Single(body => body.Id == 3);
        Check(corridors.FirstConflict(mover, [new(700, 700), new(700, 650)]) == 1,
            "Outside movement cannot enter the active helper corridor");
        Check(corridors.FirstConflict(mover with { Position = new(1100, 700) }, [new(1100, 700), new(1100, 650)]) == 4,
            "Future leg is protected while the first helper still executes");
        Check(corridors.FirstConflict(mover, [new(700, 700), new(750, 700)]) is null,
            "Unrelated movement remains free");
        Check(corridors.FirstConflict(mover, [new(700, 700), new(700, 662)]) is null,
            "Exact boundary contact stays allowed");
        Check(corridors.FirstConflict(mover, [new(700, 700),
            new(700, SimScalar.FromInteger(662) - SimScalar.FromRaw(1))]) == 1,
            "One raw unit inside the boundary is denied");
        Check(corridors.FirstConflict(mover, [new(700, 700),
            new(700, SimScalar.FromInteger(662) + SimScalar.FromRaw(1))]) is null,
            "One raw unit outside remains allowed");
        Check(corridors.FirstConflict(mover with { Task = 2 }, [new(700, 700), new(700, 650)]) is null,
            "A task cannot commandeer another task");
        Check(corridors.FirstConflict(world.Units[0], [new(600, 600), new(800, 600)]) is null,
            "Existing helpers can execute their own legs");
        var inside = mover with { Position = new(700, 620) };
        Check(corridors.FirstConflict(inside, [inside.Position, new(700, 630)]) is null,
            "An existing occupant can move outward");
        Check(corridors.FirstConflict(inside, [inside.Position, new(700, 610)]) == 1,
            "An existing occupant cannot penetrate deeper");
        Check(corridors.FirstConflict(inside, [inside.Position, new(710, 620)]) == 1,
            "Sliding inside without exiting is not new passage permission");
        Check(corridors.FirstConflict(inside with { Position = new(700, 600) },
            [new(700, 600), new(700, 610)]) is null, "A centre-line occupant can begin exiting");
        Check(world.Save() == saved, "Derivation and queries do not mutate authority");
        Check(!world.Move([3, 3], new(1200, 1000)) && world.Save() == saved,
            "Rejected input preserves plan and derived permissions");
        var restored = RouteWorld.Load(saved);
        Check(new RouteYieldCorridors(restored).FirstConflict(restored.Units[2] with { Position = new(1100, 700) },
            [new(1100, 700), new(1100, 650)]) == 4, "Restore derives the same future-leg permission");
        foreach (var action in new[] { "stop", "replacement", "append", "map", "deadline" }) Release(action);
        Console.WriteLine($"yield_corridor_checks={_checks} PASS");
    }

    private static void Release(string action)
    {
        var world = World();
        var before = world.Units.Single(body => body.Id == 1).Position;
        if (action == "stop") world.Stop([1]);
        else if (action == "replacement" || action == "append")
            Check(world.Move([1], new(1600, 1000), append: action == "append"), "Control change admitted");
        else if (action == "map") Check(world.SetWall(new(3, 3, 1, 1), true), "Terrain change admitted");
        else
        {
            var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
            world = RouteWorld.Load(JsonSerializer.Serialize(state with { Frame = 90 }));
        }
        var restored = RouteWorld.Load(world.Save());
        world.Step(); restored.Step();
        Check(world.Paths.All(task => task.Yield is null), "Cancellation releases remaining legs: " + action);
        var mover = world.Units.Single(body => body.Id == 3) with { Task = 1, Position = new(1100, 700) };
        Check(new RouteYieldCorridors(world).FirstConflict(mover, [new(1100, 700), new(1100, 650)]) is null,
            "No stale corridor after cancellation: " + action);
        Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events),
            "Cancellation continues identically after restore: " + action);
        if (action == "stop") Check(world.Units[0].Position == before, "Stop is never moved to clear a corridor");
    }

    private static RouteWorld World()
    {
        var world = new RouteWorld();
        world.Spawn(new(600, 600)); world.Spawn(new(600, 400));
        world.Spawn(new(700, 700)); world.Spawn(new(1000, 600));
        var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        var units = state.Units.Select(body => body with
        {
            Task = 1,
            Phase = body.Id == 1 ? WalkPhase.Yielding : body.Id == 4 ? WalkPhase.Settled : WalkPhase.Following,
            Preference = body.Id switch { 1 => new(800, 600), 2 => new(800, 400), 3 => new(800, 800), _ => new(1200, 600) },
            Endpoint = body.Id switch { 1 => new(800, 600), 2 => new(800, 400), 3 => new(800, 800), _ => new(1200, 600) }
        }).ToArray();
        var task = new RouteTask(1, new(800, 600), [new(600, 600), new(800, 600)], 1, 0, [],
            new(2, new ulong[] { 1, 4 }, 0, 0, 0));
        return RouteWorld.Load(JsonSerializer.Serialize(state with { Units = units, Tasks = [task], NextTask = 2 }));
    }

    private static void Check(bool passed, string detail)
    {
        _checks++;
        if (!passed) throw new InvalidOperationException(detail);
    }
}
