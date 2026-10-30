using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Measures one populated candidate world; command admission failures are not split into later frames.</summary>
public static class MultiGroupChecks
{
    public static bool Run(int count, string folder, Action<string> output)
    {
        if (count is not (300 or 500)) throw new ArgumentOutOfRangeException(nameof(count));
        Directory.CreateDirectory(folder);
        var setup = Stopwatch.StartNew();
        var world = Create(count);
        setup.Stop();
        var initial = world.Save();
        File.WriteAllText(Path.Combine(folder, "initial.json"), initial);
        var prepare = Stopwatch.StartNew();
        RoutePreparation.Run();
        prepare.Stop();
        Check(world.Save() == initial, "Preparation changed authority");
        output("multi_prepare=" + JsonSerializer.Serialize(new { count, setup_ms = setup.Elapsed.TotalMilliseconds,
            prepare_ms = prepare.Elapsed.TotalMilliseconds, authority_unchanged = true,
            format = RouteSettings.SnapshotFormat, module = typeof(RouteWorld).Assembly.ManifestModule.ModuleVersionId,
            note = "Spawn fixture setup is separate; first command and all Step samples retained" }));

        var fresh = Measure(world, count, "fresh", folder, output, out var movingSnapshot);
        if (movingSnapshot is null)
        {
            output("multi_suite=FAIL later_cases=NOT_RUN reason=no_valid_moving_snapshot");
            return false;
        }
        var restoreStart = Stopwatch.GetTimestamp();
        var restored = RouteWorld.Load(movingSnapshot);
        output("multi_restore_ms=" + Stopwatch.GetElapsedTime(restoreStart).TotalMilliseconds.ToString("R"));
        var restorePassed = Measure(restored, count, "restore", folder, output, out _);
        var changed = RouteWorld.Load(movingSnapshot);
        var editStart = Stopwatch.GetTimestamp();
        // One real future route cell, ahead of all bodies at frame30; no body or final goal is overwritten.
        Check(changed.SetWall(new(25, 9, 1, 1), true), "Obstacle edit rejected");
        output("multi_obstacle_edit_ms=" + Stopwatch.GetElapsedTime(editStart).TotalMilliseconds.ToString("R"));
        var changedPassed = Measure(changed, count, "obstacle", folder, output, out _);
        var passed = fresh && restorePassed && changedPassed;
        output("multi_suite=" + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    internal static RouteWorld Create(int count)
    {
        var world = new RouteWorld(128, 128);
        // Three columns of1280-wide lanes, seven544-high rows fit the existing4096-square map limit.
        // Each party has at most24 bodies, with80-unit spacing and room for a640-unit eastward move.
        for (var index = 0; index < count; index++)
        {
            var party = index / 24;
            var member = index % 24;
            world.Spawn(new(128 + party % 3 * 1280 + member % 5 * 80,
                96 + party / 3 * 544 + member / 5 * 80));
        }
        return world;
    }

    internal static int[] Submit(RouteWorld world, int count)
    {
        var rejected = new List<int>();
        for (var party = 0; party < (count + 23) / 24; party++)
        {
            var members = Enumerable.Range(party * 24 + 1, Math.Min(24, count - party * 24)).Select(id => (ulong)id);
            if (!world.Move(members, new(928 + party % 3 * 1280, 256 + party / 3 * 544))) rejected.Add(party);
        }
        return rejected.ToArray();
    }

    private static bool Measure(RouteWorld world, int count, string scenario, string folder,
        Action<string> output, out string? movingSnapshot)
    {
        movingSnapshot = null;
        var input = world.Save();
        File.WriteAllText(Path.Combine(folder, scenario + "-input.json"), input);
        var replay = RouteWorld.Load(input);
        var log = new FrameSpikeLog(1290);
        var completed = new HashSet<ulong>();
        var errors = new List<string>();
        var peak = 0.0;
        var overBudget = 0;
        var stable = 0;
        var arrivedAt = -1;
        var measured = 0;
        var admissionRejected = Array.Empty<int>();
        var completionPositions = new Dictionary<ulong, FixedVector2>();
        try
        {
            for (var tick = 0; tick < 1290; tick++)
            {
                var before = world.Save();
                var counters = FrameSpikeLog.ReadCounters();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                if (tick == 0) admissionRejected = Submit(world, count);
                // A partial admitted prefix is not a valid500-unit load. Preserve it without executing it.
                if (admissionRejected.Length == 0) world.Step();
                var end = Stopwatch.GetTimestamp();
                var afterCounters = FrameSpikeLog.ReadCounters();
                log.Record(tick, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, counters, afterCounters);
                measured++;
                var elapsed = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
                overBudget += elapsed > 1000.0 / 30 ? 1 : 0;
                if (elapsed > peak)
                {
                    peak = elapsed;
                    File.WriteAllText(Path.Combine(folder, scenario + "-peak-before.json"), before);
                    File.WriteAllText(Path.Combine(folder, scenario + "-peak-after.json"), world.Save());
                }
                if (admissionRejected.Length != 0)
                {
                    errors.Add("command_admission_rejected");
                    output("multi_admission=" + JsonSerializer.Serialize(new { count, scenario,
                        expected_groups = (count + 23) / 24, rejected_zero_based_groups = admissionRejected,
                        stepped = false, world.Frame, elapsed_ms = elapsed }));
                    Check(RouteWorld.Load(world.Save()).Hash() == world.Hash(), "Partial admission snapshot cannot restore");
                    break;
                }
                if (tick == 0) Check(Submit(replay, count).Length == 0, "Replay admission differs");
                replay.Step();
                var hash = world.Hash();
                var events = JsonSerializer.Serialize(world.Events);
                Check(hash == replay.Hash() && events == JsonSerializer.Serialize(replay.Events), "Restored replay differs");
                output("multi_tick=" + JsonSerializer.Serialize(new { count, scenario, tick, world.Frame,
                    first = tick == 0, elapsed_ms = elapsed, hash, world.Events }));
                if (elapsed > 1000.0 / 30 || world.Events.Any(item => item.Kind is "failed" or "rejected"))
                {
                    File.WriteAllText(Path.Combine(folder, $"{scenario}-before-{world.Frame}.json"), before);
                    File.WriteAllText(Path.Combine(folder, $"{scenario}-after-{world.Frame}.json"), world.Save());
                }
                foreach (var item in world.Events.Where(item => item.Kind == "completed"))
                    Check(completed.Add(item.Id), "Duplicate completion");
                var units = world.Units;
                if (units.Any(body => body.Phase == WalkPhase.Failed) || world.Events.Any(item => item.Kind == "rejected"))
                {
                    errors.Add("execution_failed_or_rejected");
                    break;
                }
                if (scenario == "fresh" && world.Frame == 30)
                {
                    movingSnapshot = world.Save();
                    File.WriteAllText(Path.Combine(folder, "moving-frame30.json"), movingSnapshot);
                }
                if (completed.Count == count && units.All(body => body.Task == 0 && body.Phase == WalkPhase.Idle))
                {
                    if (arrivedAt < 0)
                    {
                        arrivedAt = tick;
                        completionPositions = units.ToDictionary(body => body.Id, body => body.Position);
                    }
                    else
                    {
                        Check(units.All(body => body.Position == completionPositions[body.Id]) && world.Events.Count == 0,
                            "Position or events changed after completion");
                        if (++stable == 90) break;
                    }
                }
                if (tick == 1199 && arrivedAt < 0) break;
            }
        }
        finally
        {
            log.Report(count, 0, "candidate-multi-" + scenario);
            File.WriteAllText(Path.Combine(folder, scenario + "-final.json"), world.Save());
        }
        var passed = errors.Count == 0 && completed.Count == count && stable == 90 && overBudget == 0;
        output("multi_result=" + JsonSerializer.Serialize(new { count, scenario, passed, measured, completed = completed.Count,
            stable, arrived_tick = arrivedAt, peak_ms = peak, over_budget = overBudget, errors,
            admission_rejected = admissionRejected, hash = world.Hash() }));
        return passed;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
