using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public enum OrderSource
{
    Player = 0,
    PlayerAi = 1,
    UnitAi = 2,
}
public enum OrderMode
{
    Replace = 0,
    Append = 1,
}
public enum UnitOrderKind
{
    Move = 1,
    Stop = 2,
}

public sealed record UnitOrderIntent(UnitOrderKind Kind, OrderSource Source, MoveRequest? Move = null, GroupSlot? Group = null)
{
    internal bool IsValid => Enum.IsDefined(Source) && (Kind switch
    {
        UnitOrderKind.Move => Move is { IsValid: true } && (Group is null || Group.IsValid),
        UnitOrderKind.Stop => Move is null && Group is null,
        _ => false,
    });
}

public sealed record UnitOrderQueueSnapshot(ulong EntityId, UnitOrderIntent? Current,
    IReadOnlyList<UnitOrderIntent> Pending);

internal sealed class UnitOrderQueue(UnitOrderIntent? current)
{
    internal UnitOrderIntent? Current { get; set; } = current;
    internal Queue<UnitOrderIntent> Pending { get; } = new();
}
