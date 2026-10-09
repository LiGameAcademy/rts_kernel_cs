namespace Rts.Kernel.Navigation;

internal sealed record MovementStep(SimVector2 Position, double Facing, int NextWaypoint,
    IReadOnlyList<SimVector2> Trace, string? Failure = null);

/// <summary>One proposal uses one frame of time; callers decide whether to reserve and commit it.</summary>
internal static class MovementStepper
{
    internal static MovementStep Compute(EntityState entity, MoveOrder order, double seconds, TerrainHeights? terrain)
        => ComputeTimed(entity, order, seconds, terrain, diagnostic: false);

    internal static MovementStep ComputeDiagnostic(EntityState entity, MoveOrder order,
        double seconds, TerrainHeights? terrain)
        => order.Request.Motion is null
            ? ComputeDiagnosticLinear(entity, order, seconds)
            : ComputeTimed(entity, order, seconds, terrain, diagnostic: true);

    private static MovementStep ComputeDiagnosticLinear(EntityState entity, MoveOrder order, double seconds)
    {
        // The diagnostic linear API historically consumes distance and preserves facing.
        // Keep its arithmetic: switching to time consumption would change floating-point snapshots.
        var position = entity.Position;
        var remaining = order.Request.Speed * seconds;
        var next = order.NextWaypoint;
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(distance))
                return new(position, entity.Facing, next, Array.Empty<SimVector2>(), "invalid_move_geometry");
            if (distance <= remaining)
            {
                position = target;
                remaining -= distance;
                next++;
                continue;
            }
            if (remaining > 0)
                position = new(position.X + dx / distance * remaining, position.Y + dy / distance * remaining);
            break;
        }
        return new(position, entity.Facing, next, Array.Empty<SimVector2>());
    }

    private static MovementStep ComputeTimed(EntityState entity, MoveOrder order, double seconds,
        TerrainHeights? terrain, bool diagnostic)
    {
        var position = entity.Position;
        var facing = entity.Facing;
        var remaining = seconds;
        var next = order.NextWaypoint;
        List<SimVector2>? trace = diagnostic ? null : new() { position };
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(distance))
                return new(position, facing, next, ReadTrace(trace), "invalid_move_geometry");
            if (distance == 0)
            {
                // Diagnostic motion snaps even a zero-length target; formal motion retains its position bits.
                if (diagnostic) position = target;
                next++;
                continue;
            }
            if (remaining <= 0) break;
            var desired = Math.Atan2(dy, dx);
            var motion = order.Request.Motion;
            double speed;
            try
            {
                var turn = motion is { ScaleTurnSpeed: true }
                    ? MotionRules.TurnSpeedScale(facing, desired, motion.FullSpeedDegrees,
                        motion.MinimumSpeedDegrees, motion.MinimumTurnScale) : 1;
                var slope = motion is { ScaleSlopeSpeed: true }
                    ? MotionRules.SlopeSpeedScale(terrain!, position, target, motion.MaximumSlopeDegrees,
                        motion.UphillScale, motion.DownhillScale) : 1;
                speed = order.Request.Speed * turn * slope;
                var elapsed = speed == 0 ? remaining : Math.Min(remaining, distance / speed);
                facing = motion is null
                    ? MotionRules.Normalize(desired)
                    : MotionRules.TurnTowards(facing, desired, motion.TurnRate, elapsed);
                if (speed == 0) break;
                if (distance / speed <= remaining)
                {
                    position = target;
                    remaining -= elapsed;
                    next++;
                }
                else
                {
                    var travelled = speed * elapsed;
                    position = new(position.X + dx / distance * travelled, position.Y + dy / distance * travelled);
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
    private static IReadOnlyList<SimVector2> ReadTrace(List<SimVector2>? trace)
        => trace is null ? Array.Empty<SimVector2>() : trace;
}
