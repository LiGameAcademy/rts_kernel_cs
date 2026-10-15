using NumericPrototype.Groups;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal static class PlacementWorkChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var world = PlacementChecks.World(wall: true);
        foreach (var size in new[] { 16, 31, 64, 128 })
        foreach (var budget in new[] { 81, 127, 4096 })
        {
            var radius = size * FixedMath.One;
            var cache = new PlacementConnectivity(world);
            var cold = cache.Start(radius);
            var ticks = 0;
            while (!cold.Complete) { cold.Advance(budget); ticks++; }
            cache.Remember(cold);
            var warm = cache.Start(radius);
            for (var i = 0; i < ticks; i++)
            {
                check(!warm.Complete, "warm cache cannot skip reserved cold work frames");
                warm.Advance(budget);
            }
            check(warm.Complete && ticks == (cold.Data.RequiredWork + budget - 1) / budget,
                "cached and cold connectivity have identical completion frame");
            var reference = new PlacementReachability(world);
            for (var y = 0; y < world.Height; y += 3)
            for (var x = 0; x < world.Width; x += 3)
                check(cold.Connected(world.Center(3, 6), world.Center(x, y))
                    == reference.Connected(world.Center(3, 6), world.Center(x, y), radius),
                    "incremental components equal original reachability for all audited clearances");
        }
        var left = world.Center(3, 6);
        var right = world.Center(28, 6);
        var r = 8 * FixedMath.One;
        PlacementMember[] members = [new(1, world.Center(20, 6), r, [world.Center(20, 6)]), new(2, left, r, [left, right])];
        FixedPoint[] ideals = [right, right];
        using var job = new GroupPlanningJob(new(1, members.Select(m => new GroupMember(m.Id, m.Start)).ToArray(), ideals, 1, false), world);
        while (!job.Ready) job.Advance(world, 10000, 64);
        var slots = job.Assignments().ToDictionary(a => a.Member, a => a.Slot);
        var work = new PlacementWork(world, members, job, slots, [], new(world));
        while (!work.Complete) work.Advance();
        check(work.Results().SequenceEqual(FixedPlacement.Plan(world, members, ideals, slots, [])),
            "resumable failure fixed point preserves retained future goals and survivor adjustment");
    }
}
