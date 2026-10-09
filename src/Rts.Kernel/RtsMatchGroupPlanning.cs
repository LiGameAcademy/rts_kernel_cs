using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int ImmediateGroupMembers = 64;
    public const int GroupMatchingColumnsPerFrame = 2097152;
    public const int MaximumGroupPlans = 16;
    public const int GroupCandidateSlotsPerFrame = 64;
    private readonly List<GroupPlanningJob> _groupPlans = [];

    public IReadOnlyList<GroupPlanningView> ReadGroupPlans() => Array.AsReadOnly(_groupPlans.Select(job =>
        new GroupPlanningView(job.Id, job.Members.Count, job.CanceledCount, job.MatchingRow)).ToArray());

    private void CancelGroupPlans(EntityId id, OrderSource source)
    {
        foreach (var job in _groupPlans)
            if ((source != OrderSource.UnitAi || job.Command.Source == OrderSource.UnitAi) && job.Cancel(id))
            {
                AddGroupEvent(MatchEventKind.CommandRejected, id, "group_plan_superseded", new(job.Id, -1, null, false));
            }
    }

    private void AdvanceGroupPlanning()
    {
        var remaining = GroupMatchingColumnsPerFrame;
        while (_groupPlans.Count > 0 && remaining > 0)
        {
            var job = _groupPlans[0];
            if (job.AllCanceled)
            {
                _groupPlans.RemoveAt(0);
                continue;
            }
            try
            {
                remaining -= job.Advance(_navigation!, remaining, GroupCandidateSlotsPerFrame);
            }
            catch (ArgumentException)
            {
                foreach (var member in job.ActiveMembers)
                    AddGroupEvent(MatchEventKind.CommandRejected, new EntityId(member.EntityId),
                        "invalid_group_geometry", new(job.Id, -1, null, false));
                _groupPlans.RemoveAt(0);
                continue;
            }
            if (!job.Ready) break;
            _groupPlans.RemoveAt(0); // Committing this job must not cancel itself.
            var activeIds = job.ActiveMembers
                .Select(member => new EntityId(member.EntityId)).ToArray();
            var activeCommand = job.Command with { Group = job.Command.Group! with { EntityIds = activeIds } };
            var members = PrepareGroupMembers(activeCommand, job.Id).ToArray();
            if (members.Length == 0) continue;
            try
            {
                var goals = job.PlanPlacement(_navigation!, members, GroupBodies(members));
                CommitPlannedGroupGoals(job.Command, job.Id, job.Heading, goals);
            }
            catch (ArgumentException)
            {
                foreach (var member in members)
                    AddGroupEvent(MatchEventKind.CommandRejected, member.Id, "invalid_group_geometry", new(job.Id, -1, null, false));
            }
            // At most one full placement/commit in a frame, even if several matchings have completed.
            break;
        }
    }

    private void RestoreGroupPlans(MatchSnapshot snapshot)
    {
        foreach (var saved in snapshot.GroupPlans!)
        {
            foreach (var member in saved.Members)
                if (!_entities.TryGetValue(new EntityId(member.EntityId), out var entity)
                    || entity.OwnerId != saved.Command.PlayerId || ReadMovementDefinition(entity.Id) is null)
                    throw new InvalidDataException("Group planning member does not match entity content.");
            _groupPlans.Add(GroupPlanningJob.Restore(saved, _navigation!));
        }
    }
}
