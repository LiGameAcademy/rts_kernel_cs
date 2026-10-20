using Rts.Movement.Prototype;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RouteBoundaryChecks
{
    internal static void Run()
    {
        var world = RouteScenarios.Create(5);
        world.Step();
        var original = world.Hash();
        Check(!world.Move([1, 1], new(900, 800)) && world.Hash() == original, "invalid IDs preserve authority");
        Check(!world.Move([1], new(-100, 800)) && world.Hash() == original, "invalid goal preserves authority");
        world.Stop([1]); world.Step();
        var stopped = world.Units.First().Position;
        for (var tick = 0; tick < 100; tick++) world.Step();
        Check(world.Units.First().Position == stopped && world.Units.First().Phase == WalkPhase.Stopped, "Stop cannot be recruited");
        var peers = world.Units.Single(body => body.Id == 3).Task;
        Check(world.Move([2], new(600, 1050)), "replacement submitted");
        world.Step();
        Check(world.Units.Single(body => body.Id == 3).Task == peers, "one replacement preserves peers' task");
        var json = world.Save();
        var restored = RouteWorld.Load(json);
        for (var i = 0; i < 100; i++)
        {
            world.Step(); restored.Step();
            Check(world.Hash() == restored.Hash(), "replacement/Stop recovery state");
            Check(JsonSerializer.Serialize(world.Events) == JsonSerializer.Serialize(restored.Events), "replacement events");
        }
        var single = new RouteWorld();
        single.Spawn(new(400, 640));
        single.Move([1], new(850, 640));
        single.Move([1], new(700, 1000), append: true);
        var completions = 0;
        var firstCompletion = -1L;
        for (var i = 0; i < 700; i++)
        {
            single.Step();
            if (single.Events.Any(item => item.Kind == "completed"))
            {
                if (firstCompletion < 0) firstCompletion = single.Frame;
                else Check(single.Frame > firstCompletion, "queued leg starts on a later frame");
                completions++;
            }
        }
        Check(completions == 2 && single.Units[0].Position == new FixedVector2(700, 1000), "append reaches both legs once");
        var dynamic = new RouteWorld();
        dynamic.Spawn(new(400, 640)); dynamic.Move([1], new(1500, 640)); dynamic.Step();
        Check(dynamic.SetWall(new(30, 17, 2, 6), true), "dynamic obstacle inserted");
        var copy = RouteWorld.Load(dynamic.Save());
        for (var i = 0; i < 400; i++)
        {
            dynamic.Step(); copy.Step();
            Check(dynamic.Hash() == copy.Hash(), "map revision recovery");
        }
        Check(dynamic.Units[0].Phase == WalkPhase.Idle, "dynamic obstacle route completes");
        var corrupt = JsonNode.Parse(single.Save())!;
        corrupt["Units"]![0]!["Position"]!["X"] = -1L;
        var rejected = false;
        try { RouteWorld.Load(corrupt.ToJsonString()); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException) { rejected = true; }
        Check(rejected, "invalid body snapshot rejected");
        var detached = RouteWorld.Load(single.Save());
        detached.Move([1], new(800, 1100)); detached.Step();
        Check(single.Units[0].Position == new FixedVector2(700, 1000), "worlds have independent state");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
