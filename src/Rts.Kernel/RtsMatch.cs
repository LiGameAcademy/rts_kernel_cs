using Rts.Kernel.Navigation;
using System.Security.Cryptography;
using System.Text;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private readonly SortedDictionary<EntityId, EntityState> _entities = [];
    private readonly List<QueuedCommand> _pendingCommands = [];
    private readonly List<MatchEvent> _events = [];
    private NavigationState? _navigation;
    private ulong _nextEntityId = 1;
    private long _nextArrivalOrder;
    private long _nextEventSequence;

    public RtsMatch(MatchConfig config, ulong seed, PathingGrid? pathingGrid = null, TerrainHeights? terrain = null)
    {
        config.Validate();
        if (terrain is not null && pathingGrid is null)
            throw new ArgumentException("A height field requires navigation.");
        Config = config;
        Rng = new DeterministicRng(seed);
        _navigation = pathingGrid is null ? null : new NavigationState(pathingGrid, terrain);
    }

    public MatchConfig Config { get; }

    public long Frame { get; private set; }

    public DeterministicRng Rng { get; }

    public IReadOnlyCollection<EntityState> Entities => _entities.Values;

    public PathResult FindPath(GridCell start, GridCell goal, int clearanceCells = 0,
        int maxExpandedNodes = int.MaxValue) =>
        (_navigation ?? throw new InvalidOperationException("Match has no pathing grid."))
        .FindPath(start, goal, clearanceCells, maxExpandedNodes);

    public CommandAcceptance SubmitCommand(CommandEnvelope command)
    {
        if (command.ExecuteFrame <= Frame)
        {
            return CommandAcceptance.Reject("execute_frame_must_be_in_the_future");
        }

        if (command.PlayerId < 0 || command.Sequence < 0)
        {
            return CommandAcceptance.Reject("invalid_command_identity");
        }

        if (!command.HasValidObstaclePayload)
            return CommandAcceptance.Reject("invalid_obstacle_payload");

        if (!command.HasValidMovePayload) return CommandAcceptance.Reject("invalid_move_payload");

        _pendingCommands.Add(new QueuedCommand(_nextArrivalOrder++, command));
        return CommandAcceptance.Accept();
    }

    public void Step()
    {
        Frame++;
        ExecuteCommandsForCurrentFrame();

        AdvanceMovement(1.0 / Config.TickRate);
    }

    public bool TryGetEntity(EntityId entityId, out EntityState? state) =>
        _entities.TryGetValue(entityId, out state);

    public IReadOnlyList<MatchEvent> DrainEvents()
    {
        var drained = _events.ToArray();
        _events.Clear();
        return drained;
    }

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
                entity.Facing))
            .ToArray();

        var pending = _pendingCommands
            .OrderBy(item => item.Command.ExecuteFrame)
            .ThenBy(item => item.Command.PlayerId)
            .ThenBy(item => item.Command.Sequence)
            .ThenBy(item => item.ArrivalOrder)
            .Select(item => new QueuedCommandSnapshot(item.ArrivalOrder, item.Command))
            .ToArray();

        return new MatchSnapshot(
            SnapshotJson.CurrentFormatVersion,
            Config.TickRate,
            Frame,
            _nextEntityId,
            _nextArrivalOrder,
            _nextEventSequence,
            Rng.State,
            entities,
            pending,
            _navigation?.CaptureSnapshot(),
            ReadMoveOrders());
    }

    public static RtsMatch Restore(MatchSnapshot snapshot, PathingGrid? pathingGrid = null, TerrainHeights? terrain = null)
    {
        SnapshotValidator.Validate(snapshot);

        if (snapshot.Navigation is not null && pathingGrid is null)
            throw new InvalidDataException("Static pathing grid required to restore navigation.");
        if (snapshot.Navigation is null && (pathingGrid is not null || terrain is not null))
            throw new InvalidDataException("Cannot add navigation while restoring a match without navigation.");

        var match = new RtsMatch(new MatchConfig(snapshot.TickRate), snapshot.RngState)
        {
            Frame = snapshot.Frame,
            _nextEntityId = snapshot.NextEntityId,
            _nextArrivalOrder = snapshot.NextArrivalOrder,
            _nextEventSequence = snapshot.NextEventSequence,
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
                    new SimVector2(entity.VelocityX, entity.VelocityY), entity.Facing)))
            {
                throw new InvalidDataException($"Invalid or duplicate entity id {entity.Id}.");
            }
        }

        match._pendingCommands.AddRange(snapshot.PendingCommands.Select(
            item => new QueuedCommand(item.ArrivalOrder, item.Command)));
        match.RestoreMoveOrders(snapshot);
        return match;
    }

    public string ComputeStateHash()
    {
        var bytes = Encoding.UTF8.GetBytes(SnapshotJson.Serialize(CaptureSnapshot()));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private void ExecuteCommandsForCurrentFrame()
    {
        var due = _pendingCommands
            .Where(item => item.Command.ExecuteFrame == Frame)
            .OrderBy(item => item.Command.PlayerId)
            .ThenBy(item => item.Command.Sequence)
            .ThenBy(item => item.ArrivalOrder)
            .ToArray();

        _pendingCommands.RemoveAll(item => item.Command.ExecuteFrame <= Frame);
        foreach (var queued in due)
        {
            Execute(queued.Command);
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
            case CommandKind.MoveTo:
                ExecuteMove(command);
                break;
            case CommandKind.SpawnEntity:
            {
                var id = new EntityId(_nextEntityId++);
                _entities.Add(id, new EntityState(id, command.PlayerId, command.Position, SimVector2.Zero));
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
                _entities[command.EntityId] = entity with { Velocity = command.Velocity };
                break;
            case CommandKind.Stop:
                if (_entities.TryGetValue(command.EntityId, out var stopped) && stopped.OwnerId == command.PlayerId)
                {
                    _moveOrders.Remove(command.EntityId);
                    _entities[command.EntityId] = stopped with { Velocity = SimVector2.Zero };
                }
                else
                {
                    AddEvent(MatchEventKind.CommandRejected, command.EntityId, "entity_missing_or_not_owned");
                }

                break;
            default:
                AddEvent(MatchEventKind.CommandRejected, command.EntityId, "unknown_command");
                break;
        }
    }

    private void AddEvent(MatchEventKind kind, EntityId entityId, string detail) =>
        _events.Add(new MatchEvent(Frame, _nextEventSequence++, kind, entityId, detail));
}
