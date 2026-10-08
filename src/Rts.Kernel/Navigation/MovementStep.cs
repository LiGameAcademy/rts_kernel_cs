namespace Rts.Kernel.Navigation;

internal sealed record MovementStep(SimVector2 Position, double Facing, int NextWaypoint,
    IReadOnlyList<SimVector2> Trace, string? Failure = null);

/// <summary>One proposal uses one frame of time; callers decide whether to reserve and commit it.</summary>
internal static class MovementStepper
{
    internal static MovementStep Compute(EntityState entity, MoveOrder order, double seconds, TerrainHeights? terrain)
    {
        var position = entity.Position;
        var facing = entity.Facing;
        var remaining = seconds;
        var next = order.NextWaypoint;
        var trace = new List<SimVector2> { position };
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(distance)) return new(position, facing, next, trace, "invalid_move_geometry");
            if (distance == 0) { next++; continue; }
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
                facing = motion is null ? MotionRules.Normalize(desired) : MotionRules.TurnTowards(facing, desired, motion.TurnRate, elapsed);
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
                    remaining = 0;
                }
            }
            catch (ArgumentException) { return new(position, facing, next, trace, "invalid_motion_terrain"); }
            trace.Add(position);
        }
        if (trace.Count == 1) trace.Add(position);
        return new(position, facing, next, trace);
    }
}
