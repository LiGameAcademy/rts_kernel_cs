namespace Rts.Movement.Prototype;

public enum WalkPhase { Idle, Following, Waiting, Settled, Stopped, Failed, Yielding, YieldHolding }
public sealed record QueuedGoal(ulong Task, FixedVector2 Preference, FixedVector2 Offset);
public sealed record RouteConnection(FixedVector2 Target, int Cursor, int Version, IReadOnlyList<FixedVector2> Points, int Next = 0, int TransitQuarters = 4);
public enum MemberReferenceKind { Direct, SharedSuffix, Independent }
public sealed record MemberReference(FixedVector2 Goal, int MapRevision, int Version,
    MemberReferenceKind Kind, IReadOnlyList<FixedVector2> Points, IReadOnlyList<int> Corners);
public sealed record RecoveryProbe(FixedVector2 Goal, int MapRevision, long StartedAt,
    IReadOnlyList<ulong> Candidates, int Next = 0, int PairWork = 0);
public sealed record PassageState(ulong Blocker, int Side);
public sealed record Walker(ulong Id, FixedVector2 Position, SimScalar Facing, WalkPhase Phase,
    ulong Task, int Cursor, FixedVector2 Preference, FixedVector2 Endpoint, FixedVector2 Offset,
    long WaitTicks, long RetryAtWait, int Recoveries, SimScalar Progress, int RouteVersion,
    ulong? Blocker, FixedVector2 Velocity, IReadOnlyList<QueuedGoal> Pending, ulong RecentGroup = 0, RouteConnection? Connection = null, int TransitQuarters = 4,
    int ProgressCursor = 0, int ProgressVersion = 0, long PlacementCheckedRevision = -1, MemberReference? Navigation = null, PassageState? Passage = null, RecoveryProbe? Recovery = null, IReadOnlyList<ulong>? BlockingContacts = null);
public sealed record RouteTask(ulong Id, FixedVector2 Anchor, IReadOnlyList<FixedVector2> Points,
    int Version, int MapRevision, IReadOnlyList<int> Corners, RouteYieldPlan? Yield = null);
public enum TemporaryYieldPhase { Outward, Passing }
public sealed record TemporaryYield(IReadOnlyList<FixedVector2> Targets,
    IReadOnlyList<FixedVector2> Origins, FixedVector2 RequesterStart, FixedVector2 RequesterTarget,
    TemporaryYieldPhase Phase = TemporaryYieldPhase.Outward, bool HasRequesterConnection = false);
public sealed record RouteYieldPlan(ulong Requester, IReadOnlyList<ulong> Members, int Next, int MapRevision, long StartedAt,
    TemporaryYield? Temporary = null);
public sealed record RouteCommand(bool Stop, IReadOnlyList<ulong> Members, FixedVector2 Goal, bool Append);
public sealed record RouteEvent(long Frame, long Sequence, ulong Id, string Kind, string Detail = "");
public sealed record RouteSnapshot(int Format, int Width, int Height, byte[] Flags, long Frame,
    ulong NextUnit, ulong NextTask, long EventSequence, int MapRevision, Walker[] Units,
    RouteTask[] Tasks, RouteCommand[] Commands, long PlacementRevision = 0);

/// <summary>Experimental defaults, explicitly separate from production gameplay and v14 snapshots.</summary>
public static class RouteSettings
{
    public const int SnapshotFormat = 10;
    // A500-body world needs21 small-group commands, with bounded room for subsequent control input.
    public const int CommandCapacity = 32;
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
