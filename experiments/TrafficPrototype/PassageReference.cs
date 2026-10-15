using System.Text.Json;
using Rts.Kernel.Navigation;
using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

internal readonly record struct PassageState(GridCell A, GridCell B);
internal sealed record PassageInput(IReadOnlyList<GridCell> Cells, PassageState Start, PassageState Goal,
    bool StopA = false, bool StopB = false, long OrderRevision = 0);
internal enum PassageStage { Searching, Reconstructing, Found, Unreachable }

/// <summary>
/// Tiny exact joint-search oracle, limited to two radius-4 bodies and <=16 cardinal grid cells.
/// It demonstrates a shared yield plan; it is not the proposed production traffic algorithm.
/// Each Advance quantum dequeues one state, tests one joint transition, or follows one parent.
/// </summary>
internal sealed class PassageReference
{
    private static readonly GridCell[] Directions = [new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
    private readonly PassageInput _input;
    private readonly HashSet<GridCell> _cells;
    private readonly Queue<PassageState> _frontier = new();
    private readonly Dictionary<PassageState, PassageState?> _parents = [];
    private readonly List<PassageState> _reverse = [];
    private PassageState? _current;
    private int _candidate;
    private PassageState? _trace;
    internal PassageStage Stage { get; private set; }
    internal int SpentWork { get; private set; }
    internal int Expanded { get; private set; }
    internal long OrderRevision => _input.OrderRevision;
    internal bool Complete => Stage is PassageStage.Found or PassageStage.Unreachable;

    internal PassageReference(PassageInput input)
    {
        _input = Freeze(input);
        _cells = _input.Cells.ToHashSet();
        _frontier.Enqueue(input.Start);
        _parents.Add(input.Start, null);
    }

    internal static PassageInput Freeze(PassageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Cells is null || input.Cells.Count is < 1 or > 16 || input.OrderRevision < 0
            || input.Cells.Any(cell => cell.X is < 0 or > 16 || cell.Y is < 0 or > 16))
            throw new ArgumentException("Passage oracle accepts 1..16 small nonnegative cells.");
        var cells = input.Cells.ToHashSet();
        bool ContainsState(PassageState state) => state.A != state.B && cells.Contains(state.A) && cells.Contains(state.B);
        if (cells.Count != input.Cells.Count || !ContainsState(input.Start) || !ContainsState(input.Goal)
            || (input.StopA && input.Start.A != input.Goal.A) || (input.StopB && input.Start.B != input.Goal.B))
            throw new ArgumentException("Invalid passage identity, endpoint or Stop goal.");
        return input with { Cells = Array.AsReadOnly(input.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X).ToArray()) };
    }

    internal bool Matches(PassageInput input) => OrderRevision == input.OrderRevision
        && _input.Start == input.Start && _input.Goal == input.Goal && _input.StopA == input.StopA && _input.StopB == input.StopB
        && _input.Cells.SequenceEqual(input.Cells.OrderBy(cell => cell.Y).ThenBy(cell => cell.X));

    internal int Advance(int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var used = 0;
        while (used < budget && !Complete)
        {
            used++;
            SpentWork++;
            if (Stage == PassageStage.Reconstructing)
            {
                var cursor = _trace!.Value;
                _reverse.Add(cursor);
                _trace = _parents[cursor];
                if (_trace is null) Stage = PassageStage.Found;
                continue;
            }
            if (_current is null)
            {
                if (!_frontier.TryDequeue(out var state))
                {
                    Stage = PassageStage.Unreachable;
                    continue;
                }
                Expanded++;
                if (state == _input.Goal)
                {
                    Stage = PassageStage.Reconstructing;
                    _trace = state;
                    continue;
                }
                _current = state;
                _candidate = 0;
                continue;
            }
            var from = _current.Value;
            var da = Directions[_candidate / Directions.Length];
            var db = Directions[_candidate % Directions.Length];
            _candidate++;
            if (_candidate == Directions.Length * Directions.Length) _current = null;
            var next = new PassageState(new(from.A.X + da.X, from.A.Y + da.Y), new(from.B.X + db.X, from.B.Y + db.Y));
            if (!Contains(next) || (_input.StopA && next.A != from.A) || (_input.StopB && next.B != from.B)
                || _parents.ContainsKey(next) || !SafeTransition(from, next)) continue;
            _parents.Add(next, from);
            _frontier.Enqueue(next);
        }
        return used;
    }

    internal IReadOnlyList<PassageState> ReadPlan() => Stage == PassageStage.Found
        ? Array.AsReadOnly(_reverse.AsEnumerable().Reverse().ToArray()) : Array.Empty<PassageState>();

    // This small reference reconstructs its deterministic cursor by replay. This cost is not a production restore claim.
    internal string CaptureJson() => JsonSerializer.Serialize(new Checkpoint(1, _input, SpentWork));
    internal static PassageReference Restore(string json)
    {
        var saved = JsonSerializer.Deserialize<Checkpoint>(json) ?? throw new InvalidDataException("Missing passage checkpoint.");
        if (saved.Version != 1 || saved.SpentWork is < 0 or > 10000)
            throw new InvalidDataException("Invalid passage work checkpoint.");
        var job = new PassageReference(saved.Input);
        if (saved.SpentWork > 0) job.Advance(saved.SpentWork);
        if (job.SpentWork != saved.SpentWork) throw new InvalidDataException("Checkpoint work extends past completion.");
        return job;
    }

    internal static bool SafeTransition(PassageState from, PassageState to) =>
        Adjacent(from.A, to.A) && Adjacent(from.B, to.B)
        && !LinearTimeCollision.Overlaps(World(from.A), World(to.A), 4, World(from.B), World(to.B), 4);
    private bool Contains(PassageState state) => state.A != state.B && _cells.Contains(state.A) && _cells.Contains(state.B);
    private static bool Adjacent(GridCell a, GridCell b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= 1;
    private static FixedVector2 World(GridCell cell) => new(cell.X * 10, cell.Y * 10);
    private sealed record Checkpoint(int Version, PassageInput Input, int SpentWork);
}
