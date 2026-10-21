using System.Text.Json.Nodes;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Reference semantics, physical progress and terminal ownership regressions.</summary>
internal static class RouteReferenceChecks
{
    internal static void Run()
    {
        var failures = new List<string>();
        foreach (var check in new Action[] { StraightLookahead, EntryForward, ConnectionProgress, TerminalFailure })
        {
            try { check(); }
            catch (InvalidOperationException error) { failures.Add(error.Message); }
        }
        RouteProgressChecks.Run();
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("; ", failures));
    }

    private static void StraightLookahead()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 500));
        world.Move([1], new(1500, 650));
        world.Step();
        Check((world.FollowTarget(1) - world.Units[0].Position).Length > 128,
            "straight interpolation cannot stop lookahead at a false bend");
    }

    private static void EntryForward()
    {
        var world = RouteFixture.Load("reference-entry-399.json");
        var body = world.Units.Single(member => member.Id == 24);
        Check(world.FollowTarget(24).X > body.Position.X,
            "contracted entrance member follows a forward sample instead of reversing to the old point");
    }

    private static void ConnectionProgress()
    {
        var world = RouteFixture.Load("reference-progress-789.json");
        var before = world.Units.Single(member => member.Id == 16);
        world.Step();
        var after = world.Units.Single(member => member.Id == 16);
        Check((after.Position - before.Position).Length < 10 && after.WaitTicks >= before.WaitTicks,
            "a short connection cannot claim 32 units of progress by advancing its target cursor");
    }

    private static void TerminalFailure()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 500));
        world.Spawn(new(400, 580));
        world.Move([1, 2], new(1550, 640));
        world.Step();
        var json = JsonNode.Parse(world.Save())!;
        var first = json["Units"]![0]!;
        first["Phase"] = (int)WalkPhase.Waiting;
        first["WaitTicks"] = 360;
        first["RetryAtWait"] = 360;
        first["Recoveries"] = 3;
        first["Blocker"] = null;
        first["Connection"] = null;
        world = RouteWorld.Load(json.ToJsonString());
        var route = world.Paths.Single();
        world.Step();
        Check(world.Units[0].Phase == WalkPhase.Failed && world.Events.Count(item => item.Id == 1 && item.Kind == "failed") == 1,
            "exhausted member fails exactly once");
        Check(world.Paths.Single().Version == route.Version && world.Paths.Single().Points.SequenceEqual(route.Points)
            && world.Events.All(item => item.Id != 1 || item.Kind != "retry"),
            "terminal member cannot replace the shared reference or publish a retry");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
