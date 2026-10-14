using NumericPrototype.Movement;

namespace NumericPrototype.Groups;

internal sealed record GroupActivation(int Sequence, int Frame, ulong Group, ulong Member, int Slot);
internal sealed record GroupQueueSnapshot(int Format, string WorldIdentity, int Frame, int NextEvent,
    int ColumnBudget, int CandidateBudget, ulong NextGroupId, GroupJobSnapshot[] Jobs, GroupActivation[] Active);

/// <summary>FIFO numeric harness: pending jobs preserve previous activations until atomic group commit.</summary>
internal sealed class GroupPlanningQueue
{
    internal const int ProductionColumnsPerFrame = 2097152;
    internal const int ProductionCandidatesPerFrame = 64;
    private readonly MovementWorld _world;
    private readonly List<GroupPlanningJob> _jobs = [];
    private readonly SortedDictionary<ulong, GroupActivation> _active = [];
    private readonly int _columnBudget;
    private readonly int _candidateBudget;
    private int _frame;
    private int _nextEvent = 1;
    private ulong _nextGroupId = 1;

    internal GroupPlanningQueue(MovementWorld world, int columnBudget = ProductionColumnsPerFrame,
        int candidateBudget = ProductionCandidatesPerFrame)
    {
        if (columnBudget <= 0 || columnBudget > ProductionColumnsPerFrame
            || candidateBudget <= 0 || candidateBudget > ProductionCandidatesPerFrame)
            throw new ArgumentOutOfRangeException(nameof(columnBudget));
        _world = world;
        _columnBudget = columnBudget;
        _candidateBudget = candidateBudget;
    }

    internal void Enqueue(GroupPlanInput input)
    {
        if (input is null || _jobs.Count >= 16 || input.Id < _nextGroupId || input.Id == ulong.MaxValue)
            throw new ArgumentException("Invalid pending group identity/capacity.");
        var job = new GroupPlanningJob(input, _world);
        _jobs.Add(job);
        _nextGroupId = input.Id + 1;
    }

    // Append calls do not cancel planning; tests simply leave the queue untouched for Append.
    // Unit-AI replacement cannot cancel a protected player plan.
    internal int CancelMember(ulong member, bool fromUnitAi = false)
    {
        var canceled = 0;
        foreach (var job in _jobs)
            if ((!fromUnitAi || job.UnitAi) && job.Cancel(member)) canceled++;
        return canceled;
    }

    internal GroupActivation[] Step()
    {
        _frame++;
        var remaining = _columnBudget;
        while (_jobs.Count > 0 && remaining > 0)
        {
            var job = _jobs[0];
            if (job.AllCanceled)
            {
                _jobs.RemoveAt(0);
                continue;
            }
            remaining -= job.Advance(_world, remaining, _candidateBudget);
            if (!job.Ready) return [];
            _jobs.RemoveAt(0); // Commit cannot cancel its own job.
            var events = job.Assignments().Select(item =>
                new GroupActivation(_nextEvent++, _frame, job.Id, item.Member, item.Slot)).ToArray();
            foreach (var item in events) _active[item.Member] = item;
            return events; // At most one full activation in a frame.
        }
        return [];
    }

    internal GroupQueueSnapshot Capture() => new(1, _world.Identity, _frame, _nextEvent,
        _columnBudget, _candidateBudget, _nextGroupId, _jobs.Select(job => job.Capture()).ToArray(), _active.Values.ToArray());

    internal static GroupPlanningQueue Restore(MovementWorld world, GroupQueueSnapshot saved)
    {
        if (saved is null || saved.Format != 1 || saved.WorldIdentity != world.Identity || saved.Frame < 0
            || saved.NextEvent < 1 || saved.NextGroupId == 0 || saved.Jobs is null || saved.Active is null || saved.Jobs.Length > 16
            || saved.ColumnBudget <= 0 || saved.ColumnBudget > ProductionColumnsPerFrame
            || saved.CandidateBudget <= 0 || saved.CandidateBudget > ProductionCandidatesPerFrame
            || saved.Jobs.Any(job => job is null || job.Input is null || job.Input.Id >= saved.NextGroupId)
            || !saved.Jobs.Select(job => job.Input.Id).SequenceEqual(saved.Jobs.Select(job => job.Input.Id).Order().Distinct())
            || saved.Active.Any(item => item is null || item.Member == 0 || item.Group == 0 || item.Group >= saved.NextGroupId
                || item.Frame <= 0 || item.Frame > saved.Frame || item.Slot < 0 || item.Slot >= 500
                || item.Sequence < 1 || item.Sequence >= saved.NextEvent)
            || !saved.Active.Select(item => item.Member).SequenceEqual(saved.Active.Select(item => item.Member).Order().Distinct())
            || saved.Active.Select(item => item.Sequence).Distinct().Count() != saved.Active.Length
            || saved.Active.Select(item => (item.Group, item.Slot)).Distinct().Count() != saved.Active.Length)
            throw new InvalidDataException("Invalid group queue snapshot.");
        var candidate = new GroupPlanningQueue(world, saved.ColumnBudget, saved.CandidateBudget)
        {
            _frame = saved.Frame, _nextEvent = saved.NextEvent, _nextGroupId = saved.NextGroupId
        };
        foreach (var job in saved.Jobs) candidate._jobs.Add(GroupPlanningJob.Restore(job, world));
        foreach (var activation in saved.Active) candidate._active.Add(activation.Member, activation);
        return candidate;
    }
}
