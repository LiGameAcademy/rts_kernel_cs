using System.Globalization;
using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public static class SnapshotDiff
{
    public static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        SnapshotDifference? difference;
        if ((difference = Compare("formatVersion", expected.FormatVersion, actual.FormatVersion)) is not null
            || (difference = Compare("tickRate", expected.TickRate, actual.TickRate)) is not null
            || (difference = Compare("frame", expected.Frame, actual.Frame)) is not null
            || (difference = Compare("nextEntityId", expected.NextEntityId, actual.NextEntityId)) is not null
            || (difference = Compare("nextArrivalOrder", expected.NextArrivalOrder, actual.NextArrivalOrder)) is not null
            || (difference = Compare("nextEventSequence", expected.NextEventSequence, actual.NextEventSequence)) is not null
            || (difference = Compare("rngState", expected.RngState, actual.RngState)) is not null
            || (difference = Compare("entities.count", expected.Entities.Count, actual.Entities.Count)) is not null)
        {
            return difference;
        }

        for (var index = 0; index < expected.Entities.Count; index++)
        {
            var left = expected.Entities[index];
            var right = actual.Entities[index];
            var prefix = $"entities[{index}]";
            if ((difference = Compare($"{prefix}.id", left.Id, right.Id)) is not null
                || (difference = Compare($"{prefix}.ownerId", left.OwnerId, right.OwnerId)) is not null
                || (difference = Compare($"{prefix}.positionX", left.PositionX, right.PositionX)) is not null
                || (difference = Compare($"{prefix}.positionY", left.PositionY, right.PositionY)) is not null
                || (difference = Compare($"{prefix}.velocityX", left.VelocityX, right.VelocityX)) is not null
                || (difference = Compare($"{prefix}.velocityY", left.VelocityY, right.VelocityY)) is not null
                || (difference = Compare($"{prefix}.facing", left.Facing, right.Facing)) is not null
                || (difference = Compare($"{prefix}.movementDefinitionId", left.MovementDefinitionId, right.MovementDefinitionId)) is not null)
            {
                return difference;
            }
        }

        return PendingCommandSnapshotDiff.FindMetadata(expected, actual)
            ?? NavigationSnapshotDiff.FindFirst(expected, actual)
            ?? PendingCommandSnapshotDiff.FindObstacles(expected, actual)
            ?? MovementSnapshot.FindFirst(expected, actual)
            ?? PendingCommandSnapshotDiff.FindMoves(expected, actual)
            ?? UnitOrdersSnapshot.FindFirst(expected, actual)
            ?? GroupSnapshot.FindFirst(expected, actual)
            ?? PendingCommandSnapshotDiff.FindGroups(expected, actual)
            ?? GroupPlanningSnapshotRules.FindFirst(expected, actual)
            ?? CooperationSnapshotRules.FindFirst(expected, actual);
    }

    private static SnapshotDifference? Compare<T>(string path, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            return null;
        }

        return new SnapshotDifference(path, Format(expected), Format(actual));
    }

    private static string Format<T>(T value) => value switch
    {
        null => "null",
        SimScalar number => number.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}

