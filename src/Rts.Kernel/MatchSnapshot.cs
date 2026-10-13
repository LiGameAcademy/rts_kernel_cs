using Rts.Kernel.Navigation;
using System.Text.Json;

namespace Rts.Kernel;

public sealed record EntitySnapshot(
    ulong Id,
    int OwnerId,
    double PositionX,
    double PositionY,
    double VelocityX,
    double VelocityY,
    double Facing = 0,
    ulong MovementDefinitionId = 0);

public sealed record QueuedCommandSnapshot(long ArrivalOrder, CommandEnvelope Command);

public sealed record MatchSnapshot(
    int FormatVersion,
    int TickRate,
    long Frame,
    ulong NextEntityId,
    long NextArrivalOrder,
    long NextEventSequence,
    ulong RngState,
    IReadOnlyList<EntitySnapshot> Entities,
    IReadOnlyList<QueuedCommandSnapshot> PendingCommands,
    NavigationSnapshot? Navigation = null,
    IReadOnlyList<MoveOrderSnapshot>? Moves = null,
    IReadOnlyList<UnitOrderQueueSnapshot>? Orders = null,
    string? MovementHash = null, ulong NextGroupId = 1,
    IReadOnlyList<GroupPlanningSnapshot>? GroupPlans = null);

public readonly record struct SnapshotDifference(string Path, string Expected, string Actual);

public static class SnapshotValidator
{
    public static void Validate(MatchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.FormatVersion != SnapshotJson.CurrentFormatVersion)
        {
            throw new InvalidDataException($"Unsupported snapshot version: {snapshot.FormatVersion}.");
        }

        new MatchConfig(snapshot.TickRate).Validate();
        if (snapshot.Frame < 0 || snapshot.NextEntityId == 0
            || snapshot.NextArrivalOrder < 0 || snapshot.NextEventSequence < 0)
        {
            throw new InvalidDataException("Snapshot counters are outside their valid range.");
        }

        if (snapshot.Entities is null || snapshot.PendingCommands is null)
        {
            throw new InvalidDataException("Snapshot collections cannot be null.");
        }

        if (snapshot.Navigation is not null) NavigationState.ValidateSnapshot(snapshot.Navigation);

        var entityIds = new HashSet<ulong>();
        ulong maximumEntityId = 0;
        foreach (var entity in snapshot.Entities)
        {
            if (entity.Id == 0 || entity.OwnerId < 0 || !entityIds.Add(entity.Id)
                || !double.IsFinite(entity.PositionX) || !double.IsFinite(entity.PositionY)
                || !double.IsFinite(entity.VelocityX) || !double.IsFinite(entity.VelocityY) || !double.IsFinite(entity.Facing)
                || entity.Facing < -Math.PI || entity.Facing >= Math.PI)
            {
                throw new InvalidDataException($"Invalid entity snapshot: {entity.Id}.");
            }

            maximumEntityId = Math.Max(maximumEntityId, entity.Id);
        }

        if (snapshot.NextEntityId <= maximumEntityId)
        {
            throw new InvalidDataException("Next entity id must be greater than every existing entity id.");
        }

        MovementSnapshot.Validate(snapshot, entityIds);
        UnitOrdersSnapshot.Validate(snapshot, entityIds);

        var arrivalOrders = new HashSet<long>();
        foreach (var queued in snapshot.PendingCommands)
        {
            var command = queued.Command
                ?? throw new InvalidDataException("Pending command cannot be null.");
            if (queued.ArrivalOrder < 0 || queued.ArrivalOrder >= snapshot.NextArrivalOrder
                || !arrivalOrders.Add(queued.ArrivalOrder))
            {
                throw new InvalidDataException($"Invalid pending arrival order: {queued.ArrivalOrder}.");
            }

            if (command.ExecuteFrame <= snapshot.Frame || command.GetStructureError() is not null)
            {
                throw new InvalidDataException("Invalid pending command.");
            }
        }
        GroupSnapshot.Validate(snapshot);
        GroupPlanningSnapshotRules.Validate(snapshot);
    }
}

public static class SnapshotJson
{
    public const int CurrentFormatVersion = 9;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string Serialize(MatchSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static MatchSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<MatchSnapshot>(json, Options)
        ?? throw new InvalidDataException("Snapshot JSON did not contain a match snapshot.");
}
