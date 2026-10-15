namespace Rts.Kernel.Navigation;

public sealed record MoveRequest(FixedVector2 Goal, SimScalar Speed, int ClearanceCells = 0, MotionParameters? Motion = null)
{
    internal bool IsValid => SimulationDomain.Contains(Goal.X) && SimulationDomain.Contains(Goal.Y)
        && SimulationDomain.IsCapability(Speed) && ClearanceCells >= 0 && (Motion is null || Motion.IsValid);
}

public sealed record MoveOrderSnapshot(ulong EntityId, MoveRequest Request,
    IReadOnlyList<FixedVector2> Waypoints, int NextWaypoint, int WaitFrames = 0, long RetryAfterFrame = 0);
/// <summary>Detached per-unit movement diagnostics without copying path waypoints.</summary>
public readonly record struct MovementStatusView(ulong EntityId, int WaitFrames, long RetryAfterFrame);

internal sealed record MoveOrder(MoveRequest Request, IReadOnlyList<FixedVector2> Waypoints, int NextWaypoint = 0, int WaitFrames = 0, long RetryAfterFrame = 0);
