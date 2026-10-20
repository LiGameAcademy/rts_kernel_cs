using Rts.Kernel.Numerics;
using Rts.Kernel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rts.Movement.Prototype;

/// <summary>Task-wide goal ownership survives replacement, waiting and snapshot restoration.</summary>
internal static class RoutePlacementChecks
{
    internal static void Run()
    {
        var world = RouteScenarios.Create(5);
        var changes = 0;
        for (var tick = 0; tick < 125; tick++)
        {
            if (tick is 31 or 62 or 93 or 124)
                world.Move(world.Units.Select(body => body.Id), tick % 62 == 0 ? new(1550, 640) : new(450, 640));
            var before = world.Units.ToDictionary(body => body.Id);
            world.Step();
            AssertReserved(world);
            changes += world.Units.Count(body => body.Task != 0 && body.Task == before[body.Id].Task
                && body.Endpoint != before[body.Id].Endpoint);
        }
        Check(changes == 0, "transient teammates cannot repeatedly change task-owned endpoints");
        FixedBlockerAndQueue();
        NoReplacement();
        TerrainRepair();
        InvalidReservations();
    }

    private static void FixedBlockerAndQueue()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 640));
        world.Move([1], new(1500, 640));
        world.Step();
        var preference = world.Units[0].Preference;
        world.Spawn(preference, stopped: true);
        world.Step();
        var goal = world.Units[0].Endpoint;
        Check(goal != preference && (goal - preference).Length <= 156,
            "fixed blocker repairs a goal within its original envelope");
        Check(world.Events.Count(item => item.Kind == "goal_reassigned") == 1, "goal repair publishes once");
        world.Move([1], new(800, 1050), append: true);
        world.Step();
        var copy = RouteWorld.Load(world.Save());
        for (var tick = 0; tick < 80; tick++)
        {
            world.Step();
            copy.Step();
            AssertReserved(world);
            Check(world.Hash() == copy.Hash() && world.Events.SequenceEqual(copy.Events), "queued reservations resume identically");
            Check(world.Events.All(item => item.Kind != "goal_reassigned"), "unchanged fixed context cannot churn a repaired goal");
            Check(world.Units[1].Phase == WalkPhase.Stopped && world.Units[1].Position == preference, "Stop stays fixed");
        }
        world.Stop([1]);
        world.Step();
        Check(world.Units[0].Task == 0 && world.Units[0].Pending.Count == 0 && world.Units[0].PlacementCheckedRevision == -1,
            "Stop releases active and queued reservations");
        world.Move([1], new(700, 1100));
        world.Step();
        AssertReserved(world);
        Check(world.Units[0].Task != 0 && world.Units[0].Endpoint != goal, "replacement owns its own goal");
    }

    private static void NoReplacement()
    {
        var world = new RouteWorld(8, 8);
        world.Spawn(new(48, 48));
        world.Move([1], new(208, 208));
        world.Step();
        foreach (var y in new[] { 80, 144, 208 })
            foreach (var x in new[] { 80, 144, 208 })
                if (x != 80 || y != 80) world.Spawn(new(x, y), stopped: true);
        var events = 0;
        var goal = world.Units[0].Endpoint;
        world.Step();
        events += world.Events.Count(item => item.Kind == "goal_repair_blocked");
        var copy = RouteWorld.Load(world.Save());
        for (var i = 0; i < 20; i++)
        {
            world.Step();
            copy.Step();
            events += world.Events.Count(item => item.Kind == "goal_repair_blocked");
            Check(world.Hash() == copy.Hash() && world.Events.SequenceEqual(copy.Events), "failed repair cache resumes");
            Check(world.Units[0].Endpoint == goal, "failed repair preserves the previous reservation");
        }
        Check(events == 1, "unchanged blocked context is not searched again every tick");
    }

    private static void TerrainRepair()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 640));
        world.Move([1], new(1500, 640));
        world.Step();
        var goal = world.Units[0].Endpoint;
        Check(world.SetWall(new(46, 19, 2, 3), true), "goal terrain invalidated without touching the body");
        world.Step();
        Check(world.Units[0].Endpoint != goal && world.Events.Any(item => item.Kind == "goal_reassigned" && item.Detail == "terrain_invalid"),
            "terrain change repairs through the goal owner");
        var copy = RouteWorld.Load(world.Save());
        for (var i = 0; i < 20; i++)
        {
            world.Step();
            copy.Step();
            Check(world.Hash() == copy.Hash() && world.Events.SequenceEqual(copy.Events), "terrain repair resumes");
        }
    }

    private static void InvalidReservations()
    {
        var world = new RouteWorld();
        world.Spawn(new(400, 500));
        world.Spawn(new(400, 580));
        world.Move([1, 2], new(1500, 640));
        world.Step();
        void Reject(JsonNode json, string message)
        {
            var rejected = false;
            try { RouteWorld.Load(json.ToJsonString()); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        var json = JsonNode.Parse(world.Save())!;
        json["Units"]![1]!["Endpoint"] = json["Units"]![0]!["Endpoint"]!.DeepClone();
        Reject(json, "duplicate active reservation rejected");
        world.Move([1, 2], new(900, 1000), append: true);
        world.Step();
        json = JsonNode.Parse(world.Save())!;
        json["Units"]![1]!["Pending"]![0]!["Preference"] = json["Units"]![0]!["Pending"]![0]!["Preference"]!.DeepClone();
        Reject(json, "duplicate queued reservation rejected");
        json = JsonNode.Parse(world.Save())!;
        json["PlacementRevision"] = -1;
        Reject(json, "negative context revision rejected");
        json = JsonNode.Parse(world.Save())!;
        json["Units"]![0]!["PlacementCheckedRevision"] = long.MaxValue;
        Reject(json, "future claim check rejected");
        json = JsonNode.Parse(world.Save())!;
        json["Units"]![0]!["Endpoint"]!["X"] = SimScalar.FromInteger(400).Raw;
        Reject(json, "goal outside original envelope rejected");
        foreach (var format in new[] { 1, 2, 3, 4, 5 })
        {
            json = JsonNode.Parse(world.Save())!;
            json["Format"] = format;
            Reject(json, "old experimental format rejected");
        }
    }

    internal static void AssertReserved(RouteWorld world)
    {
        var claims = world.Units.SelectMany(body =>
        {
            var entries = body.Pending.Select(goal => (Id: body.Id, goal.Task, Point: goal.Preference)).ToList();
            if (body.Task != 0) entries.Add((body.Id, body.Task, body.Endpoint));
            return entries;
        }).ToArray();
        for (var i = 0; i < claims.Length; i++)
            for (var j = i + 1; j < claims.Length; j++)
                if (claims[i].Task == claims[j].Task)
                    Check(claims[i].Id != claims[j].Id && (claims[i].Point - claims[j].Point).Length >= 64,
                        $"task {claims[i].Task} has overlapping endpoint claims for {claims[i].Id}/{claims[j].Id} at frame {world.Frame}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
