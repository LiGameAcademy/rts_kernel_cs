using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int ImmediateGroupMembers = 64;
    public const int GroupMatchingColumnsPerFrame = 1048576;
    public const int MaximumGroupPlans = 16;
    public const int GroupCandidateSlotsPerFrame = 64;
    private readonly List<GroupPlanningJob> _groupPlans = [];

    public IReadOnlyList<GroupPlanningView> ReadGroupPlans() => Array.AsReadOnly(_groupPlans.Select(job =>
        new GroupPlanningView(job.Id, job.Members.Length, job.Canceled.Count, job.Matching?.NextRow ?? 0)).ToArray());

    private void CancelGroupPlans(EntityId id, OrderSource source)
    {
        if (source == OrderSource.UnitAi) return;
        foreach (var job in _groupPlans)
            if (job.Contains(id))
            {
                job.Canceled.Add(id.Value);
                AddGroupEvent(MatchEventKind.CommandRejected, id, "group_plan_superseded", new(job.Id, -1, null, false));
            }
    }

    private void AdvanceGroupPlanning()
    {
        var remaining = GroupMatchingColumnsPerFrame;
        while (_groupPlans.Count > 0 && remaining > 0)
        {
            var job = _groupPlans[0];
            if (job.Canceled.Count == job.Members.Length) { _groupPlans.RemoveAt(0); continue; }
            try
            {
                if (job.Matching is null)
                {
                    job.StartMatching();
                    remaining -= 2 * (job.Members.Length - 1) * (job.Members.Length - 1);
                }
                remaining -= job.Matching!.Advance(Math.Max(1, remaining));
            }
            catch (ArgumentException)
            {
                foreach (var member in job.Members.Where(member => !job.Canceled.Contains(member.EntityId)))
                    AddGroupEvent(MatchEventKind.CommandRejected, new EntityId(member.EntityId),
                        "invalid_group_geometry", new(job.Id, -1, null, false));
                _groupPlans.RemoveAt(0);
                continue;
            }
            if (!job.Matching!.Complete) break;
            var end = Math.Min(job.Members.Length, job.Candidates.Count + GroupCandidateSlotsPerFrame);
            for (var i = job.Candidates.Count; i < end; i++)
                job.Candidates.Add(GroupPlacementPlanner.Candidates(_navigation!, job.Ideals[i]));
            if (job.Candidates.Count != job.Members.Length) break;
            _groupPlans.RemoveAt(0); // Committing this job must not cancel itself.
            var activeIds = job.Members.Where(member => !job.Canceled.Contains(member.EntityId))
                .Select(member => new EntityId(member.EntityId)).ToArray();
            var activeCommand = job.Command with { Group = job.Command.Group! with { EntityIds = activeIds } };
            var members = PrepareGroupMembers(activeCommand, job.Id).ToArray();
            if (members.Length == 0) continue;
            var result = job.Matching.Result();
            var slots = members.Select(member => result[Array.FindIndex(job.Members,
                saved => saved.EntityId == member.Id.Value)]).ToArray();
            try
            {
                var goals = GroupPlacementPlanner.PlanAssigned(_navigation!, members, job.Ideals, slots, GroupBodies(members), job.Candidates);
                ApplyGroupGoals(job.Command, job.Id, job.Heading, goals, true);
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
