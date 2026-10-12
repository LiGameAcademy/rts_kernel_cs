using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int ImmediateGroupMembers = 64;
    public const int GroupMatchingColumnsPerFrame = 2097152;
    public const int MaximumGroupPlans = 16;
    public const int GroupCandidateSlotsPerFrame = 64;
    private readonly GroupPlanningQueue _planning = new();

    public IReadOnlyList<GroupPlanningView> ReadGroupPlans() => _planning.ReadViews();

    private void CancelGroupPlans(EntityId id, OrderSource source)
    {
        foreach (var outcome in _planning.Cancel(id, source))
            AddGroupEvent(MatchEventKind.CommandRejected, id, "group_plan_superseded", outcome);
    }

    private void AdvanceGroupPlanning()
    {
        var remaining = GroupMatchingColumnsPerFrame;
        while (_planning.AdvanceNext(_navigation!, ref remaining) is { } progress)
        {
            var job = progress.Job;
            if (progress.Failed)
            {
                foreach (var member in job.ActiveMembers)
                    AddGroupEvent(MatchEventKind.CommandRejected, new EntityId(member.EntityId),
                        "invalid_group_geometry", new(job.Id, -1, null, false));
                continue;
            }
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

}
