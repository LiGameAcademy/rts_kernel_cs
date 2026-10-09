namespace Rts.Kernel.Navigation;

internal sealed record GroupPlanningAdvance(GroupPlanningJob Job, bool Failed);

/// <summary>Owns plan identities, FIFO lifecycle, cancellation and charged frame work.</summary>
internal sealed class GroupPlanningQueue
{
    private readonly List<GroupPlanningJob> _jobs = [];
    internal ulong NextId { get; private set; } = 1;
    internal int Count => _jobs.Count;

    internal bool TryAllocateId(out ulong id)
    {
        id = NextId;
        if (NextId == ulong.MaxValue) return false;
        NextId++;
        return true;
    }

    internal void Enqueue(GroupPlanningJob job) => _jobs.Add(job);
    internal bool HasProtectedMember(EntityId id) =>
        _jobs.Any(job => job.Command.Source != OrderSource.UnitAi && job.Contains(id));

    internal IReadOnlyList<GroupMoveOutcome> Cancel(EntityId id, OrderSource source)
    {
        var outcomes = new List<GroupMoveOutcome>();
        foreach (var job in _jobs)
            if ((source != OrderSource.UnitAi || job.Command.Source == OrderSource.UnitAi) && job.Cancel(id))
                outcomes.Add(new(job.Id, -1, null, false));
        return outcomes;
    }

    internal IReadOnlyList<GroupPlanningView> ReadViews() => Array.AsReadOnly(_jobs.Select(job =>
        new GroupPlanningView(job.Id, job.Members.Count, job.CanceledCount, job.MatchingRow)).ToArray());
    internal IReadOnlyList<GroupPlanningSnapshot> Capture() => Array.AsReadOnly(_jobs.Select(job => job.Capture()).ToArray());

    // Release a completed/failed head before reporting it; a commit never cancels its own task.
    // The match may ask for another head only if no full placement/commit has happened this frame.
    internal GroupPlanningAdvance? AdvanceNext(NavigationState navigation, ref int columnsRemaining)
    {
        while (_jobs.Count > 0 && columnsRemaining > 0)
        {
            var job = _jobs[0];
            if (job.AllCanceled)
            {
                _jobs.RemoveAt(0);
                continue;
            }
            try
            {
                columnsRemaining -= job.Advance(navigation, columnsRemaining, RtsMatch.GroupCandidateSlotsPerFrame);
            }
            catch (ArgumentException)
            {
                _jobs.RemoveAt(0);
                return new(job, true);
            }
            if (!job.Ready) return null;
            _jobs.RemoveAt(0);
            return new(job, false);
        }
        return null;
    }

    internal void Restore(MatchSnapshot snapshot, IReadOnlyDictionary<EntityId, EntityState> entities,
        MovementDefinitions definitions, NavigationState? navigation)
    {
        NextId = snapshot.NextGroupId;
        foreach (var saved in snapshot.GroupPlans!)
        {
            foreach (var member in saved.Members)
                if (!entities.TryGetValue(new EntityId(member.EntityId), out var entity)
                    || entity.OwnerId != saved.Command.PlayerId || !definitions.TryGet(entity.MovementDefinitionId, out _))
                    throw new InvalidDataException("Group planning member does not match entity content.");
            _jobs.Add(GroupPlanningJob.Restore(saved, navigation!));
        }
    }
}
