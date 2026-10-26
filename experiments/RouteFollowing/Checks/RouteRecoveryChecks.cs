using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Automatic recovery in the unmodified complete world; no blocker or counter is repaired.</summary>
public static class RouteRecoveryChecks
{
    public static void Frozen(Action<string> output)
    {
        var world = RouteYieldChecks.Load("recovery-blockers-before-388.json");
        var saved = world.Save();
        var watch = Stopwatch.StartNew();
        RoutePreparation.Run(); RouteYieldChecks.Warmup();
        watch.Stop();
        Check(world.Save() == saved, "Preparation changed authority");
        output("recovery_prepare_ms=" + watch.Elapsed.TotalMilliseconds.ToString("R"));
        var stable = 0;
        var completedAt = 0L;
        var frozenPositions = new Dictionary<ulong, FixedVector2>();
        var automatic = false;
        var peaks = 0.0;
        var over = 0;
        for (var tick = 0; tick < 1200; tick++)
        {
            var restored = RouteWorld.Load(world.Save());
            var previous = world.Units.ToDictionary(body => body.Id);
            var cpu = RecoveryClock.Read();
            watch.Restart(); world.Step(); watch.Stop();
            var cpuElapsed = RecoveryClock.Read() - cpu;
            var elapsed = watch.Elapsed.TotalMilliseconds;
            peaks = Math.Max(peaks, elapsed); over += elapsed > 1000.0 / 30 ? 1 : 0;
            output("recovery_tick=" + JsonSerializer.Serialize(new { world.Frame, elapsed_ms = elapsed,
                first = tick == 0, thread_cpu_ms = cpuElapsed, hash = world.Hash(), events = world.Events }));
            restored.Step();
            Check(world.Hash() == restored.Hash() && world.Events.SequenceEqual(restored.Events), "Per-frame restore");
            RoutePlacementChecks.AssertReserved(world);
            foreach (var body in world.Units)
            {
                Check((body.Position - previous[body.Id].Position).Length <= 9 + SimScalar.FromRaw(32), "Teleport");
                foreach (var other in world.Units.Where(other => other.Id > body.Id))
                    Check((body.Position - other.Position).Length >= 62, "Body overlap");
            }
            if (world.Events.Any(ev => ev.Kind == "yield_started" && ev.Id == 23))
            {
                var plan = world.Paths.Single(task => task.Id == previous[23].Task).Yield;
                automatic |= plan is not null && plan.Members.Contains(4UL) && plan.Members.Contains(13UL);
            }
            Check(!world.Events.Any(ev => ev.Kind == "failed"), "Original frozen group failed before 24/24");
            if (world.Units.All(body => body.Task == 0))
            {
                if (completedAt == 0)
                {
                    completedAt = world.Frame;
                    frozenPositions = world.Units.ToDictionary(body => body.Id, body => body.Position);
                }
                else
                {
                    Check(world.Units.All(body => body.Position == frozenPositions[body.Id]), "Settled jitter");
                    Check(world.Events.Count == 0, "Repeated completion");
                    if (++stable == 90) break;
                }
            }
        }
        output("recovery_result=" + JsonSerializer.Serialize(new { automatic, completed_at = completedAt,
            completed = world.Units.Count(body => body.Phase == WalkPhase.Idle), stable,
            peak_ms = peaks, over_budget = over, hash = world.Hash() }));
        Check(automatic && completedAt is > 0 and <= 1200 && stable == 90, "Automatic frozen recovery not complete");
        Check(over == 0, "Frozen recovery exceeded 30Hz; all firsts retained");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
