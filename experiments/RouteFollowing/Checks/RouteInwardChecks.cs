using System.Text.Json.Nodes;
using Rts.Kernel;
using Rts.Movement.Prototype;

/// <summary>Frozen pre-fix traffic states expose the missed inward steps without changing their inputs.</summary>
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
        Check(Quarter(upper, 10) < 4 && upper.Units.Single(body => body.Id == 10).Position != before[10].Position,
            "upper traffic front takes a safe inward step");
        Check(Quarter(upper, 3) < 4, "inner traffic front contracts on the same reference point");
        Check(JsonNode.Parse(upper.Save())!["Format"]!.GetValue<int>() == 2, "experimental format covers inward state");
        Safe(before, upper);
        var lower = Fixture(47);
        lower.Step();
        Check(Quarter(lower, 5) < 4, "lower front uses its inward opportunity before it closes");
        Continuation(upper);
        Lifecycle(upper);
        Corrupt(upper, -1);
        Corrupt(upper, 5);
        var legacy = JsonNode.Parse(upper.Save())!;
        legacy["Format"] = 1;
        var rejected = false;
        try { RouteWorld.Load(legacy.ToJsonString()); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "old experimental format is not silently accepted");
    }

    private static void Continuation(RouteWorld initial)
    {
        var first = RouteWorld.Load(initial.Save());
        var restored = RouteWorld.Load(initial.Save());
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
            Safe(before, first);
        }
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
        // Exercise the existing settled-member/queued-leg handover with an explicit legal state.
        var json = JsonNode.Parse(queued.Save())!;
        var member = json["Units"]!.AsArray().Single(node => node!["Id"]!.GetValue<ulong>() == 10)!;
        member["Phase"] = (int)WalkPhase.Settled;
        member["Endpoint"] = member["Position"]!.DeepClone();
        member["Blocker"] = null;
        member["Connection"] = null;
        queued = RouteWorld.Load(json.ToJsonString());
        queued.Step();
        Check(queued.Units.Single(body => body.Id == 10).Task == 0, "settled queued member releases before next frame");
        queued.Step();
        Check(Quarter(queued, 10) == 4 && queued.Units.Single(body => body.Id == 10).Pending.Count == 0,
            "queued task resets inward state");
    }

    private static RouteWorld Fixture(int frame)
    {
        using var stream = typeof(RouteInwardChecks).Assembly.GetManifestResourceStream($"narrow-entry-{frame}.json")
            ?? throw new InvalidOperationException("Missing frozen fixture.");
        using var reader = new StreamReader(stream);
        var json = JsonNode.Parse(reader.ReadToEnd())!;
        // Explicit test setup, not a runtime migration of the historical v1 snapshot.
        json["Format"] = JsonNode.Parse(new RouteWorld().Save())!["Format"]!.DeepClone();
        foreach (var node in json["Units"]!.AsArray()) node!["TransitQuarters"] = 4;
        return RouteWorld.Load(json.ToJsonString());
    }

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
