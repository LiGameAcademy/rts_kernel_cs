namespace Rts.Kernel.Navigation;

public sealed record MoveRequest(SimVector2 Goal, double Speed, int ClearanceCells = 0, MotionParameters? Motion = null)
{
    internal bool IsValid => double.IsFinite(Goal.X) && double.IsFinite(Goal.Y)
        && double.IsFinite(Speed) && Speed > 0 && ClearanceCells >= 0 && (Motion is null || Motion.IsValid);
}

public sealed record MoveOrderSnapshot(ulong EntityId, MoveRequest Request,
    IReadOnlyList<SimVector2> Waypoints, int NextWaypoint);
internal sealed record MoveOrder(MoveRequest Request, IReadOnlyList<SimVector2> Waypoints, int NextWaypoint = 0);
