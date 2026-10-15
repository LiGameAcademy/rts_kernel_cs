using System.Diagnostics;
using NumericPrototype.Groups;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal sealed record PlacementWorkSnapshot(int Format, int Ticks);

/// <summary>Resumable final placement; results remain private until the complete failure fixed point.</summary>
internal sealed class PlacementWork
{
    internal const int CandidateWorkPerTick = 262144;
    internal const int ConnectivityWorkPerTick = 1048576;
    private readonly MovementWorld _world;
    private readonly PlacementMember[] _members;
    private readonly GroupPlanningJob _job;
    private readonly Dictionary<ulong, int> _slots;
    private readonly PlacementBody[] _external;
    private readonly PlacementConnectivity _cache;
    private readonly long[] _radii;
    private readonly Dictionary<long, ConnectivityWork> _connections = [];
    private readonly IReadOnlyList<CandidateCell>[] _candidates;
    private readonly HashSet<ulong> _failed = [];
    private readonly List<PlacementOutcome> _results = [];
    private PlacementBodyIndex _occupied;
    private PlacementBody[] _retained = [];
    private int _radius;
    private int _body;
    private int _member;
    private int _cell;
    private int _passFailures;
    private int _ticks;
    private bool _passStarted;
    internal bool Complete { get; private set; }
    internal string Stage => _radius < _radii.Length ? "Connectivity" : Complete ? "Complete" : "Placement";
    internal double ConnectivityMs { get; private set; }

    internal PlacementWork(MovementWorld world, PlacementMember[] members, GroupPlanningJob job,
        Dictionary<ulong, int> slots, PlacementBody[] external, PlacementConnectivity cache)
    {
        _world = world;
        _job = job;
        _slots = slots;
        _members = members.OrderBy(member => slots[member.Id]).ToArray();
        _external = external;
        _cache = cache;
        _radii = members.Select(member => member.Radius).Distinct().Order().ToArray();
        _candidates = _members.Select(member => job.PreparedCandidates(slots[member.Id])).ToArray();
        _occupied = new(world);
    }

    internal void Advance()
    {
        if (Complete) return;
        _ticks++;
        if (_radius < _radii.Length)
        {
            var watch = Stopwatch.StartNew();
            var radius = _radii[_radius];
            if (!_connections.TryGetValue(radius, out var work))
            {
                work = _cache.Start(radius);
                _connections.Add(radius, work);
            }
            work.Advance(ConnectivityWorkPerTick);
            if (work.Complete)
            {
                _cache.Remember(work);
                _radius++;
            }
            ConnectivityMs += watch.Elapsed.TotalMilliseconds;
            return;
        }
        var remaining = CandidateWorkPerTick;
        while (!Complete && remaining > 0)
        {
            if (!_passStarted) StartPass();
            if (_body < _retained.Length)
            {
                _occupied.Add(_retained[_body++]);
                remaining--;
                continue;
            }
            if (_member == _members.Length)
            {
                if (_failed.Count == _passFailures) Complete = true;
                else _passStarted = false;
                remaining--;
                continue;
            }
            var member = _members[_member];
            if (_failed.Contains(member.Id) || _cell == _candidates[_member].Count)
            {
                _failed.Add(member.Id);
                FinishMember(null);
                remaining--;
                continue;
            }
            if (remaining < 64) break;
            remaining -= 64;
            var cell = _candidates[_member][_cell++];
            var point = _world.Center(cell.X, cell.Y);
            if (!_connections[member.Radius].Connected(member.Start, point) || _occupied.Overlaps(point, member.Radius)) continue;
            _occupied.Add(new(point, member.Radius));
            FinishMember(point);
        }
    }

    private void StartPass()
    {
        _passFailures = _failed.Count;
        _occupied = new(_world);
        _retained = _external.Concat(_members.Where(member => _failed.Contains(member.Id))
            .SelectMany(member => member.Retained.Select(point => new PlacementBody(point, member.Radius)))).ToArray();
        _results.Clear();
        _body = _member = _cell = 0;
        _passStarted = true;
    }

    private void FinishMember(FixedPoint? goal)
    {
        var member = _members[_member];
        var slot = _slots[member.Id];
        _results.Add(new(member.Id, slot, goal, goal.HasValue && goal.Value != _job.Ideal(slot)));
        _member++;
        _cell = 0;
    }

    internal PlacementOutcome[] Results() => Complete ? _results.OrderBy(item => item.Member).ToArray()
        : throw new InvalidOperationException("Final placement is pending.");
    internal PlacementWorkSnapshot Capture() => new(1, _ticks);

    internal void Restore(PlacementWorkSnapshot saved)
    {
        // Each tick performs at least one primitive operation. Include every radius build
        // and at most n+1 failure passes; legal dense cases must not outgrow an arbitrary cap.
        var maximumTicks = checked(2L + _radii.Length * (91L * _world.Width * _world.Height + 2)
            + (_members.Length + 1L) * (_external.Length + _members.Sum(member => (long)member.Retained.Length)
                + _members.Length * 291L + 2));
        if (saved.Format != 1 || saved.Ticks < 0 || saved.Ticks > maximumTicks || _ticks != 0)
            throw new InvalidDataException("Invalid experimental placement cursor.");
        while (_ticks < saved.Ticks && !Complete) Advance();
        if (_ticks != saved.Ticks) throw new InvalidDataException("Placement cursor exceeds reachable work.");
        ConnectivityMs = 0; // Replay diagnostics are not simulation state or measured tick costs.
    }
}
