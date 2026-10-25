using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>Stop on a failed executed case, retaining every input body and timed first frame.</summary>
public static class RouteCloseoutChecks
{
    public static bool Run(Action<string> output)
    {
        var authority = RouteScenarios.Create(1);
        var before = authority.Save();
        var timer = Stopwatch.StartNew();
        RoutePreparation.Run();
        timer.Stop();
        Check(authority.Save() == before, "Preparation mutated authority");
        output(JsonSerializer.Serialize(new { preparation_ms = timer.Elapsed.TotalMilliseconds, authority_unchanged = true }));
        if (!Exercise("closeout-before-151.json", new ulong[] { 21 }, output)) return false;
        if (!Exercise("closeout-before-259.json", new ulong[] { 4, 23 }, output)) return false;
        RouteYieldChecks.Run(output);
        output("closeout_frozen_gates=PASS");
        return true;
    }

    private static bool Exercise(string name, ulong[] targets, Action<string> output)
    {
        using var stream = typeof(RouteCloseoutChecks).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var original = reader.ReadToEnd();
        var state = JsonSerializer.Deserialize<RouteSnapshot>(original)!;
        // Explicit fixture shape adaptation only: no physical state, goals, counters or task is repaired.
        var world = RouteWorld.Load(JsonSerializer.Serialize(state with { Format = RouteSettings.SnapshotFormat }));
        var fixedBodies = world.Units.Where(body => body.Phase is WalkPhase.Stopped or WalkPhase.Idle)
            .ToDictionary(body => body.Id, body => body.Position);
        var arrived = new Dictionary<ulong, long>();
        using var transcript = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var peak = 0.0;
        var overBudget = 0;
        var failures = new HashSet<ulong>();
        var revisions = 0;
        var finished = false;
        for (var tick = 0; tick < 1200; tick++)
        {
            var previous = world.Units.ToDictionary(body => body.Id);
            var resumed = RouteWorld.Load(world.Save());
            var watch = Stopwatch.StartNew();
            world.Step();
            watch.Stop();
            var elapsed = watch.Elapsed.TotalMilliseconds;
            peak = Math.Max(peak, elapsed);
            overBudget += elapsed > 1000.0 / 30 ? 1 : 0;
            // Write timing before any assertion, including the exact failing frame.
            output("closeout_tick=" + JsonSerializer.Serialize(new
            {
                name, frame = world.Frame, first = tick == 0, elapsed_ms = elapsed,
                selected = world.Units.Where(body => targets.Contains(body.Id)), world.Events
            }));
            resumed.Step();
            Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "Per-frame restore continuation");
            transcript.AppendData(Encoding.UTF8.GetBytes(world.Hash() + "\n" + JsonSerializer.Serialize(world.Events) + "\n"));
            RoutePlacementChecks.AssertReserved(world);
            foreach (var body in world.Units)
            {
                Check((body.Position - previous[body.Id].Position).Length <= 9 + SimScalar.FromRaw(32), "Speed/teleport");
                Check(!fixedBodies.TryGetValue(body.Id, out var fixedPoint) || body.Position == fixedPoint, "Stop/Idle moved");
                foreach (var other in world.Units.Where(other => other.Id > body.Id))
                    Check(SquaredDistance.Between(body.Position, other.Position).Raw >= (Int128)62 * 62 * SimScalar.Scale * SimScalar.Scale,
                        "Bodies overlap");
                if (targets.Contains(body.Id) && body.Phase is WalkPhase.Settled or WalkPhase.Idle && body.Position == body.Endpoint)
                    arrived.TryAdd(body.Id, world.Frame);
                if (body.Task != 0 && body.Task == previous[body.Id].Task && body.Endpoint != previous[body.Id].Endpoint) revisions++;
            }
            foreach (var ev in world.Events.Where(ev => ev.Kind == "failed")) failures.Add(ev.Id);
            if (targets.All(arrived.ContainsKey)) { finished = true; break; }
            if (targets.Any(failures.Contains)) break;
        }
        var passed = finished && overBudget == 0 && !targets.Any(failures.Contains);
        output("closeout_result=" + JsonSerializer.Serialize(new
        {
            name, targets, arrived, functional_passed = finished && !targets.Any(failures.Contains), passed,
            failed = failures, goal_revisions = revisions, peak_ms = peak, over_budget = overBudget,
            hash = world.Hash(), trace_sha256 = Convert.ToHexString(transcript.GetHashAndReset()),
            input_sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(original))),
            selected = world.Units.Where(body => targets.Contains(body.Id))
        }));
        if (!passed)
        {
            var folder = Environment.GetEnvironmentVariable("RTS_CLOSEOUT_EVIDENCE");
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, name + ".failure.json"), world.Save());
            }
            output("closeout_frozen_gates=FAIL stopped_before_next_case=true");
        }
        return passed;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
