using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupEdgeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definitions = new[] { new MovementDefinition(1, 30, 4) };
        var flags = new byte[25 * 25];
        var grid = new PathingGrid(25, 25, 10, SimVector2.Zero, flags);
        RtsMatch Create()
        {
            var m = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitions);
            m.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(25, 25), 1));
            m.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new SimVector2(45, 25), 1));
            m.SubmitCommand(CommandEnvelope.Spawn(1, 1, 2, new SimVector2(125, 125), 1));
            m.Step(); m.DrainEvents(); return m;
        }
        var ids = new[] { new EntityId(1), new EntityId(2) };
        var request = new GroupMoveRequest(ids, new SimVector2(125, 125), FormationKind.Rectangle, new EntityId(2), Math.PI / 2);
        var m = Create();
        m.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, request)); m.Step();
        var assigned = m.DrainEvents().Where(e => e.Kind == MatchEventKind.GroupMoveAssigned).ToArray();
        check(assigned.Length == 2 && m.ReadCurrentOrder(new EntityId(2))!.Group!.Slot == 0, "explicit leader need not be first selected");
        check(assigned.All(e => e.Group!.Goal != new SimVector2(125, 125)), "stationary external unit cannot be overlapped at anchor");
        check(m.ReadCurrentOrder(new EntityId(2))!.Group!.Adjusted, "local obstacle deformation is reported");
        var old = m.ReadCurrentOrder(new EntityId(1));
        m.SubmitCommand(CommandEnvelope.MoveTo(3, 0, 0, new EntityId(1), new SimVector2(225, 225), 999));
        m.Step();
        check(m.ReadCurrentOrder(new EntityId(1)) == old
            && m.DrainEvents().Any(e => e.Detail == "move_does_not_match_definition"),
            "diagnostic single move cannot bypass frozen capabilities");
        m.SubmitCommand(CommandEnvelope.MoveGroup(4, 1, 2, request)); m.Step();
        check(m.ReadCurrentOrder(new EntityId(1)) == old, "not-owned group members retain previous orders");
        check(m.DrainEvents().Count(e => e.Detail == "entity_missing_or_not_owned") == 2, "each not-owned member reported");
        var saved = m.CaptureSnapshot();
        var order = saved.Orders![0];
        var changed = saved with { Orders = new[] { order with { Current = order.Current! with
            { Group = order.Current!.Group! with { Heading = -1 } } }, saved.Orders[1] } };
        var rejected = false;
        try { SnapshotValidator.Validate(changed); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "conflicting geometry within same group rejected");
        changed = saved with { Orders = new[] { order with { Current = order.Current! with
            { Move = order.Current!.Move! with { Speed = 500 } } }, saved.Orders[1] },
            Moves = saved.Moves!.Select(move => move.EntityId == order.EntityId ? move with
            { Request = move.Request with { Speed = 500 } } : move).ToArray() };
        rejected = false;
        try { RtsMatch.Restore(changed, grid, movementDefinitions: definitions); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "group request cannot forge movement speed through snapshot");
        var edge = Create();
        edge.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, request with { Goal = SimVector2.Zero })); edge.Step();
        check(edge.DrainEvents().Count(e => e.Kind == MatchEventKind.GroupMoveAssigned) == 2, "map-edge anchor adjusts into valid cells");
        var partial = Create();
        partial.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, request with { EntityIds = new[] { ids[0], new EntityId(999), ids[1] } })); partial.Step();
        var outcomes = partial.DrainEvents();
        check(outcomes.Count(e => e.Kind == MatchEventKind.GroupMoveAssigned) == 2
            && outcomes.Any(e => e.EntityId.Value == 999 && e.Detail == "entity_missing_or_not_owned"), "missing member does not abort other members");
        var constrainedFlags = Enumerable.Repeat((byte)2, 625).ToArray();
        constrainedFlags[12 * 25 + 12] = 0;
        constrainedFlags[2 * 25 + 2] = 0;
        var constrainedGrid = new PathingGrid(25, 25, 10, SimVector2.Zero, constrainedFlags);
        var constrained = new RtsMatch(MatchConfig.Default, 7, constrainedGrid, movementDefinitions: definitions);
        constrained.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(125, 125), 1));
        constrained.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new SimVector2(25, 25), 1)); constrained.Step(); constrained.DrainEvents();
        constrained.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, new(ids, new SimVector2(125, 125)))); constrained.Step();
        check(constrained.DrainEvents().Any(e => e.Detail == "group_slot_unavailable"), "insufficient space and disconnected members explicitly fail");
        var collisionFlags = new byte[625]; collisionFlags[2 * 25 + 3] = 2;
        var collisionGrid = new PathingGrid(25, 25, 10, SimVector2.Zero, collisionFlags);
        var corner = new RtsMatch(MatchConfig.Default, 7, collisionGrid, movementDefinitions: definitions);
        corner.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(25, 25), 1)); corner.Step();
        corner.SubmitCommand(CommandEnvelope.MoveGroup(2, 0, 1, new(new[] { ids[0] }, new SimVector2(65, 65)))); corner.Step();
        var recovered = RtsMatch.Restore(corner.CaptureSnapshot(), collisionGrid, movementDefinitions: definitions);
        check(corner.ComputeStateHash() == recovered.ComputeStateHash(), "group direct route falls back around blocked corners and restores");
        foreach (var bad in new[] { request with { Formation = (FormationKind)99 }, request with { Heading = double.NaN },
            request with { LeaderId = new EntityId(99) }, request with { Goal = new SimVector2(double.PositiveInfinity, 0) } })
            check(!m.SubmitCommand(CommandEnvelope.MoveGroup(m.Frame + 1, 0, 5, bad)).Accepted, "malformed group payload rejected");
        foreach (var bad in new[] { new MovementDefinition(0, 1, 1), new MovementDefinition(1, double.NaN, 1),
            new MovementDefinition(1, 1, 0), new MovementDefinition(1, 1, double.PositiveInfinity) })
        {
            rejected = false;
            try { _ = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: new[] { bad }); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid frozen movement definition rejected before match creation");
        }
        rejected = false;
        try { _ = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: new[] { definitions[0], definitions[0] }); }
        catch (ArgumentException) { rejected = true; }
        check(rejected, "duplicate movement definition IDs rejected");
        var definitionArray = definitions.ToArray();
        var frozen = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: definitionArray);
        var frozenHash = frozen.ComputeStateHash(); definitionArray[0] = new(1, 999, 99);
        check(frozen.ComputeStateHash() == frozenHash, "caller changing definition collection cannot mutate match content");
    }
}
