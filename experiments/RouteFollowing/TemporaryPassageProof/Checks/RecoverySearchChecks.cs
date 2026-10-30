using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

internal static class RecoverySearchChecks
{
    internal static void Run()
    {
        using var stream = typeof(RecoverySearchChecks).Assembly.GetManifestResourceStream("recovery-peak-512.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        var saved = world.Save();
        var observation = new RouteYieldObservation(world);
        var requester = world.Bodies[385];
        var helper = world.Bodies[401];
        var targets = RoutePlanning.Candidates(world, helper.Preference, 156).ToArray();
        foreach (var target in targets.Concat(targets.Reverse()))
        {
            var expected = RouteYieldClearance.CanAdvance(world, requester, [helper.Id], [target]);
            Check(observation.CanAdvance(world, requester, [helper.Id], [target]) == expected,
                "Reused trial agrees with a fresh complete reservation, including return visits");
        }
        foreach (var member in world.Bodies.Values.Take(24))
        foreach (var offset in new[] { new FixedVector2(64, 0), new(128, -64), new(-128, 64), new(1, 1) })
        foreach (var delta in new long[] { -1, 0, 1 })
        {
            var target = member.Position + offset + FixedVector2.FromRaw(delta, delta);
            var relocated = world.Bodies[500];
            var replacement = member.Position + new FixedVector2(32, 0);
            var projected = world.Bodies.Values.Where(b => b.Id != member.Id)
                .Select(b => b.Id == relocated.Id ? replacement : b.Position).ToArray();
            var atGoal = projected.Any(p => RouteYieldGeometry.GoalOverlap(p, target));
            var onLeg = projected.Any(p => RouteYieldGeometry.Overlap(member.Position, target, 31, p, p, 31));
            Check(observation.BodyAtGoal(member.Id, target, relocated.Id, replacement) == atGoal, "Goal bucket boundary agrees");
            Check(observation.BodyOnLeg(member.Id, member.Position, target, relocated.Id, replacement) == onLeg,
                "Leg buckets include relocated helpers arriving from distant buckets");
            Check(observation.BodyAtGoalOrOnLeg(member.Id, member.Position, target, relocated.Id, replacement) == (atGoal || onLeg),
                "Combined projection agrees with original full scan");
            var obstacles = world.Bodies.Values.Where(b => b.Id != member.Id
                && (RouteYieldGeometry.GoalOverlap(b.Position, target)
                    || RouteYieldGeometry.Overlap(member.Position, target, 31, b.Position, b.Position, 31)))
                .Select(b => b.Id).ToArray();
            Check(observation.Obstacles(member.Id, member.Position, target).SequenceEqual(obstacles), "Stable obstacle order agrees");
        }
        Check(observation.FirstVisit(requester, [helper.Id], [targets[0]]), "First layout visited");
        Check(!observation.FirstVisit(requester, [helper.Id], [targets[0]]), "Repeated layout skipped");
        Check(observation.FirstVisit(requester with { Passage = null }, [helper.Id], [targets[0]]),
            "Different admitted pose is not conflated with previous rejection");
        Check(new RouteYieldObservation(world).FirstVisit(requester, [helper.Id], [targets[0]]), "Search state is not retained");
        Check(world.Save() == saved, "All observations and trial rollbacks preserve authority");
        Console.WriteLine("recovery_search=PASS fresh_vs_reused_trials_spatial_projection_boundaries_order_authority");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
