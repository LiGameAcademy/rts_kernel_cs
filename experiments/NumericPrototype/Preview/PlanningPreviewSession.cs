using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NumericPrototype.Groups;
using NumericPrototype.Movement;
using NumericPrototype.Placement;

namespace NumericPrototype;

public sealed record PreviewPoint(long X, long Y);
public sealed record PreviewMap(int Width, int Height, long CellSize, PreviewPoint Origin, byte[] Walkable);
public sealed record PreviewUnit(ulong Id, PreviewPoint Start, PreviewPoint? Goal, int Slot, bool Failed, bool Canceled);
public sealed record PreviewMetrics(int Frame, bool Planning, int Prepared, int MatchingRow,
    int Succeeded, int Failed, double CreateMs, double LastTickMs, double PeakTickMs, double PlacementMs, long AllocatedBytes);
internal sealed record PreviewSaved(int Format, string Fixture, int Frame, ulong NextId,
    GroupJobSnapshot? Job, PlacementOutcome[] Outcomes);

/// <summary>Interactive planning only. Frozen starts and final goals; never invents crowd movement.</summary>
public sealed class PlanningPreviewSession
{
    private readonly MovementWorld _world;
    private readonly PlacementMember[] _members;
    private readonly string _fixture;
    private GroupPlanningJob? _job;
    private PlacementOutcome[] _outcomes = [];
    private int _frame;
    private ulong _nextId = 1;
    private double _createMs;
    private double _lastMs;
    private double _peakMs;
    private double _placementMs;
    private long _allocated;
    public long BodyRadius => _members[0].Radius;
    public ulong Leader => _members[Count / 2].Id;
    public int Count => _members.Length;
    public bool Planning => _job is not null && !_job.Ready;
    public string WorldIdentity => _world.Identity;

    public PlanningPreviewSession(string assetsDirectory, int count)
    {
        if (count is < 2 or > 500) throw new ArgumentOutOfRangeException(nameof(count));
        _world = new(File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/pathing.json")),
            File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/terrain-heightfield.json")));
        using var balance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(assetsDirectory, "slk-exported/Units/UnitBalance.json")));
        var radius = MovementWorld.Number(balance.RootElement.GetProperty("records").EnumerateArray()
            .Single(value => value.GetProperty("unitBalanceID").GetString() == "hfoo"), "collision");
        if (radius <= 0 || radius > _world.CellSize) throw new ArgumentException("Preview radius outside audited range.");
        var points = new List<FixedPoint>();
        var separation = Math.Max(2, checked((int)((2 * radius + _world.CellSize - 1) / _world.CellSize)));
        for (var y = 2; y < _world.Height - 2 && points.Count < count * 2; y += separation)
        for (var x = 2; x < _world.Width - 2 && points.Count < count * 2; x += separation)
        {
            var point = _world.Center(x, y);
            if (CollisionOracle.Clear(_world, point, point, radius)) points.Add(point);
        }
        if (points.Count < count * 2) throw new InvalidOperationException("Insufficient real-map preview positions.");
        _members = GroupPlanningChecks.Input(points.ToArray(), count, 1).Members.Select(member =>
            new PlacementMember(member.Id, member.Start, radius, [member.Start])).ToArray();
        _fixture = Hash(JsonSerializer.Serialize(new { _world.Identity, Members = _members }));
    }

    public PreviewMap ReadMap() => new(_world.Width, _world.Height, _world.CellSize,
        Point(_world.Origin), Enumerable.Range(0, _world.Width * _world.Height)
            .Select(index => _world.Walkable(index % _world.Width, index / _world.Width) ? (byte)1 : (byte)0).ToArray());

    public void Plan(int formation, string x, string y)
    {
        var goal = FixedPoint.Parse(x, y);
        var cell = _world.Cell(goal);
        if (cell.X < 0 || cell.Y < 0 || cell.X >= _world.Width || cell.Y >= _world.Height)
            throw new ArgumentException("Click a target inside the map.");
        if (_nextId == ulong.MaxValue) throw new InvalidOperationException("Preview group IDs exhausted.");
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var ideals = FixedFormation.Create((FixedFormationKind)formation, Count, FixedPlacement.Spacing(_world, _members), goal, 0);
        var input = new GroupPlanInput(_nextId, _members.Select(member => new GroupMember(member.Id, member.Start)).ToArray(),
            ideals, _members[Count / 2].Id, false);
        var job = new GroupPlanningJob(input, _world);
        _job = job;
        _nextId++;
        _createMs = watch.Elapsed.TotalMilliseconds;
        _allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        _peakMs = 0;
        _placementMs = 0;
    }

    public bool Cancel(ulong member) => Planning && _job!.Cancel(member);

    public void Tick()
    {
        _frame++;
        if (!Planning) { _lastMs = 0; return; }
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        _job!.Advance(_world, GroupPlanningQueue.ProductionColumnsPerFrame, GroupPlanningQueue.ProductionCandidatesPerFrame);
        if (_job.Ready)
        {
            var placementWatch = Stopwatch.StartNew();
            var assignments = _job.Assignments().ToDictionary(item => item.Member, item => item.Slot);
            var active = _members.Where(member => assignments.ContainsKey(member.Id)).ToArray();
            var bodies = _members.Where(member => !assignments.ContainsKey(member.Id))
                .Select(member => new PlacementBody(member.Start, member.Radius)).ToArray();
            if (active.Length > 0)
                _outcomes = FixedPlacement.Plan(_world, active, _job.Capture().Input.Ideals, assignments, bodies);
            else _outcomes = [];
            _placementMs = placementWatch.Elapsed.TotalMilliseconds;
        }
        _lastMs = watch.Elapsed.TotalMilliseconds;
        _peakMs = Math.Max(_peakMs, _lastMs);
        _allocated += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public PreviewMetrics Metrics()
    {
        return new(_frame, Planning, _job?.PreparedCount ?? 0, _job?.MatchingRow ?? 0,
            _outcomes.Count(item => item.Goal.HasValue), _outcomes.Count(item => !item.Goal.HasValue),
            _createMs, _lastMs, _peakMs, _placementMs, _allocated);
    }

    public PreviewUnit[] ReadUnits()
    {
        var outcomes = _outcomes.ToDictionary(item => item.Member);
        return _members.Select(member =>
        {
            outcomes.TryGetValue(member.Id, out var outcome);
            return new PreviewUnit(member.Id, Point(member.Start), outcome?.Goal is FixedPoint goal ? Point(goal) : null,
                outcome?.Slot ?? -1, outcome is not null && !outcome.Goal.HasValue, _job?.IsCanceled(member.Id) ?? false);
        }).ToArray();
    }

    public string Capture() => JsonSerializer.Serialize(new PreviewSaved(1, _fixture, _frame, _nextId, _job?.Capture(), _outcomes));
    public void Restore(string json)
    {
        var saved = JsonSerializer.Deserialize<PreviewSaved>(json) ?? throw new InvalidDataException("Missing preview snapshot.");
        if (saved.Format != 1 || saved.Fixture != _fixture || saved.Frame < 0 || saved.NextId == 0 || saved.Outcomes is null
            || saved.Job is not null && (saved.Job.Input is null || saved.Job.Input.Members is null
                || saved.Job.Input.Id >= saved.NextId || !saved.Job.Input.Members.SequenceEqual(
                    _members.Select(member => new GroupMember(member.Id, member.Start))))
            || saved.Outcomes.Any(item => item is null || !_members.Any(member => member.Id == item.Member)
                || item.Slot < 0 || item.Slot >= Count)
            || saved.Outcomes.Select(item => item.Member).Distinct().Count() != saved.Outcomes.Length)
            throw new InvalidDataException("Preview snapshot does not match fixture/state.");
        var job = saved.Job is null ? null : GroupPlanningJob.Restore(saved.Job, _world);
        _job = job;
        _frame = saved.Frame;
        _nextId = saved.NextId;
        _outcomes = saved.Outcomes.ToArray();
        _lastMs = _peakMs = _placementMs = _createMs = 0;
        _allocated = 0; // Diagnostics are deliberately outside simulation state.
    }

    private static PreviewPoint Point(FixedPoint value) => new(value.X, value.Y);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
