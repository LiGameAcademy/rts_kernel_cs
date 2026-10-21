using System.Text.Json;
using System.Text.Json.Nodes;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Physical projection cannot be earned by detour bookkeeping, version changes or oscillation.</summary>
internal static class RouteProgressChecks
{
    internal static void Run()
    {
        RealCorner();
        ReverseThenForward();
        VersionAndLifecycle();
        InvalidState();
    }

    private static RouteWorld Straight()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 640));
        world.Move([1], new(1500, 640));
        world.Step();
        return world;
    }

    private static RouteWorld Restore(RouteSnapshot state) => RouteWorld.Load(JsonSerializer.Serialize(state));
    private static RouteSnapshot Read(RouteWorld world) => JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;

    private static void RealCorner()
    {
        var state = Read(Straight());
        var points = new FixedVector2[] { new(400, 500), new(500, 500), new(600, 500), new(700, 500),
            new(800, 500), new(800, 600), new(800, 700), new(800, 800), new(800, 900) };
        state = state with
        {
            Tasks = [state.Tasks[0] with { Points = points, Corners = [4], Anchor = points[^1] }],
            Units = [state.Units[0] with { Position = new(620, 500), Preference = points[^1], Endpoint = points[^1],
                Cursor = 2, Progress = 0, ProgressCursor = 0, ProgressVersion = 0 }]
        };
        var world = Restore(state);
        Check(world.FollowTarget(1) == new FixedVector2(800, 500), "real bend is retained while passed samples advance");
        world.Step();
        Check(world.Units[0].ProgressCursor == 4, "physical progress uses the real segment, not the lookahead cursor");
        var hash = world.Hash();
        ((int[])world.Paths[0].Corners)[0] = 1;
        Check(world.Hash() == hash, "published corner metadata cannot mutate authority");
        var resumed = RouteWorld.Load(world.Save());
        world.Step(); resumed.Step();
        Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "corner progress round trip");
    }

    private static void ReverseThenForward()
    {
        var state = Read(Straight());
        var before = state.Units[0];
        var west = before.Position - new FixedVector2(80, 0);
        state = state with { Units = [before with { Progress = 20, WaitTicks = 50, Recoveries = 2,
            Facing = -SimMath.Pi, Velocity = default,
            Connection = new(west, before.Cursor, state.Tasks[0].Version, [before.Position, west], 1) }] };
        var world = Restore(state);
        world.Step();
        var back = world.Units[0];
        Check(back.Position.X < before.Position.X && back.Progress < 20 && back.WaitTicks == 50 && back.Recoveries == 2,
            "physical backward motion subtracts progress and preserves wait history");
        var stateAfterBack = Read(world);
        world = Restore(stateAfterBack with { Units = [back with { Connection = null, Facing = 0, Velocity = default }] });
        world.Step();
        Check(world.Units[0].Progress <= 20 && world.Units[0].WaitTicks == 50,
            "back-and-forth cannot manufacture a new progress threshold");
    }

    private static void VersionAndLifecycle()
    {
        var state = Read(Straight());
        var member = state.Units[0] with { Progress = 28, WaitTicks = 50, Recoveries = 2 };
        var world = Restore(state with { Units = [member], Tasks = [state.Tasks[0] with { Version = 2 }] });
        world.Step();
        var changed = world.Units[0];
        Check(changed.ProgressVersion == 2 && changed.Progress < 10 && changed.WaitTicks == 50 && changed.Recoveries == 2,
            "reference version changes only the physical baseline, never recovery history");
        var resumed = RouteWorld.Load(world.Save());
        for (var i = 0; i < 40; i++)
        {
            world.Step(); resumed.Step();
            Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "warm progress continuation");
        }
        world.Stop([1]); world.Step();
        var stopped = world.Units[0];
        Check(stopped.Progress == 0 && stopped.ProgressCursor == 0 && stopped.ProgressVersion == 0,
            "Stop releases all physical progress state");
        world.Move([1], new(1600, 800)); world.Step();
        Check(world.Units[0].Task != stopped.Task && world.Units[0].Recoveries == 0 && world.Units[0].WaitTicks == 0,
            "replacement owns a fresh progress baseline");
    }

    private static void InvalidState()
    {
        var world = Straight();
        void Rejected(Action<JsonNode> corrupt, string message)
        {
            var json = JsonNode.Parse(world.Save())!;
            corrupt(json);
            var rejected = false;
            try { RouteWorld.Load(json.ToJsonString()); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        Rejected(json => json["Units"]![0]!["ProgressCursor"] = 0, "missing physical segment rejected");
        Rejected(json => json["Units"]![0]!["ProgressVersion"] = 99, "future physical version rejected");
        Rejected(json => json["Units"]![0]!["Progress"] = SimScalar.FromInteger(32).Raw, "uncommitted progress threshold rejected");
        Rejected(json => json["Tasks"]![0]!["Corners"] = null, "missing corner metadata rejected");
        Rejected(json => json["Tasks"]![0]!["Corners"] = new JsonArray(0), "invalid corner index rejected");
        foreach (var format in new[] { 1, 2, 3, 4, 5 })
            Rejected(json => json["Format"] = format, "old experimental snapshot rejected");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
