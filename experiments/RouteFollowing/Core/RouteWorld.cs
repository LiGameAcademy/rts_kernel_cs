using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rts.Movement.Prototype;

/// <summary>Owns experimental commands, walkers and shared routes. No engine or wall-clock dependency.</summary>
public sealed class RouteWorld
{
    internal readonly SortedDictionary<ulong, Walker> Bodies = [];
    internal readonly SortedDictionary<ulong, RouteTask> Routes = [];
    private readonly Queue<RouteCommand> _commands = [];
    private readonly List<RouteEvent> _events = [];
    internal NavigationState Navigation;
    internal byte[] Flags;
    internal int Revision;
    internal long PlacementRevision;
    internal ulong NextTask = 1;
    private ulong _nextUnit = 1;
    private long _eventSequence;
    public long Frame { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<Walker> Units => Bodies.Values.Select(Copy).ToArray();
    public IReadOnlyList<RouteTask> Paths => Routes.Values.Select(route => route with { Points = route.Points.ToArray(), Corners = route.Corners.ToArray(), Yield = CopyYield(route.Yield) }).ToArray();
    public byte[] ReadFlags() => Flags.ToArray();
    public FixedVector2 FollowTarget(ulong id) => Bodies[id].Task == 0 ? Bodies[id].Position
        : Bodies[id].Phase == WalkPhase.Yielding ? Bodies[id].Endpoint
        : RoutePlanning.Follow(this, Bodies[id], Routes[Bodies[id].Task]).Target;

    public RouteWorld(int width = 64, int height = 40, byte[]? flags = null)
    {
        if (width is < 8 or > 128 || height is < 8 or > 128) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        Flags = flags?.ToArray() ?? new byte[width * height];
        if (Flags.Length != width * height) throw new ArgumentException("Invalid map length.");
        Navigation = NewNavigation();
    }

    private NavigationState NewNavigation() => new(new PathingGrid(Width, Height, 32, default, Flags));

    public ulong Spawn(FixedVector2 position, bool stopped = false)
    {
        if (Bodies.Count >= 500 || !Navigation.CanStandAt(position, RouteSettings.Radius) || Bodies.Values.Any(body =>
            (body.Position - position).Length < RouteSettings.Radius * 2)) throw new ArgumentException("Invalid spawn.");
        var id = _nextUnit++;
        Bodies.Add(id, new(id, position, 0, stopped ? WalkPhase.Stopped : WalkPhase.Idle, 0, 0,
            position, position, default, 0, 90, 0, 0, 0, null, default, []));
        PlacementRevision++;
        return id;
    }

    public bool Move(IEnumerable<ulong> members, FixedVector2 goal, bool append = false)
    {
        var ids = members.Order().ToArray();
        if (ids.Length is < 1 or > 24 || ids.Distinct().Count() != ids.Length || ids.Any(id => !Bodies.ContainsKey(id))
            || !Navigation.TryWorldToCell(goal, out _) || _commands.Count >= 16
            || ids.Any(id => Bodies[id].Pending.Count >= 8)) return false;
        _commands.Enqueue(new(false, ids, goal, append));
        return true;
    }

    public bool Stop(IEnumerable<ulong> members)
    {
        var ids = members.Distinct().Order().ToArray();
        if (ids.Length == 0 || ids.Any(id => !Bodies.ContainsKey(id)) || _commands.Count >= 16) return false;
        _commands.Enqueue(new(true, ids, default, false));
        return true;
    }

    public bool SetWall(GridArea area, bool blocked)
    {
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1
            || area.X + area.Width > Width || area.Y + area.Height > Height) return false;
        var flags = Flags.ToArray();
        for (var y = area.Y; y < area.Y + area.Height; y++)
        for (var x = area.X; x < area.X + area.Width; x++) flags[y * Width + x] = blocked ? (byte)2 : (byte)0;
        var candidate = new NavigationState(new PathingGrid(Width, Height, 32, default, flags));
        if (Bodies.Values.Any(body => !candidate.CanStandAt(body.Position, RouteSettings.Radius))) return false;
        Flags = flags;
        Navigation = candidate;
        Revision++;
        PlacementRevision++;
        return true;
    }

    public void Step()
    {
        Frame++;
        _events.Clear();
        var fixedBeforeCommands = RoutePlacements.FixedBodies(this);
        // One new group search per tick; queued commands preserve the previous active order until processed.
        if (_commands.TryDequeue(out var command)) Apply(command);
        foreach (var body in Bodies.Values.ToArray())
            if (body.Task == 0 && body.Phase != WalkPhase.Stopped && body.Pending.Count > 0)
            {
                var next = body.Pending[0];
                Bodies[body.Id] = Activate(body with { Pending = body.Pending.Skip(1).ToArray() }, next);
            }
        RouteYielding.Reconcile(this);
        var fixedBeforeMotion = RoutePlacements.FixedBodies(this);
        if (!fixedBeforeCommands.SequenceEqual(fixedBeforeMotion)) PlacementRevision++;
        RoutePlacements.Refresh(this);
        RouteMotion.Advance(this);
        RouteYielding.Reconcile(this);
        foreach (var group in Bodies.Values.Where(body => body.Task != 0).GroupBy(body => body.Task).ToArray())
        {
            var allSettled = Routes[group.Key].Yield is null && group.All(body => body.Phase == WalkPhase.Settled);
            foreach (var body in group)
                if (body.Phase == WalkPhase.Settled && (allSettled || body.Pending.Count > 0))
                {
                    Bodies[body.Id] = body with { Task = 0, Phase = WalkPhase.Idle, Velocity = default, Blocker = null, TransitQuarters = 4,
                        Progress = 0, ProgressCursor = 0, ProgressVersion = 0, PlacementCheckedRevision = -1, Navigation = null };
                    Publish(body.Id, "completed");
                }
        }
        if (!fixedBeforeMotion.SequenceEqual(RoutePlacements.FixedBodies(this))) PlacementRevision++;
        var referenced = Bodies.Values.Select(body => body.Task)
            .Concat(Bodies.Values.SelectMany(body => body.Pending.Select(goal => goal.Task))).ToHashSet();
        foreach (var id in Routes.Keys.Where(id => !referenced.Contains(id)).ToArray()) Routes.Remove(id);
    }

    private void Apply(RouteCommand command)
    {
        if (command.Stop)
        {
            foreach (var id in command.Members)
            {
                var body = Bodies[id];
                Bodies[id] = body with { Task = 0, Phase = WalkPhase.Stopped, Pending = [], Velocity = default,
                    Blocker = null, WaitTicks = 0, Recoveries = 0, RecentGroup = 0, Connection = null, TransitQuarters = 4,
                    Progress = 0, ProgressCursor = 0, ProgressVersion = 0, PlacementCheckedRevision = -1, Navigation = null };
                Publish(id, "stopped");
            }
            return;
        }
        var members = command.Members.Select(id => Bodies[id]).ToArray();
        if (!RoutePlacements.TryAssign(this, members, command.Goal, out var anchor, out var goals))
        {
            foreach (var id in command.Members) Publish(id, "rejected", "no_complete_group_placement");
            return;
        }
        // A group's mathematical centre is not a member and need not be navigable.
        if (!RoutePlanning.TryRoute(this, RoutePlanning.Centre(members), anchor, [], out var points, out var corners))
        {
            points = new FixedVector2[] { anchor, anchor };
            corners = [];
        }
        var taskId = NextTask++;
        Routes.Add(taskId, new(taskId, anchor, points, 1, Revision, corners));
        foreach (var body in members)
        {
            // Offset remains intent metadata; free movement does not chase it in transit.
            var intent = new QueuedGoal(taskId, goals[body.Id], body.Position - RoutePlanning.Centre(members));
            Bodies[body.Id] = command.Append && body.Task != 0
                ? body with { Pending = body.Pending.Append(intent).ToArray() }
                : Activate(body with { Pending = command.Append ? body.Pending : [] }, intent);
            Publish(body.Id, "accepted");
        }
    }

    private static Walker Activate(Walker body, QueuedGoal intent) => RoutePlacements.Initialise(body with
    {
        Task = intent.Task, Offset = intent.Offset,
        Cursor = 0, Phase = WalkPhase.Following, WaitTicks = 0, RetryAtWait = 90, Recoveries = 0,
        Progress = 0, ProgressCursor = 0, ProgressVersion = 0, RouteVersion = 0, Blocker = null, Velocity = default,
        RecentGroup = body.Task != 0 ? body.Task : body.RecentGroup, Connection = null, TransitQuarters = 4, Navigation = null
    }, intent.Preference);

    internal void Publish(ulong id, string kind, string detail = "") =>
        _events.Add(new(Frame, _eventSequence++, id, kind, detail));
    public IReadOnlyList<RouteEvent> Events => _events.ToArray();
    private static RouteYieldPlan? CopyYield(RouteYieldPlan? plan) => plan is null ? null
        : plan with { Members = plan.Members.ToArray() };
    private static Walker Copy(Walker body) => body with { Pending = body.Pending.ToArray(),
        Connection = body.Connection is { } connection ? connection with { Points = connection.Points.ToArray() } : null,
        Navigation = body.Navigation is { } reference ? reference with
            { Points = reference.Points.ToArray(), Corners = reference.Corners.ToArray() } : null };
    public string Save() => JsonSerializer.Serialize(new RouteSnapshot(RouteSettings.SnapshotFormat, Width, Height, Flags.ToArray(), Frame,
        _nextUnit, NextTask, _eventSequence, Revision, Bodies.Values.Select(Copy).ToArray(),
        Routes.Values.Select(route => route with { Points = route.Points.ToArray(), Corners = route.Corners.ToArray(), Yield = CopyYield(route.Yield) }).ToArray(), _commands.ToArray(), PlacementRevision));
    public string Hash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Save())));

    public static RouteWorld Load(string json)
    {
        var state = JsonSerializer.Deserialize<RouteSnapshot>(json) ?? throw new InvalidDataException("Empty snapshot.");
        RouteSnapshotValidation.Validate(state);
        var world = new RouteWorld(state.Width, state.Height, state.Flags)
        {
            Frame = state.Frame, _nextUnit = state.NextUnit, NextTask = state.NextTask,
            _eventSequence = state.EventSequence, Revision = state.MapRevision, PlacementRevision = state.PlacementRevision
        };
        foreach (var route in state.Tasks) world.Routes.Add(route.Id, route with { Points = route.Points.ToArray(), Corners = route.Corners.ToArray(), Yield = CopyYield(route.Yield) });
        foreach (var body in state.Units) world.Bodies.Add(body.Id, Copy(body));
        foreach (var command in state.Commands) world._commands.Enqueue(command with { Members = command.Members.ToArray() });
        if (world.Bodies.Values.Any(body => !world.Navigation.CanStandAt(body.Position, RouteSettings.Radius)))
            throw new InvalidDataException("Snapshot body intersects terrain.");
        return world;
    }
}
