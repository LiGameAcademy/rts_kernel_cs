using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Text.Json;

/// <summary>Fixed #137 inputs: every member must arrive rather than merely stop retrying.</summary>
internal static class ArrivalTimeoutChecks
{
    internal static void Run(Action<bool, string> check)
    {
        YieldAtEnvelope(check);
        var yielded = RunCase(91, 62, [new(1799, 1480), new(1673, 1354), new(2079, 2420), new(2028, 1542), new(1267, 2812)], check);
        yielded |= RunCase(106, 46, [new(2522, 2353), new(2036, 2745), new(2275, 1918), new(2974, 1282), new(2224, 2903)], check);
        yielded |= RunCase(137, 65, [new(2694, 1756), new(1346, 2738), new(2438, 1719), new(2168, 2786), new(1200, 2613)], check);
        // Clearing stale holds can remove the need to cooperate in an individual case.
        check(yielded, "arrival regression suite still exercises real cooperation");
    }

    private static void YieldAtEnvelope(Action<bool, string> check)
    {
        var navigation = new NavigationState(GroupYieldChecks.Grid, GroupYieldChecks.Heights);
        var definition = GroupYieldChecks.Footman;
        var definitions = new MovementDefinitions([definition]);
        var requester = new EntityState(new(1), 0, new(1000, 1000), default, MovementDefinitionId: 1);
        var helper = new EntityState(new(2), 0, new(1062, 1000), default, MovementDefinitionId: 1);
        var orders = new UnitOrderBook();
        MoveOrder Assign(EntityState entity, FixedVector2 goal, int slot)
        {
            var request = new MoveRequest(goal, 270, 1, definition.Motion);
            var path = new MoveOrder(request, StraightMovePath.TryFind(navigation, entity.Position, goal, 1)!);
            orders.ReplaceMove(entity.Id, new(UnitOrderKind.Move, OrderSource.Player, request,
                new(1, slot, FormationKind.Compact, goal, 0, false, true, 3)), path);
            return path;
        }
        var path = Assign(requester, new(1400, 1000), 0) with { Review = new(15, 400, 0, Recovering: true) };
        Assign(helper, new(1212, 1000), 1);
        orders.SuspendPath(helper.Id);
        orders.Cooperation.Settle(helper.Id, helper.Position);
        var before = orders.ReadCurrent(helper.Id);
        check(LocalYieldPlanner.Plan(requester, path with { Review = null }, helper.Id,
            [requester, helper], orders, definitions, navigation) is null,
            "ordinary movement does not use the recovery-only forward fallback");
        var plan = LocalYieldPlanner.Plan(requester, path, helper.Id, [requester, helper], orders, definitions, navigation);
        check(plan is { Count: 1 } && plan[0].Path.Waypoints[^1] == new FixedVector2(1094, 1070),
            "bounded forward slide opens a corridor when both lateral translations exceed the preference envelope");
        check(orders.ReadCurrent(helper.Id) == before && !orders.Cooperation.IsYielding(helper.Id),
            "yield planning cannot mutate player intent or install a temporary path");
        check((plan![0].Path.Waypoints[^1] - before!.Move!.Goal).Length <= 156,
            "forward yield keeps the original soft envelope");
        var reservations = new CrowdReservations(64);
        reservations.Put(CrowdBody.Stationary(requester.Id, 31, requester.Position));
        check(reservations.Safe(helper.Id, 31, plan[0].Path.Waypoints), "forward yield never crosses its requester");

        var travelling = new EntityState(new(3), 0, new(1200, 1200), default, MovementDefinitionId: 1);
        Assign(travelling, new(1400, 1200), 2);
        check(LocalYieldPlanner.Plan(requester, path, helper.Id, [requester, helper, travelling],
            orders, definitions, navigation) is null, "forward recovery waits for other party members to settle");
        orders.Clear(travelling.Id);

        Assign(helper, helper.Position, 1);
        orders.SuspendPath(helper.Id);
        orders.Cooperation.Settle(helper.Id, helper.Position);
        var lateral = LocalYieldPlanner.Plan(requester, path, helper.Id, [requester, helper], orders, definitions, navigation);
        check(lateral![0].Path.Waypoints[^1] == new FixedVector2(1062, 1070),
            "existing legal lateral openings retain priority and endpoint");
        orders.Stop(helper.Id, OrderSource.Player);
        check(LocalYieldPlanner.Plan(requester, path, helper.Id, [requester, helper], orders, definitions, navigation) is null,
            "forward fallback cannot recruit Stop bodies");
        orders.Clear(helper.Id);
        check(LocalYieldPlanner.Plan(requester, path, helper.Id, [requester, helper], orders, definitions, navigation) is null,
            "taskless bodies without recent party membership do not gain idle yielding permission");
    }

    private static bool RunCase(int name, int interval, FixedVector2[] goals, Action<bool, string> check)
    {
        var match = OpenGroundPartyChecks.Create();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        var preferences = new Dictionary<EntityId, FixedVector2>();
        var completed = new HashSet<EntityId>();
        var last = interval * (goals.Length - 1);
        var failed = 0;
        var yielded = false;
        RtsMatch? resumed = null;
        var elapsed = 0;
        for (; elapsed < last + 600 && (elapsed <= last || completed.Count < 24); elapsed++)
        {
            if (elapsed % interval == 0 && elapsed / interval < goals.Length)
            {
                var command = CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25 + elapsed / interval,
                    new(ids, goals[elapsed / interval]));
                check(match.SubmitCommand(command).Accepted, "arrival replacement is accepted");
                if (resumed is not null) check(resumed.SubmitCommand(command).Accepted, "resumed replacement is accepted");
                completed.Clear();
                failed = 0;
            }
            if (elapsed % 37 == 0)
                resumed = Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())));
            var before = match.Entities.ToArray();
            match.Step();
            var events = match.DrainEvents();
            resumed!.Step();
            check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"case {name}: every-frame JSON continuation state");
            check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(resumed.DrainEvents()),
                $"case {name}: every-frame JSON continuation events");
            foreach (var item in events)
            {
                if (item.Kind == MatchEventKind.GroupMoveAssigned) preferences[item.EntityId] = item.Group!.Goal!.Value;
                if (item.Kind == MatchEventKind.MoveCompleted) check(completed.Add(item.EntityId), "arrival completes once per current order");
                if (item.Kind is MatchEventKind.MoveFailed or MatchEventKind.CommandRejected) failed++;
            }
            var after = match.Entities.ToArray();
            for (var i = 0; i < after.Length; i++)
            {
                check((after[i].Position - before[i].Position).Length <= 10, "recovery retains movement speed without teleporting");
                for (var j = i + 1; j < after.Length; j++)
                    check(!CrowdGeometry.Overlap(after[i].Position, after[i].Position, 31,
                        after[j].Position, after[j].Position, 31), "recovery never overlaps bodies");
            }
            var snapshot = match.CaptureSnapshot();
            yielded |= snapshot.Cooperation!.Yields.Count > 0;
            foreach (var member in snapshot.Cooperation.Members.Where(member => member.Settled))
                check((after.Single(entity => entity.Id.Value == member.EntityId).Position
                    - match.ReadCurrentOrder(new(member.EntityId))!.Move!.Goal).Length <= 156,
                    "settled collaborators remain inside their original preferred-goal envelope");
        }
        Console.WriteLine($"arrival_timeout case={name} interval={interval} completed={completed.Count} failed={failed} active={match.ReadMoveOrders().Count} frames_after_last={elapsed - last} yielded={yielded}");
        check(completed.Count == 24 && failed == 0 && match.ReadMoveOrders().Count == 0,
            $"case {name}: all24 arrive without converting a stalled route into failure");
        check(elapsed - last <= 450, "arrival recovery has a bounded duration");
        foreach (var entity in match.Entities)
            check((entity.Position - preferences[entity.Id]).Length <= 156, "arrival preserves the existing goal envelope");
        var settled = match.Entities.ToArray();
        for (var frame = 0; frame < 90; frame++)
        {
            match.Step();
            check(settled.SequenceEqual(match.Entities) && match.DrainEvents().Count == 0,
                "recovered parties remain still and do not complete twice");
        }
        return yielded;
    }

    private static RtsMatch Restore(MatchSnapshot snapshot) =>
        RtsMatch.Restore(snapshot, GroupYieldChecks.Grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
}
