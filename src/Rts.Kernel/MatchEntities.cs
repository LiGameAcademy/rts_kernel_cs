using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Rts.Kernel;

/// <summary>Owns entity identities, committed states and entity snapshot conversion for one match.</summary>
internal sealed class MatchEntities
{
    private readonly SortedDictionary<EntityId, EntityState> _states = [];
    private readonly ReadOnlyDictionary<EntityId, EntityState> _readOnlyStates;

    internal MatchEntities()
    {
        _readOnlyStates = new ReadOnlyDictionary<EntityId, EntityState>(_states);
    }

    internal ulong NextId { get; private set; } = 1;

    /// <summary>Live ID-ordered states. Enumerate only while no state is being committed.</summary>
    internal IReadOnlyCollection<EntityState> Live => _states.Values;

    /// <summary>Live lookup for restore validation; even a cast cannot modify the backing dictionary.</summary>
    internal IReadOnlyDictionary<EntityId, EntityState> ById => _readOnlyStates;

    internal IReadOnlyList<EntityState> ReadAll() => Array.AsReadOnly(_states.Values.ToArray());
    internal EntityState Get(EntityId id) => _states[id];
    internal bool TryGet(EntityId id, [NotNullWhen(true)] out EntityState? state) => _states.TryGetValue(id, out state);

    internal EntityState Spawn(int ownerId, SimVector2 position, ulong movementDefinitionId)
    {
        var id = new EntityId(NextId++);
        var entity = new EntityState(id, ownerId, position, SimVector2.Zero, MovementDefinitionId: movementDefinitionId);
        _states.Add(id, entity);
        return entity;
    }

    internal void Update(EntityState entity)
    {
        if (!_states.ContainsKey(entity.Id))
            throw new InvalidOperationException($"Cannot update missing entity {entity.Id}.");
        _states[entity.Id] = entity;
    }

    internal IReadOnlyList<EntitySnapshot> Capture() => _states.Values
        .Select(entity => new EntitySnapshot(entity.Id.Value, entity.OwnerId,
            entity.Position.X, entity.Position.Y, entity.Velocity.X, entity.Velocity.Y,
            entity.Facing, entity.MovementDefinitionId))
        .ToArray();

    /// <summary>Restore entity values and the ID counter after the complete snapshot has been validated.</summary>
    internal void Restore(ulong nextId, IReadOnlyList<EntitySnapshot> entities)
    {
        _states.Clear();
        foreach (var entity in entities.OrderBy(item => item.Id))
        {
            var id = new EntityId(entity.Id);
            if (id.IsNone || !_states.TryAdd(id, new EntityState(id, entity.OwnerId,
                    new SimVector2(entity.PositionX, entity.PositionY),
                    new SimVector2(entity.VelocityX, entity.VelocityY), entity.Facing, entity.MovementDefinitionId)))
            {
                throw new InvalidDataException($"Invalid or duplicate entity id {entity.Id}.");
            }
        }
        NextId = nextId;
    }
}
