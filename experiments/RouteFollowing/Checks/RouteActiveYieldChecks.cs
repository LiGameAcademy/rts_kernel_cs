using System.Diagnostics;
using System.Text.Json;
using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;

/// <summary>The complete world immediately before the first ineffective recovery of member17.</summary>
public static class RouteActiveYieldChecks
{
    public static void Run(Action<string> output)
    {
        using var stream = typeof(RouteActiveYieldChecks).Assembly.GetManifestResourceStream("gathering-before-357.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        ContractChecks(world, output);
        var original = world.Units.ToDictionary(body => body.Id);
        var saved = world.Save();
        var prepare = Stopwatch.StartNew();
        RoutePreparation.Run(); RouteYieldChecks.Warmup();
        prepare.Stop();
        Check(world.Save() == saved, "Preparation changed authority");
        output("active_prepare_ms=" + prepare.Elapsed.TotalMilliseconds.ToString("R"));
        var originalDistance = (original[17].Endpoint - original[17].Position).Length;
        var events = new List<RouteEvent>();
        var peak = 0.0;
        var over = 0;
        for (var i = 0; i < 90; i++)
        {
            var before = world.Units.ToDictionary(body => body.Id);
            var resumed = RouteWorld.Load(world.Save());
            var watch = Stopwatch.StartNew(); world.Step(); watch.Stop();
            var ms = watch.Elapsed.TotalMilliseconds;
            peak = Math.Max(peak, ms); over += ms > 1000.0 / 30 ? 1 : 0;
            output("active_tick=" + JsonSerializer.Serialize(new { world.Frame, elapsed_ms = ms, first = i == 0, hash = world.Hash(), events = world.Events }));
            resumed.Step();
            Check(world.Hash() == resumed.Hash() && world.Events.SequenceEqual(resumed.Events), "Per-frame restore");
            events.AddRange(world.Events);
            RoutePlacementChecks.AssertReserved(world);
            foreach (var body in world.Units)
            {
                Check((body.Position - before[body.Id].Position).Length <= 9 + SimScalar.FromRaw(32), "Speed/teleport");
                Check(!original.Values.Any(old => old.Id == body.Id && old.Phase is WalkPhase.Idle or WalkPhase.Stopped)
                    || body.Position == original[body.Id].Position, "Stop/ordinary Idle moved");
                foreach (var other in world.Units.Where(other => other.Id > body.Id))
                    Check((body.Position - other.Position).Length >= 62, "Body overlap");
            }
            if (i == 0)
            {
                var plan = world.Paths.Single(task => task.Id == original[17].Task).Yield;
                Check(plan is not null && plan.Requester == 17, "This frozen admission must start bounded cooperation");
                Check(plan!.Members.Any(id => original[id].Phase == WalkPhase.Waiting), "Must exercise an active waiting helper");
                Check(plan.Members.Count <= 2, "Bounded helper count");
                output("active_plan=" + JsonSerializer.Serialize(new { plan,
                    members = plan.Members.Select(id => new { before = original[id], after = world.Units.Single(body => body.Id == id) }).ToArray() }));
                if (Environment.GetEnvironmentVariable("RTS_CLOSEOUT_EVIDENCE") is { Length: > 0 } folder)
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "active-plan-before.json"), saved);
                    File.WriteAllText(Path.Combine(folder, "active-plan-started.json"), world.Save());
                }
            }
        }
        var progressed = originalDistance - (world.Units.Single(body => body.Id == 17).Position - original[17].Endpoint).Length;
        var finished = events.Any(ev => ev.Kind == "yield_finished" && ev.Id == 17);
        output("active_result=" + JsonSerializer.Serialize(new { finished, progress = progressed.ToPresentationDouble(),
            peak_ms = peak, over_budget = over, events, hash = world.Hash(), world.Frame }));
        Check(finished && progressed >= 32, "Cooperation must finish and advance the original requester");
        Check(over == 0, "90-frame prototype exceeded 30Hz");
        if (Environment.GetEnvironmentVariable("RTS_CLOSEOUT_EVIDENCE") is { Length: > 0 } finalFolder)
            File.WriteAllText(Path.Combine(finalFolder, "active-plan-final.json"), world.Save());
        output("active_yield_checks=PASS");
    }

    private static void ContractChecks(RouteWorld world, Action<string> output)
    {
        var waiting = world.Units.Single(body => body.Id == 7);
        var before = world.Save();
        Check(RouteYielding.CanParticipate(waiting, waiting.Task), "Waiting own-task helper may cooperate");
        Check(!RouteYielding.CanParticipate(waiting with { Phase = WalkPhase.Following }, waiting.Task), "Moving peer is not stopped preemptively");
        Check(!RouteYielding.CanParticipate(waiting with { Phase = WalkPhase.Stopped }, waiting.Task), "Stop excluded");
        Check(!RouteYielding.CanParticipate(waiting with { Phase = WalkPhase.Idle }, waiting.Task), "Ordinary Idle excluded");
        Check(!RouteYielding.CanParticipate(waiting, waiting.Task + 1), "Other task excluded");
        Check(!RouteYielding.CanParticipate(waiting with { Pending = new QueuedGoal[] { new(waiting.Task, waiting.Endpoint, default) } },
            waiting.Task), "Queued helper excluded");
        var requester = world.Units.Single(body => body.Id == 17);
        Check(!RoutePlacements.TryBeginYield(world, requester, [requester.Id], [requester.Endpoint]), "Requester cannot recruit itself");
        Check(world.Save() == before, "Rejected participant admission preserves authority");
        using var input = typeof(RouteActiveYieldChecks).Assembly.GetManifestResourceStream("gathering-before-268.json")!;
        using var reader = new StreamReader(input);
        var rejected = RouteWorld.Load(reader.ReadToEnd());
        var saved = rejected.Save();
        Check(!RouteYielding.TryStart(rejected, rejected.Units.Single(body => body.Id == 7)), "No forced plan in the 267 rejection case");
        Check(rejected.Save() == saved, "Failed search commits no partial goals, attempts or plans");
        output("active_participant_contracts=10 PASS");
    }

    private static void Check(bool passed, string detail)
    {
        if (!passed) throw new InvalidOperationException(detail);
    }
}
