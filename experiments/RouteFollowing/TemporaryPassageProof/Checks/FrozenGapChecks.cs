using System.Diagnostics;
using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Executes the unchanged full world while accepting only actual completion of the subject's entire party.</summary>
internal static class FrozenGapChecks
{
    internal static bool Run(string input, ulong subject, string folder)
    {
        Directory.CreateDirectory(folder);
        var text = File.ReadAllText(input);
        var world = RouteWorld.Load(text);
        var replay = RouteWorld.Load(text);
        var original = world.Bodies[subject];
        var members = world.Bodies.Values.Where(body => body.Task == original.Task).Select(body => body.Id).ToHashSet();
        if (members.Count == 0 || original.Task == 0) throw new ArgumentException("Expected an active frozen party");
        var log = new FrameSpikeLog(1290);
        var preparation = Stopwatch.StartNew();
        RoutePreparation.Run();
        preparation.Stop();
        if (world.Save() != text) throw new InvalidOperationException("Preparation or load changed authority");
        Console.WriteLine("gap_prepare_ms=" + preparation.Elapsed.TotalMilliseconds.ToString("R"));
        var over = 0;
        var stable = 0;
        var arrival = 0L;
        var peak = 0.0;
        var positions = new Dictionary<ulong, FixedVector2>();
        try
        {
            for (var tick = 0; tick < 1290; tick++)
            {
                var before = world.Save();
                var counters = FrameSpikeLog.ReadCounters();
                var bytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                world.Step();
                var end = Stopwatch.GetTimestamp();
                var after = FrameSpikeLog.ReadCounters();
                log.Record(tick, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, counters, after);
                var elapsed = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
                peak = Math.Max(peak, elapsed);
                if (elapsed > 1000.0 / 30) over++;
                replay.Step();
                var hash = world.Hash();
                if (hash != replay.Hash() || JsonSerializer.Serialize(world.Events) != JsonSerializer.Serialize(replay.Events))
                    throw new InvalidOperationException("Frozen restore continuation differs");
                Console.WriteLine("gap_tick=" + JsonSerializer.Serialize(new { subject, world.Frame, hash, world.Events }));
                if (elapsed > 1000.0 / 30 || world.Events.Any(e => members.Contains(e.Id)))
                {
                    File.WriteAllText(Path.Combine(folder, $"before-{world.Frame}.json"), before);
                    File.WriteAllText(Path.Combine(folder, $"after-{world.Frame}.json"), world.Save());
                }
                var party = members.Select(id => world.Bodies[id]).ToArray();
                if (party.Any(body => body.Phase == WalkPhase.Failed)) break;
                if (party.All(body => body.Task == 0 && body.Phase == WalkPhase.Idle))
                {
                    if (arrival == 0)
                    {
                        arrival = world.Frame;
                        positions = party.ToDictionary(body => body.Id, body => body.Position);
                    }
                    else
                    {
                        if (party.Any(body => body.Position != positions[body.Id]) || world.Events.Any(e => members.Contains(e.Id)))
                            throw new InvalidOperationException("Completed party did not remain stable");
                        if (++stable == 90) break;
                    }
                }
                if (tick == 1199 && arrival == 0) break;
            }
        }
        finally
        {
            log.Report(world.Bodies.Count, 0, "frozen-gap-" + subject);
            File.WriteAllText(Path.Combine(folder, "final.json"), world.Save());
        }
        var executed = arrival > 0 && stable == 90;
        Console.WriteLine("gap_result=" + JsonSerializer.Serialize(new { subject, executed, arrival, stable, over_budget = over,
            peak_ms = peak, party_size = members.Count,
            failed = members.Where(id => world.Bodies[id].Phase == WalkPhase.Failed).ToArray(), hash = world.Hash() }));
        return executed && over == 0;
    }
}
