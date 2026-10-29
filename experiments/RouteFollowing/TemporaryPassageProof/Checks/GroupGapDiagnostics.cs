using System.Text.Json;
using Rts.Movement.Prototype;

/// <summary>Read-only-to-input diagnosis of the two existing recovery alternatives on disposable copies.</summary>
internal static class GroupGapDiagnostics
{
    internal static void Run(string path, ulong id)
    {
        var json = File.ReadAllText(path);
        foreach (var blocker in RouteRecovery.Contacts(RouteWorld.Load(json), RouteWorld.Load(json).Bodies[id]))
        foreach (var temporaryOnly in new[] { false, true })
        {
            var world = RouteWorld.Load(json);
            var body = world.Bodies[id] with { Blocker = blocker, Recovery = null };
            if (temporaryOnly)
            {
                var observation = new RouteYieldObservation(world);
                var (_, target, _) = RouteNavigation.Follow(world, body);
                var direct = RouteRequesterPassage.Predict(world, body, target, reachTarget: false);
                var connection = RouteRequesterPassage.Predict(world, body with { Velocity = default },
                    world.Bodies[blocker].Position, reachTarget: true);
                Console.WriteLine("gap_geometry=" + JsonSerializer.Serialize(new { world.Frame, id, blocker,
                    direct, connection, directBlockers = Blockers(world, direct, id, blocker),
                    connectionBlockers = Blockers(world, connection, id, blocker) }));
            }
            var work = 0;
            var accepted = temporaryOnly
                ? RouteTemporaryYield.TryStart(world, body, new RouteYieldObservation(world), ref work)
                : RouteYielding.TryStart(world, body, ref work);
            Console.WriteLine("gap_search=" + JsonSerializer.Serialize(new { world.Frame, id, blocker, temporaryOnly,
                accepted, work, plan = world.Routes[body.Task].Yield }));
        }
    }

    private static ulong[] Blockers(RouteWorld world, RequesterPassage passage, ulong requester, ulong root) =>
        world.Bodies.Values.Where(body => body.Id != requester && body.Id != root
            && passage.Trace.Zip(passage.Trace.Skip(1)).Any(edge =>
                RouteYieldGeometry.Overlap(edge.First, edge.Second, 31, body.Position, body.Position, 31)))
            .Select(body => body.Id).ToArray();
}
