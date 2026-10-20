using Rts.Movement.Prototype;
using Rts.Kernel;
using Rts.Kernel.Numerics;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

/// <summary>CLI and Godot run the identical independent experimental acceptance suite.</summary>
public static class RouteCheckSuite
{
    public static bool Run(Action<string> output)
    {
        var authority = RouteScenarios.Create(1);
        var beforePreparation = authority.Save();
        var preparation = Stopwatch.StartNew();
        RoutePreparation.Run();
        preparation.Stop();
        if (authority.Save() != beforePreparation) throw new Exception("Preparation changed authority.");
        output($"route_prepare_ms={preparation.Elapsed.TotalMilliseconds:R} authority_unchanged=true");
        var passed = true;
        foreach (var scenario in new[] { 1, 2, 3, 4, 5 }) passed &= Scenario(scenario, output);
        RoutePlacementChecks.Run();
        output("route_placement_boundaries=PASS");
        RouteReferenceChecks.Run();
        output("route_reference_boundaries=PASS");
        RouteInwardChecks.Run();
        output("route_inward_boundaries=PASS");
        RouteBoundaryChecks.Run();
        output("route_boundaries=PASS");
        output("route_suite=" + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool Scenario(int scenario, Action<string> output)
    {
        var world = RouteScenarios.Create(scenario);
        var frames = new List<object>();
        using var transcript = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var failed = 0;
        var completed = 0;
        var ticks = 0;
        var overBudget = 0;
        var peak = 0.0;
        var endpointChanges = 0;
        var goalEvents = new List<RouteEvent>();
        for (; ticks < 1200; ticks++)
        {
            if (scenario == 5 && ticks is 31 or 62 or 93 or 124)
                world.Move(world.Units.Select(body => body.Id), ticks % 62 == 0 ? new(1550, 640) : new(450, 640));
            var before = world.Units;
            var resumed = ticks % 37 == 0 ? RouteWorld.Load(world.Save()) : null;
            var watch = Stopwatch.StartNew(); world.Step(); watch.Stop();
            var ms = watch.Elapsed.TotalMilliseconds;
            peak = Math.Max(peak, ms);
            overBudget += ms > 1000.0 / 30 ? 1 : 0;
            frames.Add(new { scenario, frame = world.Frame, elapsed_ms = ms, first = ticks == 0 });
            if (resumed is not null)
            {
                resumed.Step();
                Check(resumed.Hash() == world.Hash(), "JSON continuation hash");
                Check(JsonSerializer.Serialize(resumed.Events) == JsonSerializer.Serialize(world.Events), "JSON events");
            }
            transcript.AppendData(Encoding.UTF8.GetBytes(world.Hash() + "\n" + JsonSerializer.Serialize(world.Events) + "\n"));
            RoutePlacementChecks.AssertReserved(world);
            var after = world.Units;
            endpointChanges += after.Count(body => body.Task != 0 && body.Task == before.Single(old => old.Id == body.Id).Task
                && body.Endpoint != before.Single(old => old.Id == body.Id).Endpoint);
            for (var i = 0; i < after.Count; i++)
            {
                Check((after[i].Position - before[i].Position).Length <= 10, "no teleport/speedup");
                for (var j = i + 1; j < after.Count; j++)
                    Check((after[i].Position - after[j].Position).Length >= 62, "no overlap");
            }
            goalEvents.AddRange(world.Events.Where(item => item.Kind is "goal_reassigned" or "goal_repair_blocked"));
            failed += world.Events.Count(item => item.Kind == "failed");
            completed += world.Events.Count(item => item.Kind == "completed");
            if (ticks > 125 && after.All(body => body.Task == 0)) break;
        }
        var settled = world.Units.ToArray();
        var stableFrames = world.Units.All(body => body.Task == 0) ? 90 : 0;
        for (var idle = 0; idle < stableFrames; idle++)
        {
            world.Step();
            Check(settled.SequenceEqual(world.Units), "finished/failed units stay still");
            Check(world.Events.Count == 0, "no repeated completion");
        }
        if (scenario == 3) Check(world.Units[1].Position == new FixedVector2(900, 640)
            && world.Units[1].Phase == WalkPhase.Stopped, "Stop remains stationary");
        foreach (var frame in frames) output("route_tick=" + JsonSerializer.Serialize(frame));
        var pass = failed == 0 && world.Units.All(body => body.Task == 0) && overBudget == 0;
        output("route_result=" + JsonSerializer.Serialize(new { scenario, frames = frames.Count, stable_frames = stableFrames,
            failed, completed, active = world.Units.Count(body => body.Task != 0),
            settled = world.Units.Count(body => body.Phase == WalkPhase.Settled), endpoint_changes = endpointChanges,
            goal_overlap_frames = 0, goal_events = goalEvents, peak_ms = peak,
            over_budget = overBudget, passed = pass, hash = world.Hash(),
            trace_sha256 = Convert.ToHexString(transcript.GetHashAndReset()) }));
        return pass;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
