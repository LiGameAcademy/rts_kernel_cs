namespace Rts.Movement.Prototype;

public enum WalkPhase { Idle, Following, Waiting, Settled, Stopped, Failed }
public sealed record QueuedGoal(ulong Task, FixedVector2 Preference, FixedVector2 Offset);
public sealed record RouteConnection(FixedVector2 Target, int Cursor, int Version, IReadOnlyList<FixedVector2> Points, int Next = 0, int TransitQuarters = 4);
public sealed record Walker(ulong Id, FixedVector2 Position, SimScalar Facing, WalkPhase Phase,
    ulong Task, int Cursor, FixedVector2 Preference, FixedVector2 Endpoint, FixedVector2 Offset,
    long WaitTicks, long RetryAtWait, int Recoveries, SimScalar Progress, int RouteVersion,
    ulong? Blocker, FixedVector2 Velocity, IReadOnlyList<QueuedGoal> Pending, ulong RecentGroup = 0, RouteConnection? Connection = null, int TransitQuarters = 4,
    int ProgressCursor = 0, int ProgressVersion = 0);
public sealed record RouteTask(ulong Id, FixedVector2 Anchor, IReadOnlyList<FixedVector2> Points,
    int Version, int MapRevision, IReadOnlyList<int> Corners);
public sealed record RouteCommand(bool Stop, IReadOnlyList<ulong> Members, FixedVector2 Goal, bool Append);
public sealed record RouteEvent(long Frame, long Sequence, ulong Id, string Kind, string Detail = "");
public sealed record RouteSnapshot(int Format, int Width, int Height, byte[] Flags, long Frame,
    ulong NextUnit, ulong NextTask, long EventSequence, int MapRevision, Walker[] Units,
    RouteTask[] Tasks, RouteCommand[] Commands);

/// <summary>Experimental defaults, explicitly separate from production gameplay and v14 snapshots.</summary>
public static class RouteSettings
{
    public const int SnapshotFormat = 4;
    public const int TickRate = 30;
    public const int WaitBeforeRetry = 90;
    public const int RecoveryLimit = 3;
    public const int SearchNodes = 4096;
    public const int ShortConnectionNodes = 256;
    public const int ShortConnectionCells = 8;
    public static readonly SimScalar Radius = 31;
    public static readonly SimScalar Speed = 270;
    public static readonly SimScalar ProgressThreshold = 32;
    public static readonly MotionParameters Motion = new(TurnRate: SimScalar.Parse("0.6"), ScaleSlopeSpeed: false);
}
