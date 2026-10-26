using Rts.Movement.Prototype;
using System.Text.Json;

/// <summary>Records recovery admission and actual reference changes in the complete narrow world.</summary>
public static class RouteGatheringAudit
{
    public static void Run(Action<string> output)
    {
        var folder = Environment.GetEnvironmentVariable("RTS_CLOSEOUT_EVIDENCE")
            ?? throw new InvalidOperationException("Missing evidence directory.");
        Directory.CreateDirectory(folder);
        var world = RouteScenarios.Create(4);
        var lastRecoveryPositions = new Dictionary<ulong, Rts.Kernel.Numerics.FixedVector2>();
        for (var tick = 0; tick < 1200; tick++)
        {
            var saved = world.Save();
            var before = world.Units.ToDictionary(body => body.Id);
            world.Step();
            foreach (var ev in world.Events.Where(ev => ev.Kind is "retry" or "failed" or "yield_started"))
            {
                var prior = before[ev.Id];
                var current = world.Units.Single(body => body.Id == ev.Id);
                before.TryGetValue(prior.Blocker ?? 0, out var blocker);
                var pathChanged = prior.Navigation is null || current.Navigation is null
                    || !prior.Navigation.Points.SequenceEqual(current.Navigation.Points);
                var sinceRetry = lastRecoveryPositions.TryGetValue(ev.Id, out var last)
                    ? (prior.Position - last).Length.ToPresentationDouble() : (double?)null;
                lastRecoveryPositions[ev.Id] = prior.Position;
                File.WriteAllText(Path.Combine(folder, $"before-{world.Frame}-{ev.Id}-{ev.Kind}.json"), saved);
                File.WriteAllText(Path.Combine(folder, $"after-{world.Frame}-{ev.Id}-{ev.Kind}.json"), world.Save());
                output("gathering_audit=" + JsonSerializer.Serialize(new { world.Frame, ev.Id, ev.Kind, ev.Detail,
                    prior.WaitTicks, prior.Recoveries, next_recoveries = current.Recoveries,
                    prior.Position, prior.Endpoint, prior.Preference, path_changed = pathChanged,
                    displacement_since_previous_recovery = sinceRetry, blocker,
                    hash = world.Hash() }));
            }
            if (tick > 125 && world.Units.All(body => body.Task == 0)) break;
        }
        File.WriteAllText(Path.Combine(folder, "final-world.json"), world.Save());
        output("gathering_audit_result=" + JsonSerializer.Serialize(new { world.Frame,
            completed = world.Units.Count(body => body.Phase == WalkPhase.Idle),
            failed = world.Units.Where(body => body.Phase == WalkPhase.Failed).Select(body => body.Id).ToArray(),
            hash = world.Hash(), diagnostic_only = true }));
    }
}
