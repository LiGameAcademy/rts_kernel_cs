using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class RouteReviewBoundaryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        ShortcutAndTerrain(check);
        PositionWindow(check);
        SnapshotAndOwnership(check);
    }

    private static void PositionWindow(Action<bool, string> check)
    {
        var navigation = new NavigationState(GroupYieldChecks.Grid);
        var entity = new EntityState(new(1), 0, new(1040, 1040), FixedVector2.Zero, MovementDefinitionId: 1);
        var goal = new FixedVector2(1360, 1040);
        var points = StraightMovePath.TryFind(navigation, entity.Position, goal, 1)!;
        var state = new RouteReviewState(15, 320, 0, WindowStart: entity.Position);
        for (var frame = 0; frame < 12; frame++) state = RouteReview.RecordMotion(state, 5)!;
        var order = new MoveOrder(new(goal, 270, 1, GroupYieldChecks.Footman.Motion), points, Review: state);
        var result = RouteReview.Advance(90, 30, entity, GroupYieldChecks.Footman, order,
            navigation, new CrowdReservations(64));
        check(result.Review!.Oscillating, "large travelled distance but unchanged checkpoint marks local oscillation");
        var waiting = RouteReview.Advance(90, 30, entity, GroupYieldChecks.Footman,
            order with { Review = state with { Travelled = 0 } }, navigation, new CrowdReservations(64));
        check(!waiting.Review!.Oscillating, "normal stationary waiting is not mistaken for moving in circles");
        var advancing = RouteReview.Advance(90, 30, entity, GroupYieldChecks.Footman,
            order with { Review = state with { BestDistance = 400 } }, navigation, new CrowdReservations(64));
        check(!advancing.Review!.Oscillating && advancing.Review.ProgressFrame == 90,
            "meaningful advancement resets the position observation window");
    }

    private static void ShortcutAndTerrain(Action<bool, string> check)
    {
        var navigation = new NavigationState(GroupYieldChecks.Grid);
        var entity = new EntityState(new(1), 0, new(1040, 1040), FixedVector2.Zero, MovementDefinitionId: 1);
        var definition = GroupYieldChecks.Footman;
        var goal = new FixedVector2(1360, 1040);
        var points = new List<FixedVector2> { entity.Position };
        foreach (var next in new[] { new FixedVector2(1040, 1200), new(1360, 1200), goal })
            points.AddRange(StraightMovePath.TryFind(navigation, points[^1], next, 1)!.Skip(1));
        var order = new MoveOrder(new(goal, definition.Speed, 1, definition.Motion), points,
            Review: new(15, 320, 15));
        var blocked = new CrowdReservations(64);
        blocked.Put(CrowdBody.Stationary(new(2), 31, new(1200, 1040)));
        var kept = RouteReview.Advance(15, 30, entity, definition, order, navigation, blocked);
        check(kept.Waypoints.SequenceEqual(order.Waypoints), "review never shortcuts through a stationary Stop body");
        var delayed = RouteReview.Advance(120, 30, entity, definition,
            order with { Review = order.Review! with { ProgressFrame = 0 } }, navigation, blocked);
        check(delayed.Waypoints.SequenceEqual(order.Waypoints) && !delayed.Review!.Recovering,
            "progress review cannot repeatedly restart a fully safe detour around peers");
        var clear = RouteReview.Advance(15, 30, entity, definition, order, navigation, new CrowdReservations(64));
        check(clear.Waypoints.Count < order.Waypoints.Count && clear.Waypoints[^1] == goal,
            "clear corridor shortens a detour without changing its endpoint");

        var flags = new byte[128 * 128];
        for (var y = 25; y < 40; y++) flags[y * 128 + 37] = 2;
        var terrain = new NavigationState(new(128, 128, 32, default, flags));
        check(MovePathPlanner.TryPlan(terrain, entity.Position, order.Request, out var around), "terrain detour fixture is reachable");
        around = around! with { Review = new(15, 1, 0) };
        var reviewed = RouteReview.Advance(90, 30, entity, definition, around, terrain, new CrowdReservations(64));
        check(reviewed.Waypoints.SequenceEqual(around.Waypoints) && !reviewed.Review!.Recovering,
            "necessary terrain detours are not reversed by straight-line distance");
    }

    private static RtsMatch StartReview(Action<bool, string> check)
    {
        var match = OpenGroundPartyChecks.Create();
        match.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 25,
            new(match.Entities.Select(entity => entity.Id).ToArray(), new(1490, 2390))));
        for (var frame = 0; frame < 150; frame++)
        {
            match.Step();
            match.DrainEvents();
            if (match.ReadMoveOrders().Any(order => order.Review is not null)) return match;
        }
        check(false, "fixture reaches an active reviewed local detour");
        return match;
    }

    private static void SnapshotAndOwnership(Action<bool, string> check)
    {
        var match = StartReview(check);
        var saved = match.CaptureSnapshot();
        var move = saved.Moves!.First(order => order.Review is not null);
        void Reject(RouteReviewState state)
        {
            var bad = saved with { Moves = saved.Moves!.Select(item => item.EntityId == move.EntityId ? item with { Review = state } : item).ToArray() };
            try { Restore(bad); check(false, "corrupt review state is rejected"); }
            catch (InvalidDataException) { check(true, "corrupt review state is rejected"); }
        }
        Reject(move.Review! with { NextFrame = saved.Frame + 16 });
        Reject(move.Review! with { ProgressFrame = saved.Frame + 1 });
        Reject(move.Review! with { BestDistance = -1 });
        Reject(move.Review! with { BestDistance = SimScalar.MaxValue });
        Reject(move.Review! with { WindowFrame = saved.Frame + 1 });
        Reject(move.Review! with { WindowStart = new(SimScalar.MaxValue, 0) });
        Reject(move.Review! with { Travelled = -1 });
        Reject(move.Review! with { Travelled = SimScalar.MaxValue });
        try { Restore(saved with { FormatVersion = 13 }); check(false, "v13 cannot discard review authority"); }
        catch (InvalidDataException) { check(true, "v13 cannot discard review authority"); }
        var changed = saved with { Moves = saved.Moves!.Select(item => item.EntityId == move.EntityId
            ? item with { Review = move.Review! with { Recovering = !move.Review.Recovering } } : item).ToArray() };
        check(Restore(changed).ComputeStateHash() != match.ComputeStateHash()
            && SnapshotDiff.FindFirst(saved, changed)?.Path.EndsWith(".review") == true,
            "review progress is authoritative in hashes and differences");

        var id = new EntityId(move.EntityId);
        var stopped = Restore(saved);
        stopped.SubmitCommand(CommandEnvelope.Stop(stopped.Frame + 1, 0, 26, id));
        stopped.Step();
        var position = stopped.Entities.Single(entity => entity.Id == id).Position;
        for (var frame = 0; frame < 60; frame++) stopped.Step();
        check(stopped.Entities.Single(entity => entity.Id == id).Position == position
            && stopped.ReadCurrentOrder(id)?.Kind == UnitOrderKind.Stop
            && !stopped.ReadMoveOrders().Any(order => order.EntityId == id.Value), "review cannot resurrect Stop");

        var replaced = Restore(saved);
        var goal = new FixedVector2(3408, 3600);
        replaced.SubmitCommand(CommandEnvelope.MoveTo(replaced.Frame + 1, 0, 26, id, goal, 270, 1, GroupYieldChecks.Footman.Motion));
        replaced.Step();
        check(replaced.ReadCurrentOrder(id)?.Move?.Goal == goal
            && replaced.ReadMoveOrders().Single(order => order.EntityId == id.Value).Review is null,
            "new exact command clears old group progress and detour ownership");

        var queued = Restore(saved);
        var intent = queued.ReadCurrentOrder(id);
        queued.SubmitCommand(CommandEnvelope.MoveTo(queued.Frame + 1, 0, 26, id, goal, 270, 1,
            GroupYieldChecks.Footman.Motion, OrderMode.Append));
        queued.Step();
        check(queued.ReadCurrentOrder(id) == intent && queued.GetPendingOrderCount(id) == 1,
            "appending preserves active intent while review continues");
        var restored = Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(queued.CaptureSnapshot())));
        for (var frame = 0; frame < 100; frame++)
        {
            queued.Step(); restored.Step();
            check(queued.ComputeStateHash() == restored.ComputeStateHash(), "queued review restores identically");
        }
    }

    private static RtsMatch Restore(MatchSnapshot snapshot) =>
        RtsMatch.Restore(snapshot, GroupYieldChecks.Grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
}
