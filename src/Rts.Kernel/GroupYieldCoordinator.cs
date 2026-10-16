using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Coordinates temporary motion and settled-order completion, without owning entity state.</summary>
internal sealed class GroupYieldCoordinator(UnitOrderBook orders, MatchEntities entities,
    MatchEventBuffer events, MovementDefinitions definitions)
{
    private GroupCooperation Cooperation => orders.Cooperation;

    internal void Prepare(long frame, NavigationState navigation)
    {
        foreach (var id in Cooperation.YieldingIds.ToArray())
        {
            var task = Cooperation.ReadYield(id);
            var requester = new EntityId(task.RequesterId);
            var helper = Cooperation.Read(id);
            var asking = Cooperation.Read(requester);
            if (!orders.IsMoving(requester) || asking is null || helper is null
                || (helper.GroupId != asking.GroupId && helper.GroupId != asking.AssistanceGroupId))
                EndYield(frame, id, navigation);
        }
        // Appending to a settled member releases exactly that old order before next-frame activation.
        CompleteSettled(frame);
    }

    internal bool Request(EntityId requester, EntityId blocker, IReadOnlyList<EntityState> bodies,
        NavigationState navigation)
    {
        if (!orders.TryReadPath(requester, out var order)) return false;
        var plan = LocalYieldPlanner.Plan(entities.Get(requester), order, blocker, bodies, orders, definitions, navigation);
        if (plan is null) return false;
        foreach (var (id, path) in plan)
        {
            var resume = orders.IsMoving(id);
            orders.SuspendPath(id);
            Cooperation.Start(id, requester, path, resume);
        }
        return true;
    }

    internal void EndYield(long frame, EntityId id, NavigationState navigation)
    {
        var task = Cooperation.End(id);
        var entity = entities.Get(id);
        entities.Update(entity with { Velocity = FixedVector2.Zero });
        if (!task.ResumeMove) return;
        var intent = orders.ReadCurrent(id)!;
        if (MovePathPlanner.TryPlan(navigation, entity.Position, intent.Move!, out var path,
                intent.Group is not null, intent.Group?.SoftLanding == true))
            orders.UpdatePath(id, path!);
        else Finish(frame, id, false, "yield_resume_path_unavailable");
    }

    internal void Arrive(long frame, EntityId id)
    {
        if (orders.ReadCurrent(id)?.Group?.SoftLanding == true && orders.PendingCount(id) == 0)
        {
            orders.SuspendPath(id);
            Cooperation.Settle(id, entities.Get(id).Position);
            entities.Update(entities.Get(id) with { Velocity = FixedVector2.Zero });
        }
        else Finish(frame, id, true, string.Empty);
    }

    internal void Finish(long frame, EntityId id, bool success, string detail)
    {
        var entity = entities.Get(id);
        Cooperation.Complete(id, success, entity.Position);
        orders.Complete(id);
        entities.Update(entity with { Velocity = FixedVector2.Zero });
        events.Publish(frame, success ? MatchEventKind.MoveCompleted : MatchEventKind.MoveFailed, id, detail);
    }

    internal void CompleteSettled(long frame)
    {
        var active = orders.ReadQueues().Where(queue => queue.Current?.Group?.SoftLanding == true)
            .Select(queue => (Id: new EntityId(queue.EntityId), Intent: queue.Current!))
            .GroupBy(pair => pair.Intent.Group!.GroupId).ToArray();
        foreach (var group in active)
        {
            var allSettled = group.All(pair => Cooperation.IsSettled(pair.Id) && !Cooperation.IsYielding(pair.Id))
                && !group.Any(pair => Cooperation.HasRequests(pair.Id));
            foreach (var (id, _) in group.OrderBy(pair => pair.Id))
                if (Cooperation.IsSettled(id) && !Cooperation.IsYielding(id)
                    && (orders.PendingCount(id) > 0 || allSettled)) Finish(frame, id, true, string.Empty);
        }
    }
}
