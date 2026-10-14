using Rts.Kernel.Navigation;
using System.Security.Cryptography;
using System.Text;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly MatchEntities _entities = new();
    private readonly MatchCommandQueue _commands = new();
    private readonly MatchEventBuffer _events = new();
    private readonly UnitOrderBook _orders = new();
    private readonly GroupPlanningQueue _planning = new();
    private readonly MatchCommandExecutor _commandExecutor;
    private readonly CrowdStepper _crowd;
    private NavigationState? _navigation;
    private readonly DeterministicRng _rng;

    public RtsMatch(MatchConfig config, ulong seed, PathingGrid? pathingGrid = null, TerrainHeights? terrain = null,
        IReadOnlyList<MovementDefinition>? movementDefinitions = null)
    {
        config.Validate();
        if (terrain is not null && pathingGrid is null)
            throw new ArgumentException("A height field requires navigation.");
        _movementDefinitions = new MovementDefinitions(movementDefinitions);
        if (_movementDefinitions.ContentHash is not null && pathingGrid is null)
            throw new ArgumentException("Ground movement definitions require navigation.");
        Config = config;
        _rng = new DeterministicRng(seed);
        _navigation = pathingGrid is null ? null : new NavigationState(pathingGrid, terrain);
        _commandExecutor = new MatchCommandExecutor(_entities, _events, _orders, _planning, _movementDefinitions);
        _crowd = new CrowdStepper(_entities, _events, _orders, _movementDefinitions);
    }

    public MatchConfig Config { get; }

    public long Frame { get; private set; }

    /// <summary>Live read-only view in entity ID order. Enumerate only between Step calls; retained views observe later changes.</summary>
    public IReadOnlyCollection<EntityState> Entities => _entities.Live;

    /// <summary>Detached read-only entity list in ID order. Later steps do not change its contents.</summary>
    public IReadOnlyList<EntityState> ReadEntities() => _entities.ReadAll();

    public PathResult FindPath(GridCell start, GridCell goal, int clearanceCells = 0,
        int maxExpandedNodes = int.MaxValue) =>
        (_navigation ?? throw new InvalidOperationException("Match has no pathing grid."))
        .FindPath(start, goal, clearanceCells, maxExpandedNodes);

    public CommandAcceptance SubmitCommand(CommandEnvelope command) => _commands.Submit(command, Frame);

    public void Step()
    {
        Frame++;
        ExecuteCommandsForCurrentFrame();

        AdvanceGroupPlanning();
        ActivateQueuedMoves();
        AdvanceMovement(1.0 / Config.TickRate);
    }

    public bool TryGetEntity(EntityId entityId, out EntityState? state) =>
        _entities.TryGet(entityId, out state);

    public IReadOnlyList<MatchEvent> DrainEvents() => _events.Drain();

    public MatchSnapshot CaptureSnapshot()
    {
        return new MatchSnapshot(
            SnapshotJson.CurrentFormatVersion,
            Config.TickRate,
            Frame,
            _entities.NextId,
            _commands.NextArrivalOrder,
            _events.NextSequence,
            _rng.State,
            _entities.Capture(),
            _commands.Capture(),
            _navigation?.CaptureSnapshot(),
            ReadMoveOrders(),
            ReadUnitOrders(), _movementDefinitions.ContentHash, _planning.NextId,
            _planning.Capture());
    }

    public static RtsMatch Restore(MatchSnapshot snapshot, PathingGrid? pathingGrid = null, TerrainHeights? terrain = null,
        IReadOnlyList<MovementDefinition>? movementDefinitions = null)
    {
        SnapshotValidator.Validate(snapshot);

        if (snapshot.Navigation is not null && pathingGrid is null)
            throw new InvalidDataException("Static pathing grid required to restore navigation.");
        if (snapshot.Navigation is null && (pathingGrid is not null || terrain is not null))
            throw new InvalidDataException("Cannot add navigation while restoring a match without navigation.");

        var match = new RtsMatch(new MatchConfig(snapshot.TickRate), snapshot.RngState, pathingGrid, terrain, movementDefinitions)
        {
            Frame = snapshot.Frame,
        };

        if (snapshot.Navigation is not null)
            match._navigation = NavigationState.Restore(pathingGrid!, snapshot.Navigation, terrain);

        match._entities.Restore(snapshot.NextEntityId, snapshot.Entities);

        match._commands.Restore(snapshot.NextArrivalOrder, snapshot.PendingCommands);
        match._events.Restore(snapshot.NextEventSequence);
        match._orders.Restore(snapshot, match._entities.ById, match._navigation);
        match.ValidateRestoredGroups(snapshot);
        match._planning.Restore(snapshot, match._entities.ById, match._movementDefinitions, match._navigation);
        match.ValidateGroundBodies();
        return match;
    }

    public string ComputeStateHash()
    {
        var bytes = Encoding.UTF8.GetBytes(SnapshotJson.Serialize(CaptureSnapshot()));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private void ExecuteCommandsForCurrentFrame()
    {
        foreach (var command in _commands.TakeForFrame(Frame))
        {
            Execute(command);
        }
    }

    private void Execute(CommandEnvelope command)
    {
        if (command.Kind == CommandKind.GroupMove)
        {
            ExecuteGroupMove(command);
            return;
        }

        _commandExecutor.Execute(Frame, command, _navigation, ref _pathsDirty);
    }

    private void AddEvent(MatchEventKind kind, EntityId entityId, string detail) =>
        _events.Publish(Frame, kind, entityId, detail);
}
