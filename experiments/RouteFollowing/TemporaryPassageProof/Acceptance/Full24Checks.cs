using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>From-start candidate acceptance shared by CLI and Godot; no simulation decisions use observations.</summary>
public static class Full24Checks
{
    public static bool Run(string folder, Action<string> output)
    {
        Directory.CreateDirectory(folder);
        var authority = RouteScenarios.Create(5);
        var saved = authority.Save();
        var preparation = Stopwatch.StartNew();
        RoutePreparation.Run();
        preparation.Stop();
        Check(saved == authority.Save(), "Preparation changed authority");
        output("full24_prepare=" + JsonSerializer.Serialize(new { elapsed_ms = preparation.Elapsed.TotalMilliseconds,
            authority_unchanged = true, format = RouteSettings.SnapshotFormat }));
        foreach (var scenario in new[] { "narrow", "same-direction", "roundtrip" })
        {
            if (Scenario(scenario, folder, output)) continue;
            output("full24_suite=FAIL stopped_at=" + scenario);
            return false;
        }
        RoutePlacementChecks.Run();
        RouteBoundaryChecks.Run();
        output("full24_placement_command_restore_boundaries=PASS");
        output("full24_suite=PASS");
        return true;
    }

    private static bool Scenario(string scenario, string folder, Action<string> output)
    {
        // Both open-ground cases contain24 members, unlike the old two-member scenario1.
        var world = RouteScenarios.Create(scenario == "narrow" ? 4 : 5);
        Check(world.Units.Count == 24 && world.Frame == 0, "Expected a fresh24-member world");
        File.WriteAllText(Path.Combine(folder, scenario + "-initial.json"), world.Save());
        var observations = new FrameSpikeLog(1290);
        using var transcript = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var peak = 0.0;
        var overBudget = 0;
        var stable = 0;
        var arrived = 0L;
        var frames = 0;
        var settledPositions = new Dictionary<ulong, FixedVector2>();
        try
        {
            for (var tick = 0; tick < 1290; tick++)
            {
                if (scenario == "roundtrip" && tick is 31 or 62 or 93 or 124)
                    Check(world.Move(world.Units.Select(body => body.Id),
                        tick % 62 == 0 ? new(1550, 640) : new(450, 640)), "Replacement input rejected");
                var before = world.Save();
                var previous = world.Units.ToDictionary(body => body.Id);
                var resumed = RouteWorld.Load(before);
                var counters = FrameSpikeLog.ReadCounters();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                world.Step();
                var end = Stopwatch.GetTimestamp();
                var afterCounters = FrameSpikeLog.ReadCounters();
                observations.Record((int)world.Frame, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, counters, afterCounters);
                frames++;
                var elapsed = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
                peak = Math.Max(peak, elapsed);
                overBudget += elapsed > 1000.0 / 30 ? 1 : 0;
                var hash = world.Hash();
                var events = JsonSerializer.Serialize(world.Events);
                output("full24_tick=" + JsonSerializer.Serialize(new { scenario, world.Frame, first = tick == 0,
                    elapsed_ms = elapsed, hash, world.Events }));
                if (elapsed > 1000.0 / 30 || world.Events.Any(item => item.Kind is "retry" or "failed"
                    or "yield_started" or "yield_finished" or "yield_cancelled"))
                {
                    File.WriteAllText(Path.Combine(folder, $"{scenario}-before-{world.Frame}.json"), before);
                    File.WriteAllText(Path.Combine(folder, $"{scenario}-after-{world.Frame}.json"), world.Save());
                }
                resumed.Step();
                Check(hash == resumed.Hash() && events == JsonSerializer.Serialize(resumed.Events), "Per-frame restore continuation");
                transcript.AppendData(Encoding.UTF8.GetBytes(hash + "\n" + events + "\n"));
                RoutePlacementChecks.AssertReserved(world);
                var units = world.Units;
                for (var i = 0; i < units.Count; i++)
                {
                    Check((units[i].Position - previous[units[i].Id].Position).Length <= 9 + SimScalar.FromRaw(32), "Speed/teleport");
                    for (var j = i + 1; j < units.Count; j++)
                        Check(SquaredDistance.Between(units[i].Position, units[j].Position).Raw
                            >= (Int128)62 * 62 * SimScalar.Scale * SimScalar.Scale, "Bodies overlap");
                }
                if (units.Any(body => body.Phase == WalkPhase.Failed)) break;
                if (units.All(body => body.Task == 0 && body.Pending.Count == 0 && body.Phase == WalkPhase.Idle))
                {
                    if (arrived == 0)
                    {
                        arrived = world.Frame;
                        settledPositions = units.ToDictionary(body => body.Id, body => body.Position);
                    }
                    else
                    {
                        Check(units.All(body => body.Position == settledPositions[body.Id]) && world.Events.Count == 0,
                            "Jitter or repeated completion after arrival");
                        if (++stable == 90) break;
                    }
                }
                if (world.Frame == 1200 && arrived == 0) break;
            }
        }
        finally
        {
            observations.Report(24, 0, "full24-" + scenario);
            File.WriteAllText(Path.Combine(folder, scenario + "-final.json"), world.Save());
        }
        var failures = world.Units.Where(body => body.Phase == WalkPhase.Failed).Select(body => body.Id).ToArray();
        var passed = failures.Length == 0 && arrived is > 0 and <= 1200 && stable == 90 && overBudget == 0;
        output("full24_result=" + JsonSerializer.Serialize(new { scenario, passed, frames, arrived, stable,
            failures, completed = world.Units.Count(body => body.Phase == WalkPhase.Idle),
            active = world.Units.Count(body => body.Task != 0), peak_ms = peak, over_budget = overBudget,
            hash = world.Hash(), trace_sha256 = Convert.ToHexString(transcript.GetHashAndReset()) }));
        return passed;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
