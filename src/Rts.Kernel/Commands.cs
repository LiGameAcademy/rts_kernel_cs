using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public enum CommandKind
{
    SpawnEntity = 1,
    SetVelocity = 2,
    Stop = 3,
    SetObstacle = 4,
    RemoveObstacle = 5,
    MoveTo = 6,
}

public sealed record CommandEnvelope(
    long ExecuteFrame,
    int PlayerId,
    long Sequence,
    CommandKind Kind,
    EntityId EntityId,
    SimVector2 Position,
    SimVector2 Velocity,
    ObstacleCommand? Obstacle = null,
    MoveRequest? Move = null,
    OrderMode Mode = OrderMode.Replace,
    OrderSource Source = OrderSource.Player)
{
    /// <summary>Diagnostic navigation command; building ownership rules are not implemented yet.</summary>
    public static CommandEnvelope SetObstacle(long executeFrame, int playerId, long sequence,
        ulong obstacleId, GridArea area) => new(executeFrame, playerId, sequence, CommandKind.SetObstacle,
            EntityId.None, SimVector2.Zero, SimVector2.Zero, new ObstacleCommand(obstacleId, area));

    public static CommandEnvelope RemoveObstacle(long executeFrame, int playerId, long sequence,
        ulong obstacleId) => new(executeFrame, playerId, sequence, CommandKind.RemoveObstacle,
            EntityId.None, SimVector2.Zero, SimVector2.Zero, new ObstacleCommand(obstacleId));

    internal bool HasValidObstaclePayload => Kind switch
    {
        CommandKind.SetObstacle => Obstacle is { Id: > 0, Area: { } area } && area.IsValid && EntityId.IsNone
            && Position == SimVector2.Zero && Velocity == SimVector2.Zero,
        CommandKind.RemoveObstacle => Obstacle is { Id: > 0, Area: null } && EntityId.IsNone
            && Position == SimVector2.Zero && Velocity == SimVector2.Zero,
        _ => Obstacle is null,
    };

    public static CommandEnvelope MoveTo(long executeFrame, int playerId, long sequence,
        EntityId entityId, SimVector2 goal, double speed, int clearanceCells = 0, MotionParameters? motion = null,
        OrderMode mode = OrderMode.Replace, OrderSource source = OrderSource.Player) =>
        new(executeFrame, playerId, sequence, CommandKind.MoveTo, entityId, SimVector2.Zero, SimVector2.Zero,
            Move: new MoveRequest(goal, speed, clearanceCells, motion), Mode: mode, Source: source);

    internal bool HasValidMovePayload => Kind == CommandKind.MoveTo
        ? Move is { IsValid: true } && !EntityId.IsNone && Obstacle is null
            && Position == SimVector2.Zero && Velocity == SimVector2.Zero
        : Move is null;

    internal bool HasValidOrderMetadata => Enum.IsDefined(Mode) && Enum.IsDefined(Source) && (Kind switch
    {
        CommandKind.MoveTo => true,
        CommandKind.Stop => Mode == OrderMode.Replace,
        _ => Mode == OrderMode.Replace && Source == OrderSource.Player,
    });

    public static CommandEnvelope Spawn(
        long executeFrame,
        int playerId,
        long sequence,
        SimVector2 position) =>
        new(executeFrame, playerId, sequence, CommandKind.SpawnEntity, EntityId.None, position, SimVector2.Zero);

    public static CommandEnvelope SetVelocity(
        long executeFrame,
        int playerId,
        long sequence,
        EntityId entityId,
        SimVector2 velocity) =>
        new(executeFrame, playerId, sequence, CommandKind.SetVelocity, entityId, SimVector2.Zero, velocity);

    public static CommandEnvelope Stop(
        long executeFrame,
        int playerId,
        long sequence,
        EntityId entityId, OrderSource source = OrderSource.Player) =>
        new(executeFrame, playerId, sequence, CommandKind.Stop, entityId, SimVector2.Zero, SimVector2.Zero, Source: source);
}

public readonly record struct CommandAcceptance(bool Accepted, string Error)
{
    public static CommandAcceptance Accept() => new(true, string.Empty);

    public static CommandAcceptance Reject(string error) => new(false, error);
}

public enum MatchEventKind
{
    EntitySpawned = 1,
    CommandRejected = 2,
    MoveCompleted = 3,
    MoveFailed = 4,
}

public sealed record MatchEvent(
    long Frame,
    long Sequence,
    MatchEventKind Kind,
    EntityId EntityId,
    string Detail);

internal sealed record QueuedCommand(long ArrivalOrder, CommandEnvelope Command);
