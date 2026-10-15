using System.Text.Json.Serialization;
using System.Diagnostics;
using NumericPrototype.Matching;
using NumericPrototype.Movement;

namespace NumericPrototype.Groups;

internal sealed record GroupMember(ulong Id, FixedPoint Start);
internal sealed record GroupPlanInput(ulong Id, GroupMember[] Members, FixedPoint[] Ideals,
    ulong Leader, bool UnitAi);
internal sealed record GroupJobSnapshot(GroupPlanInput Input, ulong[] Canceled,
    int Prepared, MatchingSnapshot? Matching,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MatchingPreparationSnapshot? Preparation = null);
internal sealed record CandidateCell(int X, int Y);

/// <summary>Numeric experiment for stable assignment and candidate preparation, not final placement.</summary>
internal sealed class GroupPlanningJob : IDisposable
{
    private readonly GroupPlanInput _input;
    private readonly int[] _followers;
    private readonly Int128[] _costs;
    private readonly HashSet<ulong> _canceled = [];
    private readonly List<CandidateCell[]> _candidates = [];
    private WideMatchingWork? _matching;
    private readonly MatchingPreparation? _preparation;
    internal string PreparationStage => _preparation?.Stage.ToString() ?? "Legacy";
    internal const int PreparationWorkPerFrame = 262144;
    // Wall-clock diagnostics never participate in scheduling or snapshots.
    internal double MatchingMs { get; private set; }
    internal double CandidateMs { get; private set; }
    internal double MatchingPeakMs { get; private set; }
    internal double CandidatePeakMs { get; private set; }
    internal double MatchingInitializationMs { get; private set; }
    internal double PreparationPeakMs { get; private set; }
    internal int PreparedCount => _candidates.Count;
    internal int MatchingRow => _matching?.NextRow ?? 0;
    internal bool IsCanceled(ulong member) => _canceled.Contains(member);
    internal ulong Id => _input.Id;
    internal bool UnitAi => _input.UnitAi;
    internal bool AllCanceled => _canceled.Count == _input.Members.Length;
    internal bool Ready => _matching is { Complete: true } && _candidates.Count == _input.Members.Length;

    internal GroupPlanningJob(GroupPlanInput input, MovementWorld world, bool scheduledPreparation = false)
    {
        if (input is null || input.Id == 0 || input.Members is null || input.Ideals is null
            || input.Members.Length is < 2 or > 500 || input.Ideals.Length != input.Members.Length
            || input.Members.Any(member => member is null || member.Id == 0)
            || !input.Members.Select(member => member.Id).SequenceEqual(
                input.Members.Select(member => member.Id).Order().Distinct()))
            throw new ArgumentException("Invalid experimental group input.");
        _input = Copy(input);
        foreach (var point in _input.Members.Select(member => member.Start))
        {
            var cell = world.Cell(point);
            if (cell.X < 0 || cell.Y < 0 || cell.X >= world.Width || cell.Y >= world.Height)
                throw new ArgumentException("Group start outside experimental world bounds.");
        }
        foreach (var point in _input.Ideals)
        {
            var margin = 131072L * FixedMath.One;
            if ((Int128)point.X < (Int128)world.Origin.X - margin
                || (Int128)point.Y < (Int128)world.Origin.Y - margin
                || (Int128)point.X > (Int128)world.Origin.X + (Int128)world.Width * world.CellSize + margin
                || (Int128)point.Y > (Int128)world.Origin.Y + (Int128)world.Height * world.CellSize + margin)
                throw new ArgumentException("Ideal outside experimental formation coordinate bound.");
        }
        var anchor = Array.FindIndex(_input.Members, member => member.Id == input.Leader);
        if (anchor < 0) anchor = 0; // Current kernel fallback, including absent leader.
        _followers = Enumerable.Range(0, input.Members.Length).Where(index => index != anchor).ToArray();
        _costs = new Int128[_followers.Length * _followers.Length];
        if (scheduledPreparation)
        {
            _preparation = new(_followers.Select(index => _input.Members[index].Start).ToArray(),
                _input.Ideals.Skip(1).ToArray(), _costs);
            return;
        }
        for (var row = 0; row < _followers.Length; row++)
        for (var column = 0; column < _followers.Length; column++)
        {
            var start = _input.Members[_followers[row]].Start;
            var goal = _input.Ideals[column + 1];
            var cost = WideSquaredDistance.Between(new(start.X), new(start.Y), new(goal.X), new(goal.Y)).Raw;
            if (cost > WideMatchingWork.MaximumFormationCost) throw new ArgumentException("Experimental matching cost bound exceeded.");
            _costs[row * _followers.Length + column] = cost;
        }
    }

    internal bool Contains(ulong member) => !_canceled.Contains(member)
        && _input.Members.Any(item => item.Id == member);
    internal bool Cancel(ulong member) => Contains(member) && _canceled.Add(member);

    internal int Advance(MovementWorld world, int columnBudget, int candidateBudget)
    {
        if (columnBudget <= 0 || candidateBudget <= 0) throw new ArgumentOutOfRangeException(nameof(columnBudget));
        var watch = Stopwatch.StartNew();
        var charged = 0;
        var preparing = _preparation is { Complete: false };
        if (preparing)
        {
            charged = _preparation!.Advance(Math.Min(columnBudget, PreparationWorkPerFrame));
            if (_preparation.Complete) _matching = _preparation.CreateWork();
        }
        else if (_matching is null)
        {
            _matching = new WideMatchingWork(_followers.Length, _costs, WideMatchingWork.MaximumFormationCost);
            charged = 2 * _followers.Length * _followers.Length;
            MatchingInitializationMs = watch.Elapsed.TotalMilliseconds;
        }
        if (!preparing) charged += _matching!.Advance(Math.Max(1, columnBudget - charged));
        var matchingMs = watch.Elapsed.TotalMilliseconds;
        if (preparing)
        {
            MatchingInitializationMs += matchingMs;
            PreparationPeakMs = Math.Max(PreparationPeakMs, matchingMs);
        }
        MatchingMs += matchingMs;
        MatchingPeakMs = Math.Max(MatchingPeakMs, matchingMs);
        watch.Restart();
        var end = Math.Min(_input.Ideals.Length, _candidates.Count + candidateBudget);
        while (_candidates.Count < end) _candidates.Add(Candidates(world, _input.Ideals[_candidates.Count]));
        var candidateMs = watch.Elapsed.TotalMilliseconds;
        CandidateMs += candidateMs;
        CandidatePeakMs = Math.Max(CandidatePeakMs, candidateMs);
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

    // Borrowed read-only views: the job owns immutable prepared cells and ideal values.
    internal IReadOnlyList<CandidateCell> PreparedCandidates(int slot)
    {
        if (!Ready) throw new InvalidOperationException("Group plan is pending.");
        return Array.AsReadOnly(_candidates[slot]);
    }

    internal FixedPoint Ideal(int slot) => _input.Ideals[slot];

    internal GroupJobSnapshot Capture() => new(Copy(_input), _canceled.Order().ToArray(),
        _candidates.Count, _matching?.Capture(), _preparation?.Capture());

    internal static GroupPlanningJob Restore(GroupJobSnapshot state, MovementWorld world)
    {
        if (state is null || state.Canceled is null || state.Input is null || state.Input.Members is null
            || state.Prepared < 0 || state.Prepared > state.Input.Members.Length
            || state.Prepared > 0 && state.Matching is null && state.Preparation is null
            || !state.Canceled.SequenceEqual(state.Canceled.Order().Distinct()))
            throw new InvalidDataException("Invalid group planning snapshot.");
        GroupPlanningJob job;
        try { job = new GroupPlanningJob(state.Input, world, state.Preparation is not null); }
        catch (ArgumentException error) { throw new InvalidDataException("Invalid group snapshot input.", error); }
        try
        {
            foreach (var member in state.Canceled)
                if (!job.Cancel(member)) throw new InvalidDataException("Unknown canceled group member.");
            if (state.Preparation is not null)
            {
                job._preparation!.Restore(state.Preparation);
                if (job._preparation.Complete != (state.Matching is not null))
                    throw new InvalidDataException("Matching does not agree with preparation stage.");
            }
            if (state.Matching is not null)
                job._matching = job._preparation is null
                    ? WideMatchingWork.Restore(job._followers.Length, job._costs, state.Matching, WideMatchingWork.MaximumFormationCost)
                    : WideMatchingWork.RestorePrepared(job._preparation.CreateWork(), state.Matching);
            while (job._candidates.Count < state.Prepared)
                job._candidates.Add(Candidates(world, job._input.Ideals[job._candidates.Count]));
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public void Dispose() => _preparation?.Dispose();

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
