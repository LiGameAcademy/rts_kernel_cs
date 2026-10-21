using System.Text.Json.Nodes;
using Rts.Movement.Prototype;

internal static class RouteYieldBoundaries
{
    internal static void Run()
    {
        foreach (var blocker in new ulong[] { 13, 18 })
        {
            var world = RouteYieldChecks.Load(blocker == 13 ? "yield-single-329.json" : "yield-pair-444.json");
            world.Stop([blocker]);
            world.Step();
            var position = world.Units.Single(body => body.Id == blocker).Position;
            for (var i = 0; i < 60; i++)
            {
                world.Step();
                Check(world.Units.Single(body => body.Id == blocker).Position == position, "Stop cannot yield");
                Check(world.Events.All(e => e.Kind != "yield_leg_started" || e.Id != blocker), "Stop cannot be recruited");
            }
        }
        var idleJson = JsonNode.Parse(RouteYieldChecks.Load("yield-single-329.json").Save())!;
        var idle = idleJson["Units"]!.AsArray().Single(body => body!["Id"]!.GetValue<ulong>() == 13)!;
        idle["Task"] = 0;
        idle["Phase"] = (int)WalkPhase.Idle;
        idle["TransitQuarters"] = 4;
        idle["Progress"] = 0;
        idle["ProgressCursor"] = 0;
        idle["ProgressVersion"] = 0;
        idle["PlacementCheckedRevision"] = -1;
        var idleWorld = RouteWorld.Load(idleJson.ToJsonString());
        var idlePosition = idleWorld.Units.Single(body => body.Id == 13).Position;
        for (var i = 0; i < 60; i++)
        {
            idleWorld.Step();
            Check(idleWorld.Units.Single(body => body.Id == 13).Position == idlePosition, "ordinary Idle stays fixed");
        }
        foreach (var action in new[] { "stop", "replacement", "append", "requester" }) Cancel(action);
        var changed = Started();
        Check(changed.SetWall(new(3, 3, 1, 1), true), "unrelated terrain change accepted");
        var changedCopy = RouteWorld.Load(changed.Save());
        changed.Step();
        changedCopy.Step();
        Check(changed.Events.Any(e => e.Kind == "yield_cancelled") && changed.Hash() == changedCopy.Hash(), "map revision cancels plan across restore");
        var exhaustedJson = JsonNode.Parse(RouteYieldChecks.Load("yield-single-329.json").Save())!;
        var requester = exhaustedJson["Units"]!.AsArray().Single(b => b!["Id"]!.GetValue<ulong>() == 24)!;
        requester["Recoveries"] = 3;
        var exhausted = RouteWorld.Load(exhaustedJson.ToJsonString());
        exhausted.Step();
        Check(exhausted.Units.Single(b => b.Id == 24).Phase == WalkPhase.Failed
            && exhausted.Events.All(e => e.Kind != "yield_started"), "cooperation cannot bypass existing recovery limit");
        var owned = Started();
        var view = owned.Paths.Single(task => task.Yield is not null);
        ((ulong[])view.Yield!.Members)[0] = 499;
        Check(owned.Paths.Single(task => task.Yield is not null).Yield!.Members[0] == 18, "yield view owns its array");
        foreach (var field in new[] { "Next", "StartedAt", "Members" })
        {
            var json = JsonNode.Parse(owned.Save())!;
            var plan = json["Tasks"]!.AsArray().Single(t => t!["Yield"] is not null)!["Yield"]!;
            if (field == "Members") plan[field] = new JsonArray(18, 18);
            else plan[field] = -1;
            var rejected = false;
            try { RouteWorld.Load(json.ToJsonString()); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "corrupt yield plan rejected");
        }
    }

    private static RouteWorld Started()
    {
        var world = RouteYieldChecks.Load("yield-pair-444.json");
        world.Step();
        Check(world.Paths.Single(task => task.Id == 5).Yield is { Next: 0 }, "pair started naturally");
        return world;
    }

    private static void Cancel(string action)
    {
        var world = Started();
        var oldTask = world.Units.Single(body => body.Id == 7).Task;
        var stopped = world.Units.Single(body => body.Id == 7).Position;
        if (action == "stop") world.Stop([7]);
        else world.Move([action == "requester" ? 4UL : 7UL], new(1850, 1050), append: action == "append");
        world.Step();
        Check(world.Paths.All(task => task.Yield is null), "control change cancels old plan");
        var copy = RouteWorld.Load(world.Save());
        for (var tick = 0; tick < 30; tick++)
        {
            world.Step();
            copy.Step();
            RoutePlacementChecks.AssertReserved(world);
            Check(world.Hash() == copy.Hash() && world.Events.SequenceEqual(copy.Events), "cancel/queue JSON continuation");
            if (action == "stop") Check(world.Units.Single(body => body.Id == 7).Position == stopped, "stopped pending leg never moves");
            if (action == "replacement") Check(world.Units.Single(body => body.Id == 7).Task != oldTask, "old task cannot regain control");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
