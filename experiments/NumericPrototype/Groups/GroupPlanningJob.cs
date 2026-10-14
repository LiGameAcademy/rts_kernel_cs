using NumericPrototype.Matching;
using NumericPrototype.Movement;

namespace NumericPrototype.Groups;

internal sealed record GroupMember(ulong Id, FixedPoint Start);
internal sealed record GroupPlanInput(ulong Id, GroupMember[] Members, FixedPoint[] Ideals,
    ulong Leader, bool UnitAi);
internal sealed record GroupJobSnapshot(GroupPlanInput Input, ulong[] Canceled,
    int Prepared, MatchingSnapshot? Matching);
internal sealed record CandidateCell(int X, int Y);

/// <summary>Numeric experiment for stable assignment and candidate preparation, not final placement.</summary>
internal sealed class GroupPlanningJob
{
    private readonly GroupPlanInput _input;
    private readonly int[] _followers;
    private readonly Int128[] _costs;
    private readonly HashSet<ulong> _canceled = [];
    private readonly List<CandidateCell[]> _candidates = [];
    private WideMatchingWork? _matching;
    internal ulong Id => _input.Id;
    internal bool UnitAi => _input.UnitAi;
    internal bool AllCanceled => _canceled.Count == _input.Members.Length;
    internal bool Ready => _matching is { Complete: true } && _candidates.Count == _input.Members.Length;

    internal GroupPlanningJob(GroupPlanInput input, MovementWorld world)
    {
        if (input is null || input.Id == 0 || input.Members is null || input.Ideals is null
            || input.Members.Length is < 2 or > 500 || input.Ideals.Length != input.Members.Length
            || input.Members.Any(member => member is null || member.Id == 0)
            || !input.Members.Select(member => member.Id).SequenceEqual(
                input.Members.Select(member => member.Id).Order().Distinct()))
            throw new ArgumentException("Invalid experimental group input.");
        _input = Copy(input);
        foreach (var point in _input.Members.Select(member => member.Start).Concat(_input.Ideals))
        {
            var cell = world.Cell(point);
            if (cell.X < 0 || cell.Y < 0 || cell.X >= world.Width || cell.Y >= world.Height)
                throw new ArgumentException("Group point outside experimental world bounds.");
        }
        var anchor = Array.FindIndex(_input.Members, member => member.Id == input.Leader);
        if (anchor < 0) anchor = 0; // Current kernel fallback, including absent leader.
        _followers = Enumerable.Range(0, input.Members.Length).Where(index => index != anchor).ToArray();
        _costs = new Int128[_followers.Length * _followers.Length];
        for (var row = 0; row < _followers.Length; row++)
        for (var column = 0; column < _followers.Length; column++)
        {
            var start = _input.Members[_followers[row]].Start;
            var goal = _input.Ideals[column + 1];
            _costs[row * _followers.Length + column] = WideSquaredDistance.Between(
                new(start.X), new(start.Y), new(goal.X), new(goal.Y)).Raw;
        }
    }

    internal bool Contains(ulong member) => !_canceled.Contains(member)
        && _input.Members.Any(item => item.Id == member);
    internal bool Cancel(ulong member) => Contains(member) && _canceled.Add(member);

    internal int Advance(MovementWorld world, int columnBudget, int candidateBudget)
    {
        if (columnBudget <= 0 || candidateBudget <= 0) throw new ArgumentOutOfRangeException(nameof(columnBudget));
        var charged = 0;
        if (_matching is null)
        {
            _matching = new WideMatchingWork(_followers.Length, _costs);
            charged = 2 * _followers.Length * _followers.Length;
        }
        charged += _matching.Advance(Math.Max(1, columnBudget - charged));
        var end = Math.Min(_input.Ideals.Length, _candidates.Count + candidateBudget);
        while (_candidates.Count < end) _candidates.Add(Candidates(world, _input.Ideals[_candidates.Count]));
        return charged;
    }

    internal (ulong Member, int Slot)[] Assignments()
    {
        if (!Ready) throw new InvalidOperationException("Group plan is pending.");
        var followerSlots = _matching!.Result();
        var slots = new int[_input.Members.Length];
        for (var row = 0; row < _followers.Length; row++) slots[_followers[row]] = followerSlots[row] + 1;
        return _input.Members.Select((member, index) => (Member: member.Id, Slot: slots[index]))
            .Where(item => !_canceled.Contains(item.Member)).ToArray();
    }

    internal GroupJobSnapshot Capture() => new(Copy(_input), _canceled.Order().ToArray(),
        _candidates.Count, _matching?.Capture());

    internal static GroupPlanningJob Restore(GroupJobSnapshot state, MovementWorld world)
    {
        if (state is null || state.Canceled is null || state.Input is null || state.Input.Members is null
            || state.Prepared < 0 || state.Prepared > state.Input.Members.Length
            || state.Prepared > 0 && state.Matching is null
            || !state.Canceled.SequenceEqual(state.Canceled.Order().Distinct()))
            throw new InvalidDataException("Invalid group planning snapshot.");
        GroupPlanningJob job;
        try { job = new GroupPlanningJob(state.Input, world); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid group snapshot input.", error); }
        foreach (var member in state.Canceled)
            if (!job.Cancel(member)) throw new InvalidDataException("Unknown canceled group member.");
        if (state.Matching is not null)
            job._matching = WideMatchingWork.Restore(job._followers.Length, job._costs, state.Matching);
        while (job._candidates.Count < state.Prepared)
            job._candidates.Add(Candidates(world, job._input.Ideals[job._candidates.Count]));
        return job;
    }

    internal string CandidateTrace() => System.Text.Json.JsonSerializer.Serialize(_candidates);
    private static GroupPlanInput Copy(GroupPlanInput input) => input with
    {
        Members = input.Members.ToArray(), Ideals = input.Ideals.ToArray()
    };

    internal static CandidateCell[] Candidates(MovementWorld world, FixedPoint ideal)
    {
        var cell = world.Cell(ideal);
        var values = new List<(CandidateCell Cell, Int128 Distance)>();
        for (var y = Math.Max(0, cell.Y - 8); y <= Math.Min(world.Height - 1, cell.Y + 8); y++)
        for (var x = Math.Max(0, cell.X - 8); x <= Math.Min(world.Width - 1, cell.X + 8); x++)
        {
            var center = world.Center(x, y);
            values.Add((new(x, y), WideSquaredDistance.Between(
                new(center.X), new(center.Y), new(ideal.X), new(ideal.Y)).Raw));
        }
        // Preparation intentionally includes blocked cells, as production does; placement filters later.
        return values.OrderBy(item => item.Distance).ThenBy(item => item.Cell.Y).ThenBy(item => item.Cell.X)
            .Select(item => item.Cell).ToArray();
    }
}
