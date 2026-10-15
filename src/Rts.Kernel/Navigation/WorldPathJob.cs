namespace Rts.Kernel.Navigation;

internal enum WorldPathPhase { Start, Search, Points, Goal, Complete }
internal sealed record WorldPathSnapshot(FixedVector2 Origin, MoveRequest Request, WorldPathPhase Phase,
    int PointCursor, IReadOnlyList<FixedVector2> Points, GridSearchSnapshot? Search, PathStatus? Status);

/// <summary>Owns incremental cell search and world-point conversion; it never mutates an order.</summary>
internal sealed class WorldPathJob(FixedVector2 origin, MoveRequest request)
{
    private GridSearchJob? _search;
    private WorldPathPhase _phase;
    private PathStatus? _status;
    private int _pointCursor;
    private readonly List<FixedVector2> _points = [];

    internal bool IsComplete => _phase == WorldPathPhase.Complete;
    internal PathStatus? Status => _status;
    internal MoveOrder? Order => _status == PathStatus.Found && IsComplete
        ? new(request, _points.AsReadOnly()) : null;

    internal int Advance(NavigationState navigation, int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        var spent = 0;
        while (spent < budget && !IsComplete)
        {
            if (_phase == WorldPathPhase.Search)
            {
                spent += _search!.Advance(budget - spent);
                if (!_search.IsComplete) break;
                if (_search.Result!.Status != PathStatus.Found) Finish(_search.Result.Status);
                else _phase = WorldPathPhase.Points;
                continue;
            }
            spent++;
            switch (_phase)
            {
                case WorldPathPhase.Start:
                    if (!request.IsValid || !navigation.TryWorldToCell(origin, out var start)
                        || !navigation.TryWorldToCell(request.Goal, out var goal))
                    {
                        Finish(PathStatus.InvalidEndpoint);
                        break;
                    }
                    _points.Add(origin);
                    _search = new(navigation.Grid, start, goal, request.ClearanceCells, navigation.IsWalkableCell);
                    _phase = WorldPathPhase.Search;
                    break;
                case WorldPathPhase.Points:
                    var cells = _search!.Result!.Cells;
                    if (cells.Count > 1 && _pointCursor < cells.Count)
                    {
                        var center = navigation.CellCenter(cells[_pointCursor++]);
                        if (!SimulationDomain.Contains(center)) { Finish(PathStatus.InvalidEndpoint); break; }
                        if (_points[^1] != center) _points.Add(center);
                    }
                    else _phase = WorldPathPhase.Goal;
                    break;
                case WorldPathPhase.Goal:
                    if (_points[^1] != request.Goal) _points.Add(request.Goal);
                    Finish(PathStatus.Found);
                    break;
                default: throw new InvalidOperationException("Unknown world path phase.");
            }
        }
        return spent;
    }

    private void Finish(PathStatus status) { _status = status; _phase = WorldPathPhase.Complete; }

    internal WorldPathSnapshot Capture() => new(origin, request, _phase, _pointCursor,
        Array.AsReadOnly(_points.ToArray()), _search?.Capture(), _status);

    internal static WorldPathJob Restore(WorldPathSnapshot saved, NavigationState navigation)
    {
        if (saved is null || saved.Request is not { IsValid: true } || !SimulationDomain.Contains(saved.Origin)
            || !Enum.IsDefined(saved.Phase) || saved.Points is null || saved.PointCursor < 0
            || saved.Points.Any(point => !SimulationDomain.Contains(point))
            || ((saved.Phase == WorldPathPhase.Complete) != (saved.Status is not null))
            || (saved.Status is not null && saved.Status is not (PathStatus.Found or PathStatus.InvalidEndpoint or PathStatus.Unreachable))
            || (saved.Phase == WorldPathPhase.Start && (saved.Search is not null || saved.Points.Count != 0))
            || (saved.Phase is WorldPathPhase.Search or WorldPathPhase.Points or WorldPathPhase.Goal && saved.Search is null)
            || (saved.Points.Count > 0 && saved.Points[0] != saved.Origin))
            throw new InvalidDataException("Invalid world path job snapshot.");
        var job = new WorldPathJob(saved.Origin, saved.Request)
        { _phase = saved.Phase, _pointCursor = saved.PointCursor, _status = saved.Status };
        if (saved.Search is not null)
        {
            if (!navigation.TryWorldToCell(saved.Origin, out var start)
                || !navigation.TryWorldToCell(saved.Request.Goal, out var goal)
                || saved.Search.Start != start || saved.Search.Goal != goal
                || saved.Search.Clearance != saved.Request.ClearanceCells
                || saved.PointCursor > saved.Search.Path.Count)
                throw new InvalidDataException("Search endpoints or conversion cursor disagree with movement request.");
            job._search = GridSearchJob.Restore(navigation.Grid, saved.Search, navigation.IsWalkableCell);
            if (saved.Phase is WorldPathPhase.Points or WorldPathPhase.Goal || saved.Status == PathStatus.Found)
            {
                if (!job._search.IsComplete || job._search.Result!.Status != PathStatus.Found)
                    throw new InvalidDataException("World conversion requires a completed cell path.");
            }
        }
        else if (saved.Status is not null and not PathStatus.InvalidEndpoint)
            throw new InvalidDataException("World path outcome requires its search state.");
        if (saved.Status == PathStatus.Found
            && (saved.Points.Count == 0 || saved.Points[^1] != saved.Request.Goal))
            throw new InvalidDataException("Completed world path must end at the requested goal.");
        var expected = new List<FixedVector2>();
        if (saved.Phase != WorldPathPhase.Start && saved.Points.Count > 0) expected.Add(saved.Origin);
        if (saved.Search is not null && saved.Search.Path.Count > 1)
            for (var i = 0; i < saved.PointCursor; i++)
            {
                var center = navigation.CellCenter(saved.Search.Path[i]);
                if (expected.Count == 0 || expected[^1] != center) expected.Add(center);
            }
        if (saved.Status == PathStatus.Found && (expected.Count == 0 || expected[^1] != saved.Request.Goal))
            expected.Add(saved.Request.Goal);
        if (!saved.Points.SequenceEqual(expected))
            throw new InvalidDataException("World path points disagree with incremental conversion progress.");
        job._points.AddRange(saved.Points);
        return job;
    }
}
