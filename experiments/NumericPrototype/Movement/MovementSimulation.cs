using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixPointCS;

namespace NumericPrototype.Movement;

internal enum MotionStatus { Moving, Arrived, Failed }
internal readonly record struct MotionState(long Frame, FixedPoint Position, long Facing, int Waypoint,
    int BlockedFrames, MotionStatus Status, long NextEvent);
internal sealed record MotionSnapshot(int Format, string ContentIdentity, MotionState State, FixedPoint[] Path);
internal sealed record MotionEvent(long Frame, long Sequence, string Detail);

internal sealed class MovementSimulation
{
    private readonly MovementWorld _world;
    private readonly long _speed;
    private readonly long _radius;
    private readonly long _turnRate;
    private readonly FixedPoint? _blocker;
    private readonly long _blockerRadius;
    private FixedPoint[] _path;
    internal MotionState State { get; private set; }
    internal string ContentIdentity { get; }
    internal const int TickRate = 30;

    internal MovementSimulation(MovementWorld world, FixedPoint start, FixedPoint goal, long speed,
        long radius, long turnRate, FixedPoint? blocker = null, long blockerRadius = 0)
    {
        if (speed <= 0 || speed > 1000L * FixedMath.One) throw new ArgumentOutOfRangeException(nameof(speed));
        if (radius <= 0 || radius > 4 * world.CellSize) throw new ArgumentOutOfRangeException(nameof(radius));
        if (turnRate < FixedMath.One / 20 || turnRate > FixedMath.One) throw new ArgumentOutOfRangeException(nameof(turnRate));
        if (blockerRadius < 0 || blockerRadius > 4 * world.CellSize) throw new ArgumentOutOfRangeException(nameof(blockerRadius));
        _world = world; _speed = speed; _radius = radius; _turnRate = turnRate;
        _blocker = blocker; _blockerRadius = blockerRadius;
        if (!CollisionOracle.Clear(world, start, start, radius, blocker, blockerRadius)
            || !CollisionOracle.Clear(world, goal, goal, radius))
            throw new ArgumentException("Movement endpoints do not fit the static terrain/body rules.");
        _path = IntegerPathfinder.Find(world, start, goal, radius);
        if (_path.Length == 0) throw new ArgumentException("No integer path exists.");
        State = new(0, start, 0, 0, 0, MotionStatus.Moving, 0);
        ContentIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { Contract = "numeric-movement-v1/FixPointCS-a852f05/integer-octile/exact-collision-oracle",
                world.Identity, start, goal, speed, radius, turnRate, blocker, blockerRadius, TickRate })))).ToLowerInvariant();
    }

    internal MotionEvent[] Step()
    {
        var before = State;
        State = State with { Frame = checked(State.Frame + 1) };
        if (before.Status != MotionStatus.Moving) return [];
        var position = before.Position;
        var facing = before.Facing;
        var next = before.Waypoint;
        var remainingFrame = FixedMath.One;
        var blocked = false;
        while (next < _path.Length && remainingFrame > 0)
        {
            var target = _path[next];
            var delta = target - position;
            var distance = delta.Length;
            if ((Int128)distance * distance < delta.LengthSquared) distance = checked(distance + 1);
            if (distance == 0) { next++; continue; }
            var desired = Fixed64.Atan2(delta.Y, delta.X);
            var turn = FixedMath.TurnScale(facing, desired);
            var slope = FixedMath.SlopeScale(_world.Sample(target) - _world.Sample(position), distance);
            // One integer division by TickRate, rather than an approximate fixed-point 1/TickRate constant.
            var perFrame = FixedMath.Mul(FixedMath.Mul(_speed, turn), slope) / TickRate;
            if (perFrame <= 0) throw new InvalidOperationException("Movement quantized to zero in the prototype domain.");
            var available = FixedMath.Mul(perFrame, remainingFrame);
            var arrived = delta.LengthSquared <= (Int128)available * available;
            // Decide arrival from exact squared raw distance. Consume enough frame fraction, never a rounded-down duration.
            var elapsed = arrived ? checked((long)(((Int128)distance * FixedMath.One + perFrame - 1) / perFrame)) : remainingFrame;
            var maximumTurn = FixedMath.Mul(FixedMath.Mul(_turnRate, Fixed64.Pi2), elapsed) / TickRate;
            var nextFacing = FixedMath.Normalize(facing + Math.Clamp(FixedMath.Normalize(desired - facing), -maximumTurn, maximumTurn));
            var travelled = FixedMath.Mul(perFrame, elapsed);
            var proposal = arrived ? target : position + delta.ScaleRatio(travelled, distance);
            if (!CollisionOracle.Clear(_world, position, proposal, _radius, _blocker, _blockerRadius))
            {
                blocked = true;
                break;
            }
            position = proposal;
            facing = nextFacing;
            remainingFrame -= elapsed;
            if (arrived) next++;
            else break;
        }
        var waiting = blocked ? before.BlockedFrames + 1 : 0;
        var status = next == _path.Length ? MotionStatus.Arrived : waiting >= 3 * TickRate ? MotionStatus.Failed : MotionStatus.Moving;
        State = State with { Position = position, Facing = facing, Waypoint = next, BlockedFrames = waiting, Status = status };
        if (status == MotionStatus.Moving) return [];
        State = State with { NextEvent = checked(before.NextEvent + 1) };
        return [new(State.Frame, before.NextEvent, status == MotionStatus.Arrived ? "arrived" : "blocked_timeout")];
    }

    internal string Capture() => JsonSerializer.Serialize(new MotionSnapshot(1, ContentIdentity, State, _path.ToArray()));
    internal void Restore(string json)
    {
        var saved = JsonSerializer.Deserialize<MotionSnapshot>(json) ?? throw new InvalidDataException("Missing motion snapshot.");
        var state = saved.State;
        if (saved.Format != 1 || saved.ContentIdentity != ContentIdentity || saved.Path is null || saved.Path.Length == 0
            || state.Frame < 0 || state.NextEvent < 0 || !Enum.IsDefined(state.Status)
            || state.NextEvent != (state.Status == MotionStatus.Moving ? 0 : 1)
            || state.BlockedFrames > state.Frame || (state.Status == MotionStatus.Failed) != (state.BlockedFrames == 90)
            || !saved.Path.SequenceEqual(_path)
            || state.Waypoint < 0 || state.Waypoint > saved.Path.Length || state.BlockedFrames < 0 || state.BlockedFrames > 90
            || (state.Status == MotionStatus.Arrived) != (state.Waypoint == saved.Path.Length)
            || state.Status == MotionStatus.Moving && state.BlockedFrames >= 90
            || state.Facing != FixedMath.Normalize(state.Facing)
            || !CollisionOracle.Clear(_world, state.Position, state.Position, _radius, _blocker, _blockerRadius)
            || saved.Path.Any(point => !CollisionOracle.Clear(_world, point, point, _radius)))
            throw new InvalidDataException("Motion snapshot does not match content or state invariants.");
        _path = saved.Path.ToArray();
        State = state;
    }
}
