using System.Text.Json;
using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

internal static class RouteYieldChecks
{
    internal static RouteWorld Load(string name)
    {
        using var stream = typeof(RouteYieldChecks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing yield fixture.");
        var state = JsonSerializer.Deserialize<RouteSnapshot>(stream)!;
        // Shape adaptation only; no goal, position, wait or route is repaired.
        return RouteWorld.Load(JsonSerializer.Serialize(state with { Format = RouteSettings.SnapshotFormat }));
    }

    internal static void Warmup()
    {
        foreach (var fixture in new[] { "yield-single-329.json", "yield-pair-444.json" })
        {
            var disposable = Load(fixture);
            for (var i = 0; i < 60; i++) disposable.Step();
        }
    }

    internal static void Run(Action<string>? output = null)
    {
        output ??= Console.WriteLine;
        Exercise("yield-single-329.json", 13, false, output);
        Exercise("yield-pair-444.json", 7, true, output);
        RouteYieldBoundaries.Run();
        output("route_yield_boundaries=PASS");
    }

    private static void Exercise(string fixture, ulong root, bool pair, Action<string> output)
    {
        var world = Load(fixture);
        var before = world.Units.ToDictionary(body => body.Id);
        var events = new List<RouteEvent>();
        var firstMovement = new Dictionary<ulong, long>();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var samples = new List<object>();
        var peak = 0.0;
        var overBudget = 0;
        for (var tick = 0; tick < 60; tick++)
        {
            var previous = world.Units.ToDictionary(body => body.Id);
            var resumed = RouteWorld.Load(world.Save());
            var timer = Stopwatch.StartNew();
            world.Step();
            timer.Stop();
            var ms = timer.Elapsed.TotalMilliseconds;
            peak = Math.Max(peak, ms);
            overBudget += ms > 1000.0 / 30 ? 1 : 0;
            samples.Add(new { fixture, frame = world.Frame, elapsed_ms = ms, first = tick == 0 });
            resumed.Step();
            if (world.Hash() != resumed.Hash() || !world.Events.SequenceEqual(resumed.Events)) throw new Exception("Yield JSON continuation.");
            digest.AppendData(Encoding.UTF8.GetBytes(world.Hash() + "\n" + JsonSerializer.Serialize(world.Events) + "\n"));
            events.AddRange(world.Events);
            RoutePlacementChecks.AssertReserved(world);
            foreach (var body in world.Units)
            {
                if ((body.Position - previous[body.Id].Position).Length > 9 + SimScalar.FromRaw(32)) throw new Exception($"Yield exceeded speed: {body.Id} at {world.Frame}, distance={(body.Position - previous[body.Id].Position).Length.ToPresentationDouble():R}, phase={body.Phase}.");
                if (body.Position != before[body.Id].Position) firstMovement.TryAdd(body.Id, world.Frame);
                foreach (var other in world.Units.Where(other => other.Id > body.Id))
                    if ((body.Position - other.Position).Length < 62) throw new Exception("Yield overlapped bodies.");
            }
        }
        foreach (var sample in samples) output("yield_tick=" + JsonSerializer.Serialize(sample));
        output("yield_result=" + JsonSerializer.Serialize(new { fixture, events, firstMovement, peak_ms = peak,
            over_budget = overBudget, hash = world.Hash(), trace_sha256 = Convert.ToHexString(digest.GetHashAndReset()),
            rootPosition = world.Units.Single(body => body.Id == root).Position }));
        if (!events.Any(item => item.Kind == "yield_finished") || !firstMovement.ContainsKey(root))
            throw new InvalidOperationException("Frozen blocker did not finish bounded cooperation.");
        var start = events.First(item => item.Kind == "yield_started");
        if (!pair && start.Detail != "13") throw new InvalidOperationException("Expected single-member yield.");
        if (pair && start.Detail != "18,7") throw new InvalidOperationException("Expected frozen 18 then 7 sequence.");
        if (pair && firstMovement[18] >= firstMovement[7]) throw new InvalidOperationException("Yield sequence lost order.");
        if (overBudget != 0) throw new InvalidOperationException("Frozen cooperation exceeded 30Hz; stop before full regression.");
        output(pair ? "route_yield_pair=PASS" : "route_yield_single=PASS");
    }
}
