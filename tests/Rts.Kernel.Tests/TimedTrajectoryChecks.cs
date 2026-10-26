using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class TimedTrajectoryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        CollisionBoundsChecks.Run(check);
        CrowdBody Body(ulong id, FixedVector2[] points, SimScalar[] times, SimScalar? radius = null) =>
            new(new(id), radius ?? 1, points, times);
        var a = Body(1, [new(-10, 0), new(10, 0)], [0, 1]);
        var crossing = Body(2, [new(0, -10), new(0, 10)], [0, 1]);
        check(TimedCrowdGeometry.Overlap(a, crossing), "simultaneous crossing is rejected between endpoint samples");
        var earlier = Body(2, [new(0, -20), new(0, 0), new(0, 20)], [0, SimScalar.FromRatio(1, 4), 1]);
        check(!TimedCrowdGeometry.Overlap(a, earlier), "same geometric crossing at separated times is allowed");
        check(!TimedCrowdGeometry.Overlap(earlier, a), "unaligned temporal geometry is symmetric");
        var corner = Body(1, [new(-10, 0), new(0, 0), new(0, 10)], [0, SimScalar.FromRatio(1, 2), 1]);
        var obstacle = CrowdBody.Stationary(new(2), 1, default);
        check(TimedCrowdGeometry.Overlap(corner, obstacle), "corner trace cannot be replaced by its endpoint chord");
        var arrived = Body(1, [new(-10, 0), new(0, 0), new(0, 0)], [0, SimScalar.FromRatio(1, 4), 1]);
        check(TimedCrowdGeometry.Overlap(arrived, crossing), "early arrival holds its goal for the remaining frame");
        foreach (var rawOffset in new long[] { -1, 0, 1 })
        {
            var other = Body(2, [FixedVector2.FromRaw(2 * SimScalar.Scale + rawOffset, 0), FixedVector2.FromRaw(12 * SimScalar.Scale + rawOffset, 0)], [0, 1]);
            var first = Body(1, [new(0, 0), new(10, 0)], [0, 1]);
            check(TimedCrowdGeometry.Overlap(first, other) == (rawOffset < 0), "parallel contact differs from overlap by one raw unit");
        }
        var zeroDuration = Body(1, [new(-10, 0), new(10, 0), new(10, 0)], [0, 0, 1]);
        check(TimedCrowdGeometry.Overlap(zeroDuration, obstacle), "sub-resolution duration cannot hide a swept collision");

        var reservations = new CrowdReservations(4);
        reservations.Put(Body(1, [new(0, 0), new(10, 0)], [0, 1]));
        reservations.Put(Body(2, [new(4, 0), new(14, 0)], [0, 1]));
        reservations.Put(CrowdBody.Stationary(new(3), 1, new(8, 0)));
        var held = CrowdMotionResolver.Resolve(reservations, [new(1), new(2)]);
        check(held.SetEquals([new(1), new(2)]), "front rejection also withdraws the follower that relied on it moving");
        for (ulong id = 1; id <= 3; id++)
            check(reservations.FirstMotionConflict(reservations.Read(new(id))) is null, "resolved chain is simultaneously safe");
        check(reservations.Read(new(3)).Trace[0] == new FixedVector2(8, 0), "static obstacle does not move during conflict resolution");

        var contacts = new CrowdReservations(4);
        contacts.Put(a);
        contacts.Put(crossing);
        contacts.Put(Body(7, [new(0, -10), new(0, 10)], [0, 1]));
        contacts.Put(earlier with { Id = new(9) });
        var beforeQuery = contacts.Read(new(2));
        check(contacts.MotionConflicts(a).SequenceEqual(new EntityId[] { new(2), new(7) }),
            "all contacts use timed geometry and stable identity order");
        check(contacts.FirstMotionConflict(a) == new EntityId(2) && contacts.Read(new(2)) == beforeQuery,
            "reading all contacts preserves first-conflict result and reservations");
        var detached = contacts.MotionConflicts(a); detached[0] = new(499);
        check(contacts.MotionConflicts(a)[0] == new EntityId(2), "contact query returns an independent result");

        var entity = new EntityState(new(1), 0, default, default);
        var order = new MoveOrder(new(new(10, 0), 120), new FixedVector2[] { new(1, 0), new(10, 0) });
        var step = MovementStepper.Compute(entity, order, 30, null);
        check(step.Times!.SequenceEqual(new SimScalar[] { 0, SimScalar.FromRatio(1, 4), 1 }),
            "motion proposal retains real waypoint time instead of uniform vertex timing");
        check(step.Position == new FixedVector2(4, 0), "time metadata does not change existing single-body displacement");
    }
}
