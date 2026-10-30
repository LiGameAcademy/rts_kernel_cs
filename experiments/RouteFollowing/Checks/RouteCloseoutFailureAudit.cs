using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Text.Json;

/// <summary>Diagnostic replay of the already-failed 60-tick fixture. Does not extend its deadline.</summary>
public static class RouteCloseoutFailureAudit
{
    public static void Run(Action<string> output)
    {
        var world = RouteYieldChecks.Load("yield-pair-444.json");
        var folder = Environment.GetEnvironmentVariable("RTS_CLOSEOUT_EVIDENCE")
            ?? throw new InvalidOperationException("Missing evidence directory.");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pair-audit-before.json"), world.Save());
        for (var i = 0; i < 60; i++)
        {
            var previous = world.Save();
            var resumed = RouteWorld.Load(previous);
            world.Step(); resumed.Step();
            if (world.Hash() != resumed.Hash() || !world.Events.SequenceEqual(resumed.Events))
                throw new InvalidOperationException("Diagnostic replay diverged.");
            if (world.Events.Any(ev => ev.Kind == "yield_cancelled"))
            {
                File.WriteAllText(Path.Combine(folder, "pair-cancel-before.json"), previous);
                File.WriteAllText(Path.Combine(folder, "pair-cancel-after.json"), world.Save());
            }
            output("pair_audit=" + JsonSerializer.Serialize(new
            {
                world.Frame, world.Events,
                plan = world.Paths.SingleOrDefault(task => task.Id == 5)?.Yield,
                members = world.Units.Where(body => body.Id is 4 or 7 or 18).Select(body => new
                {
                    body.Id, body.Task, body.Phase, body.Blocker, body.Passage, body.WaitTicks, body.Recoveries,
                    x = body.Position.X.ToPresentationDouble(), y = body.Position.Y.ToPresentationDouble(),
                    goal_x = body.Endpoint.X.ToPresentationDouble(), goal_y = body.Endpoint.Y.ToPresentationDouble(),
                    distance = (body.Endpoint - body.Position).Length.ToPresentationDouble()
                })
            }));
        }
        File.WriteAllText(Path.Combine(folder, "pair-audit-final.json"), world.Save());
        output("pair_audit_result=" + JsonSerializer.Serialize(new { hash = world.Hash(), diagnostic_only = true, ticks = 60 }));
    }
}
