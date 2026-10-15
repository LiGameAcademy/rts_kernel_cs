namespace Rts.Kernel;

public sealed record EntityState(
    EntityId Id,
    int OwnerId,
    FixedVector2 Position,
    FixedVector2 Velocity,
    SimScalar Facing = default,
    ulong MovementDefinitionId = 0);

public sealed record MatchConfig(int TickRate)
{
    public static MatchConfig Default { get; } = new(30);

    public void Validate()
    {
        if (TickRate is < 1 or > 240)
        {
            throw new ArgumentOutOfRangeException(nameof(TickRate), "Tick rate must be between 1 and 240.");
        }
    }
}
