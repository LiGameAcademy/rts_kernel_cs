using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Rts.Movement.Prototype;

/// <summary>Failure diagnosis only: compares live/reloaded control and times existing phases on disposable clones.</summary>
internal static class MultiGroupFailureAudit
{
    internal static void Run(string inputs, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var obstacle in new[] { false, true }) CompareLiveAndRestored(inputs, folder, obstacle);
        PhaseCosts(inputs);
    }

    private static void CompareLiveAndRestored(string inputs, string folder, bool obstacle)
    {
        var scenario = obstacle ? "obstacle" : "restore";
        var live = MultiGroupChecks.Create(300);
        Check(MultiGroupChecks.Submit(live, 300).Length == 0, "Initial admission");
        for (var i = 0; i < 30; i++) live.Step();
        Check(live.Save() == File.ReadAllText(Path.Combine(inputs, "moving-frame30.json")), "Reconstruction differs");
        var restored = RouteWorld.Load(live.Save());
        if (obstacle)
        {
            Check(live.SetWall(new(25, 9, 1, 1), true), "Live obstacle edit");
            Check(restored.SetWall(new(25, 9, 1, 1), true), "Restored obstacle edit");
        }
        Check(MultiGroupChecks.Submit(live, 300).Length == 0, "Live replacement");
        Check(MultiGroupChecks.Submit(restored, 300).Length == 0, "Restored replacement");
        var subject = obstacle ? 20UL : 265UL;
        var identicalFrames = 0;
        for (var tick = 0; tick < 1200; tick++)
        {
            var before = live.Save();
            var beforeBody = live.Bodies[subject];
            live.Step();
            restored.Step();
            Check(live.Hash() == restored.Hash()
                && JsonSerializer.Serialize(live.Events) == JsonSerializer.Serialize(restored.Events), "Live/restored divergence");
            identicalFrames++;
            var relevant = live.Events.Where(e => e.Id == subject).ToArray();
            if (relevant.Length > 0)
            {
                File.WriteAllText(Path.Combine(folder, $"{scenario}-before-{live.Frame}.json"), before);
                File.WriteAllText(Path.Combine(folder, $"{scenario}-after-{live.Frame}.json"), live.Save());
                var afterBody = live.Bodies[subject];
                Console.WriteLine("failure_event=" + JsonSerializer.Serialize(new { scenario, live.Frame, subject,
                    before = beforeBody, after = afterBody, events = relevant,
                    before_motion = DescribeMotion(RouteWorld.Load(before), subject),
                    yield = afterBody.Task == 0 ? null : live.Routes[afterBody.Task].Yield }));
            }
            if (live.Bodies.Values.Any(body => body.Phase == WalkPhase.Failed))
            {
                var expected = RouteWorld.Load(File.ReadAllText(Path.Combine(inputs, scenario + "-final.json")));
                Check(live.Hash() == expected.Hash(), "Recorded failure not reproduced");
                Console.WriteLine("failure_control=" + JsonSerializer.Serialize(new { scenario, identicalFrames,
                    live.Frame, live_and_restored_identical = true, hash = live.Hash() }));
                return;
            }
        }
        throw new InvalidOperationException("Expected frozen failure not reproduced");
    }

    private static object? DescribeMotion(RouteWorld clone, ulong id)
    {
        var source = clone.Bodies[id];
        if (source.Task == 0) return null;
        var (body, target, _) = RouteNavigation.Follow(clone, source);
        return new { body.Cursor, kind = body.Navigation?.Kind, target,
            terrain_clear = RoutePlanning.TryDirect(clone, body.Position, target, []) is not null,
            eligible_contacts = RouteRecovery.Contacts(clone, source) };
    }

    private static void PhaseCosts(string inputs)
    {
        // These are local attribution probes, not full-Step performance gates. Reflection/setup is outside timing.
        var initial = File.ReadAllText(Path.Combine(inputs, "initial.json"));
        var apply = typeof(RouteWorld).GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var command = new RouteCommand(false, Enumerable.Range(1, 24).Select(id => (ulong)id).ToArray(), new(928, 256), false);
        for (var run = 0; run < 5; run++)
        {
            var world = RouteWorld.Load(initial);
            Measure("first-apply", run, () => apply.Invoke(world, [command]));
            Measure("first-refresh", run, () => RoutePlacements.Refresh(world));
            Measure("first-motion", run, () => RouteMotion.Advance(world));
        }
    }

    private static void Measure(string phase, int run, Action action)
    {
        var before = FrameSpikeLog.ReadCounters();
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        var end = Stopwatch.GetTimestamp();
        var after = FrameSpikeLog.ReadCounters();
        var log = new FrameSpikeLog(1);
        log.Record(0, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, before, after);
        log.Report(300, run, "diagnostic-" + phase);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
