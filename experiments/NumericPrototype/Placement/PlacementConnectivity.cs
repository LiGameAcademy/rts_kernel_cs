using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal sealed record ConnectivityData(int[] Labels, long RequiredWork);

/// <summary>Immutable-world derived cache; warm data cannot bypass the cold build's work reservation.</summary>
internal sealed class PlacementConnectivity(MovementWorld world)
{
    private readonly Dictionary<int, ConnectivityData> _cache = [];
    internal ConnectivityWork Start(long radius)
    {
        if (radius <= 0 || radius > 4 * world.CellSize) throw new ArgumentOutOfRangeException(nameof(radius));
        var clearance = checked((int)((Math.Max(0, 2 * radius - world.CellSize) + 2 * world.CellSize - 1)
            / (2 * world.CellSize)));
        _cache.TryGetValue(clearance, out var data);
        return new(world, clearance, data);
    }

    internal void Remember(ConnectivityWork work)
    {
        if (!work.Complete) throw new InvalidOperationException("Connectivity is pending.");
        _cache.TryAdd(work.Clearance, work.Data);
    }
}

/// <summary>Mask, component scan and flood fill. Each step has a bounded, explicit integer weight.</summary>
internal sealed class ConnectivityWork
{
    private readonly MovementWorld _world;
    private readonly int[] _labels;
    private readonly ConnectivityData? _cached;
    private readonly Queue<int> _pending = [];
    private int _phase;
    private int _cursor;
    private int _component;
    private long _supplied;
    private long _spent;
    internal int Clearance { get; }
    internal bool Complete => _cached is null ? _phase == 3 : _supplied >= _cached.RequiredWork;
    internal ConnectivityData Data => Complete ? _cached ?? new(_labels, _spent)
        : throw new InvalidOperationException("Connectivity is pending.");

    internal ConnectivityWork(MovementWorld world, int clearance, ConnectivityData? cached)
    {
        _world = world;
        Clearance = clearance;
        _cached = cached;
        _labels = cached?.Labels ?? new int[checked(world.Width * world.Height)];
    }

    internal void Advance(int budget)
    {
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (Complete) return;
        _supplied = checked(_supplied + budget);
        if (_cached is not null) return;
        while (!Complete)
        {
            var charge = _phase == 0 ? (2 * Clearance + 1) * (2 * Clearance + 1) : _phase == 2 ? 9 : 1;
            if (_supplied - _spent < charge) break;
            if (_phase == 0) Mask();
            else if (_phase == 1) Seed();
            else Flood();
            _spent += charge;
        }
    }

    private void Mask()
    {
        var x = _cursor % _world.Width;
        var y = _cursor / _world.Width;
        var valid = true;
        for (var cy = y - Clearance; cy <= y + Clearance && valid; cy++)
        for (var cx = x - Clearance; cx <= x + Clearance; cx++)
            if (!_world.Walkable(cx, cy)) { valid = false; break; }
        if (!valid) _labels[_cursor] = -1;
        if (++_cursor != _labels.Length) return;
        _cursor = 0;
        _phase = 1;
    }

    private void Seed()
    {
        if (_labels[_cursor] == 0)
        {
            _labels[_cursor] = ++_component;
            _pending.Enqueue(_cursor);
            _phase = 2;
        }
        _cursor++;
        if (_phase == 1 && _cursor == _labels.Length) _phase = 3;
    }

    private void Flood()
    {
        var cell = _pending.Dequeue();
        var x = cell % _world.Width;
        var y = cell / _world.Width;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if (dx == 0 && dy == 0 || nx < 0 || ny < 0 || nx >= _world.Width || ny >= _world.Height
                || _labels[ny * _world.Width + nx] != 0 || dx != 0 && dy != 0
                && (_labels[y * _world.Width + nx] < 0 || _labels[ny * _world.Width + x] < 0)) continue;
            _labels[ny * _world.Width + nx] = _component;
            _pending.Enqueue(ny * _world.Width + nx);
        }
        if (_pending.Count == 0) _phase = _cursor == _labels.Length ? 3 : 1;
    }

    internal bool Connected(FixedPoint from, FixedPoint to)
    {
        if (!Complete) throw new InvalidOperationException("Connectivity is pending.");
        var a = _world.Cell(from);
        var b = _world.Cell(to);
        if (a.X < 0 || a.Y < 0 || b.X < 0 || b.Y < 0 || a.X >= _world.Width || b.X >= _world.Width
            || a.Y >= _world.Height || b.Y >= _world.Height) return false;
        var component = _labels[a.Y * _world.Width + a.X];
        return component > 0 && component == _labels[b.Y * _world.Width + b.X];
    }
}
