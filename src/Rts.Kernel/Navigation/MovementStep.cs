namespace Rts.Kernel.Navigation;

internal sealed record MovementStep(FixedVector2 Position, SimScalar Facing, int NextWaypoint,
    IReadOnlyList<FixedVector2> Trace, string? Failure = null);

/// <summary>One proposal uses one frame of time; callers decide whether to reserve and commit it.</summary>
internal static class MovementStepper
{
    internal static MovementStep Compute(EntityState entity, MoveOrder order, int tickRate, TerrainHeights? terrain)
        => ComputeTimed(entity, order, tickRate, terrain, diagnostic: false);

    internal static MovementStep ComputeDiagnostic(EntityState entity, MoveOrder order,
        int tickRate, TerrainHeights? terrain)
        => order.Request.Motion is null
            ? ComputeDiagnosticLinear(entity, order, tickRate)
            : ComputeTimed(entity, order, tickRate, terrain, diagnostic: true);

    private static MovementStep ComputeDiagnosticLinear(EntityState entity, MoveOrder order, int tickRate)
    {
        // The diagnostic linear API historically consumes distance and preserves facing.
        // Preserve its distance-consumption contract; timed production motion separately consumes elapsed time.
        var position = entity.Position;
        var remaining = order.Request.Speed / tickRate;
        var next = order.NextWaypoint;
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var distance = SquaredDistance.Between(position, target).Length;
            if (distance <= remaining)
            {
                position = target;
                remaining -= distance;
                next++;
                continue;
            }
            var proposed = WaypointProgress.Advance(order, next, position, remaining);
            if (proposed == position && remaining >= order.Request.Speed / tickRate)
                return new(position, entity.Facing, next, Array.Empty<FixedVector2>(), "movement_below_resolution");
            position = proposed;
            break;
        }
        return new(position, entity.Facing, next, Array.Empty<FixedVector2>());
    }

    private static MovementStep ComputeTimed(EntityState entity, MoveOrder order, int tickRate,
        TerrainHeights? terrain, bool diagnostic)
    {
        var position = entity.Position;
        var facing = entity.Facing;
        SimScalar remaining = 1;
        var next = order.NextWaypoint;
        List<FixedVector2>? trace = diagnostic ? null : new() { position };
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = SquaredDistance.Between(position, target).Length;
            if (distance == 0)
            {
                // Diagnostic motion snaps even a zero-length target; formal motion retains its position bits.
                if (diagnostic) position = target;
                next++;
                continue;
            }
            if (remaining <= 0) break;
            var desired = SimMath.Atan2(dy, dx);
            var motion = order.Request.Motion;
            SimScalar speed;
            try
            {
                var turn = motion is { ScaleTurnSpeed: true }
                    ? MotionRules.TurnSpeedScale(facing, desired, motion.FullSpeedDegrees,
                        motion.MinimumSpeedDegrees, motion.MinimumTurnScale) : 1;
                var slope = motion is { ScaleSlopeSpeed: true }
                    ? MotionRules.SlopeSpeedScale(terrain!, position, target, motion.MaximumSlopeDegrees,
                        motion.UphillScale, motion.DownhillScale) : 1;
                speed = order.Request.Speed * turn * slope;
                // Keep integer ticks exact: quantize speed * frame-fraction / tickRate once.
                // Quantizing 1/tickRate first would make speed30 at30Hz miss a one-unit goal.
                var available = speed * remaining / tickRate;
                var reached = distance <= available;
                var elapsed = reached && speed > 0 ? distance * tickRate / speed : remaining;
                facing = motion is null
                    ? MotionRules.Normalize(desired)
                    : MotionRules.TurnTowards(facing, desired, motion.TurnRate, elapsed / tickRate);
                if (speed == 0) break;
                if (reached)
                {
                    position = target;
                    remaining -= elapsed;
                    next++;
                }
                else
                {
                    var proposed = WaypointProgress.Advance(order, next, position, available);
                    if (proposed == position && remaining == SimScalar.One && facing == entity.Facing)
                        return new(position, facing, next, ReadTrace(trace), "movement_below_resolution");
                    position = proposed;
                    if (diagnostic) break;
                    remaining = 0;
                }
            }
            catch (ArgumentException)
            {
                return new(position, facing, next, ReadTrace(trace), "invalid_motion_terrain");
            }
            trace?.Add(position);
        }
        if (trace?.Count == 1) trace.Add(position);
        return new(position, facing, next, ReadTrace(trace));
    }
    private static IReadOnlyList<FixedVector2> ReadTrace(List<FixedVector2>? trace)
        => trace is null ? Array.Empty<FixedVector2>() : trace;
}
