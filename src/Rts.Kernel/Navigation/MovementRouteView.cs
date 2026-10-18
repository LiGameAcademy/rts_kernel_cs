namespace Rts.Kernel.Navigation;

/// <summary>Detached current-route diagnostics. Pending orders have no frozen path yet.</summary>
public sealed record MovementRouteView(EntityId EntityId, FixedVector2? PreferredGoal,
    IReadOnlyList<FixedVector2> Waypoints, bool TemporaryYield, bool Settled, int WaitFrames);
