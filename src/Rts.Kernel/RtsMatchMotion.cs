using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private void AdvanceMotion(EntityState entity, MoveOrder order, double seconds)
    {
        var motion = order.Request.Motion!;
        var position = entity.Position;
        var facing = entity.Facing;
        var remainingSeconds = seconds;
        var next = order.NextWaypoint;
        while (next < order.Waypoints.Count)
        {
            var target = order.Waypoints[next];
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!double.IsFinite(distance))
            {
                FailMotion(entity.Id, position, facing, "invalid_move_geometry");
                return;
            }
            if (distance == 0)
            {
                position = target;
                next++;
                continue;
            }
            if (remainingSeconds <= 0) break;
            var desiredFacing = Math.Atan2(dy, dx);
            var turnScale = motion.ScaleTurnSpeed
                ? MotionRules.TurnSpeedScale(facing, desiredFacing, motion.FullSpeedDegrees,
                    motion.MinimumSpeedDegrees, motion.MinimumTurnScale) : 1;
            double slopeScale;
            try
            {
                slopeScale = motion.ScaleSlopeSpeed
                    ? MotionRules.SlopeSpeedScale(_navigation!.Terrain!, position, target,
                        motion.MaximumSlopeDegrees, motion.UphillScale, motion.DownhillScale) : 1;
            }
            catch (ArgumentException)
            {
                FailMotion(entity.Id, position, facing, "invalid_motion_terrain");
                return;
            }
            var speed = order.Request.Speed * turnScale * slopeScale;
            if (speed == 0)
            {
                facing = MotionRules.TurnTowards(facing, desiredFacing, motion.TurnRate, remainingSeconds);
                break;
            }
            // Each segment consumes elapsed time, so corners never grant another full-frame turn budget.
            var segmentSeconds = distance / speed;
            var elapsed = Math.Min(remainingSeconds, segmentSeconds);
            facing = MotionRules.TurnTowards(facing, desiredFacing, motion.TurnRate, elapsed);
            if (segmentSeconds <= remainingSeconds)
            {
                position = target;
                remainingSeconds -= elapsed;
                next++;
            }
            else
            {
                var travelled = speed * elapsed;
                position = new SimVector2(position.X + dx / distance * travelled,
                    position.Y + dy / distance * travelled);
                break;
            }
        }
        _entities[entity.Id] = entity with { Position = position, Facing = facing,
            Velocity = new SimVector2((position.X - entity.Position.X) / seconds,
                (position.Y - entity.Position.Y) / seconds) };
        if (next == order.Waypoints.Count)
            FinishMove(entity.Id, position, MatchEventKind.MoveCompleted, string.Empty);
        else
            _moveOrders[entity.Id] = order with { NextWaypoint = next };
    }

    private void FailMotion(EntityId id, SimVector2 position, double facing, string detail)
    {
        _entities[id] = _entities[id] with { Facing = facing };
        FinishMove(id, position, MatchEventKind.MoveFailed, detail);
    }
}
