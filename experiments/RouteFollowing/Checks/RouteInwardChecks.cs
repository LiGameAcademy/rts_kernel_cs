using System.Text.Json.Nodes;
using Rts.Kernel;
using Rts.Movement.Prototype;

/// <summary>Historical traffic states exercise inward steps after explicit test-only goal reservation repair.</summary>
internal static class RouteInwardChecks
{
    internal static void Run()
    {
        var upper = Fixture(120);
        var before = upper.Units.ToDictionary(body => body.Id);
        var originalHash = upper.Hash();
        Check(!upper.Move([10, 10], new(600, 800)) && upper.Hash() == originalHash,
            "invalid replacement preserves inward state");
        upper.Step();
        Check(upper.Units.Any(body => body.Position != before[body.Id].Position),
            "upper frozen entrance produces safe movement with the corrected reference");
        Check(JsonNode.Parse(upper.Save())!["Format"]!.GetValue<int>() == 6, "experimental format covers inward state");
        Safe(before, upper);
        var lower = Fixture(47);
        lower.Step();
        Check(lower.Units.Any(body => body.TransitQuarters < 4), "lower entrance still exercises accepted contraction");
        Continuation(upper);
        Lifecycle(upper);
        Corrupt(upper, -1);
        Corrupt(upper, 5);
        foreach (var format in new[] { 1, 2, 3, 4, 5 })
        {
            var legacy = JsonNode.Parse(upper.Save())!;
            legacy["Format"] = format;
            var rejected = false;
            try { RouteWorld.Load(legacy.ToJsonString()); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "old experimental format is not silently accepted");
        }
    }

    private static void Continuation(RouteWorld initial)
    {
        var first = RouteWorld.Load(initial.Save());
        var restored = RouteWorld.Load(initial.Save());
        var contractions = 0;
        for (var frame = 0; frame < 180; frame++)
        {
            var before = first.Units.ToDictionary(body => body.Id);
            var quarters = before.Keys.ToDictionary(id => id, id => Quarter(first, id));
            first.Step();
            restored.Step();
            Check(first.Hash() == restored.Hash() && first.Events.SequenceEqual(restored.Events), "inward JSON continuation");
            foreach (var body in first.Units)
                if (body.Task != 0 && body.Task == before[body.Id].Task)
                    Check(Quarter(first, body.Id) <= quarters[body.Id], "same task never expands its transit offset");
            foreach (var body in first.Units)
                if (body.Connection is { } connection)
                    Check(connection.TransitQuarters is >= 0 and <= 4
                        && connection.TransitQuarters <= Quarter(first, body.Id), "connection preserves a permitted inward cap");
            contractions += first.Units.Count(body => body.Task == before[body.Id].Task
                && body.Task != 0 && Quarter(first, body.Id) < quarters[body.Id]
                && body.Position != before[body.Id].Position);
            Safe(before, first);
        }
        Check(contractions > 0, "continuation exercises real accepted inward translations");
    }

    private static void Lifecycle(RouteWorld initial)
    {
        var stopped = RouteWorld.Load(initial.Save());
        var position = stopped.Units.Single(body => body.Id == 10).Position;
        stopped.Stop([10]);
        for (var frame = 0; frame < 90; frame++) stopped.Step();
        Check(stopped.Units.Single(body => body.Id == 10).Position == position && Quarter(stopped, 10) == 4,
            "Stop clears inward state and never moves");
        var replaced = RouteWorld.Load(initial.Save());
        replaced.Move([10], new(400, 1050));
        replaced.Step();
        Check(replaced.Units.Single(body => body.Id == 10).Task != initial.Units.Single(body => body.Id == 10).Task
            && Quarter(replaced, 10) == 4, "new command owns fresh inward state");
        var queued = RouteWorld.Load(initial.Save());
        var cap = Quarter(queued, 10);
        queued.Move([10], new(400, 1050), append: true);
        queued.Step();
        Check(Quarter(queued, 10) <= cap && queued.Units.Single(body => body.Id == 10).Pending.Count == 1,
            "append preserves current contraction until takeover");
        // Construct a legal single-member settled state at its reserved goal.
        // This isolates queue handover from the narrow-route traversal under test above.
        var handover = new RouteWorld();
        var id = handover.Spawn(new(400, 640));
        handover.Move([id], new(850, 640));
        handover.Step();
        handover.Move([id], new(700, 1000), append: true);
        handover.Step();
        var json = JsonNode.Parse(handover.Save())!;
        var member = json["Units"]![0]!;
        member["Position"] = member["Endpoint"]!.DeepClone();
        member["Phase"] = (int)WalkPhase.Settled;
        member["Blocker"] = null;
        member["Connection"] = null;
        member["TransitQuarters"] = 2;
        member["Progress"] = 0;
        member["ProgressCursor"] = 0;
        member["ProgressVersion"] = 0;
        handover = RouteWorld.Load(json.ToJsonString());
        handover.Step();
        Check(handover.Units.Single().Task == 0, "settled queued member releases before next frame");
        handover.Step();
        Check(Quarter(handover, id) == 4 && handover.Units.Single().Pending.Count == 0,
            "queued task resets inward state");
    }

    private static RouteWorld Fixture(int frame) => RouteFixture.Load($"narrow-entry-{frame}.json");

    private static int Quarter(RouteWorld world, ulong id) => JsonNode.Parse(world.Save())!["Units"]!.AsArray()
        .Single(node => node!["Id"]!.GetValue<ulong>() == id)!["TransitQuarters"]?.GetValue<int>() ?? 4;

    private static void Corrupt(RouteWorld world, int quarter)
    {
        var json = JsonNode.Parse(world.Save())!;
        json["Units"]![0]!["TransitQuarters"] = quarter;
        var rejected = false;
        try { RouteWorld.Load(json.ToJsonString()); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "invalid inward cap rejected");
    }

    private static void Safe(IReadOnlyDictionary<ulong, Walker> before, RouteWorld world)
    {
        var bodies = world.Units;
        foreach (var body in bodies)
            Check((body.Position - before[body.Id].Position).Length <= 10, "inward step respects speed");
        for (var i = 0; i < bodies.Count; i++)
        for (var j = i + 1; j < bodies.Count; j++)
            Check((bodies[i].Position - bodies[j].Position).Length >= 62, "inward step keeps bodies separate");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
