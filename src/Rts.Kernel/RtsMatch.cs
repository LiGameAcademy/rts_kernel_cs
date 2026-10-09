using Rts.Kernel.Navigation;
using System.Security.Cryptography;
using System.Text;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly SortedDictionary<EntityId, EntityState> _entities = [];
    private readonly MatchCommandQueue _commands = new();
    private readonly MatchEventBuffer _events = new();
    private NavigationState? _navigation;
    private readonly DeterministicRng _rng;
    private ulong _nextEntityId = 1;

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
    }

    public MatchConfig Config { get; }

    public long Frame { get; private set; }

    /// <summary>Live read-only view in entity ID order. Enumerate only between Step calls; retained views observe later changes.</summary>
    public IReadOnlyCollection<EntityState> Entities => _entities.Values;

    /// <summary>Detached read-only entity list in ID order. Later steps do not change its contents.</summary>
    public IReadOnlyList<EntityState> ReadEntities() => Array.AsReadOnly(_entities.Values.ToArray());

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
        _entities.TryGetValue(entityId, out state);

    public IReadOnlyList<MatchEvent> DrainEvents() => _events.Drain();

    public MatchSnapshot CaptureSnapshot()
    {
        var entities = _entities.Values
            .Select(entity => new EntitySnapshot(
                entity.Id.Value,
                entity.OwnerId,
                entity.Position.X,
                entity.Position.Y,
                entity.Velocity.X,
                entity.Velocity.Y,
                entity.Facing, entity.MovementDefinitionId))
            .ToArray();

        return new MatchSnapshot(
            SnapshotJson.CurrentFormatVersion,
            Config.TickRate,
            Frame,
            _nextEntityId,
            _commands.NextArrivalOrder,
            _events.NextSequence,
            _rng.State,
            entities,
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
            _nextEntityId = snapshot.NextEntityId,
        };

        if (snapshot.Navigation is not null)
            match._navigation = NavigationState.Restore(pathingGrid!, snapshot.Navigation, terrain);

        foreach (var entity in snapshot.Entities.OrderBy(item => item.Id))
        {
            var id = new EntityId(entity.Id);
            if (id.IsNone || !match._entities.TryAdd(id, new EntityState(
                    id,
                    entity.OwnerId,
                    new SimVector2(entity.PositionX, entity.PositionY),
                    new SimVector2(entity.VelocityX, entity.VelocityY), entity.Facing, entity.MovementDefinitionId)))
            {
                throw new InvalidDataException($"Invalid or duplicate entity id {entity.Id}.");
            }
        }

        match._commands.Restore(snapshot.NextArrivalOrder, snapshot.PendingCommands);
        match._events.Restore(snapshot.NextEventSequence);
        match._orders.Restore(snapshot, match._entities, match._navigation);
        match.ValidateRestoredGroups(snapshot);
        match._planning.Restore(snapshot, match._entities, match._movementDefinitions, match._navigation);
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
        switch (command.Kind)
        {
            case CommandKind.SetObstacle:
                if (_navigation is null || !_navigation.TrySetObstacle(command.Obstacle!.Id, command.Obstacle.Area!.Value))
                    AddEvent(MatchEventKind.CommandRejected, EntityId.None, "invalid_obstacle_or_navigation_disabled");
                else _pathsDirty = true;
                break;
            case CommandKind.RemoveObstacle:
                if (_navigation is null || !_navigation.RemoveObstacle(command.Obstacle!.Id))
                    AddEvent(MatchEventKind.CommandRejected, EntityId.None, "obstacle_missing_or_navigation_disabled");
                else _pathsDirty = true;
                break;
            case CommandKind.GroupMove:
                ExecuteGroupMove(command);
                break;
            case CommandKind.MoveTo:
                ExecuteMove(command);
                break;
            case CommandKind.SpawnEntity:
            {
                if (command.MovementDefinitionId != 0 && !_movementDefinitions.TryGet(command.MovementDefinitionId, out _))
                { AddEvent(MatchEventKind.CommandRejected, EntityId.None, "movement_definition_missing"); break; }
                if (!CanSpawnGround(command))
                { AddEvent(MatchEventKind.CommandRejected, EntityId.None, "spawn_ground_unavailable"); break; }
                var id = new EntityId(_nextEntityId++);
                _entities.Add(id, new EntityState(id, command.PlayerId, command.Position, SimVector2.Zero, MovementDefinitionId: command.MovementDefinitionId));
                AddEvent(MatchEventKind.EntitySpawned, id, string.Empty);
                break;
            }
            case CommandKind.SetVelocity:
                if (!_entities.TryGetValue(command.EntityId, out var entity) || entity.OwnerId != command.PlayerId)
                {
                    AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
                    break;
                }

                if (_navigation is not null)
                {
                    AddEvent(MatchEventKind.CommandRejected, command.EntityId, "use_move_to_with_navigation");
                    break;
                }
                _orders.Clear(entity.Id);
                _entities[command.EntityId] = entity with { Velocity = command.Velocity };
                break;
            case CommandKind.Stop:
                ExecuteStop(command);
                break;
            default:
                AddEvent(MatchEventKind.CommandRejected, command.EntityId, "unknown_command");
                break;
        }
    }

    private void AddEvent(MatchEventKind kind, EntityId entityId, string detail) =>
        _events.Publish(Frame, kind, entityId, detail);
}
