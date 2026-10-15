using System.Globalization;
using System.Text.Json;

namespace Rts.Kernel;

/// <summary>Compares queued command data. The coordinator preserves the existing cross-domain diagnostic order.</summary>
internal static class PendingCommandSnapshotDiff
{
    internal static SnapshotDifference? FindMetadata(MatchSnapshot expected, MatchSnapshot actual)
    {
        SnapshotDifference? difference;
        if ((difference = Compare(
                "pendingCommands.count",
                expected.PendingCommands.Count,
                actual.PendingCommands.Count)) is not null)
        {
            return difference;
        }

        for (var index = 0; index < expected.PendingCommands.Count; index++)
        {
            var left = expected.PendingCommands[index];
            var right = actual.PendingCommands[index];
            var prefix = $"pendingCommands[{index}]";
            if ((difference = Compare($"{prefix}.arrivalOrder", left.ArrivalOrder, right.ArrivalOrder)) is not null
                || (difference = Compare($"{prefix}.executeFrame", left.Command.ExecuteFrame, right.Command.ExecuteFrame)) is not null
                || (difference = Compare($"{prefix}.playerId", left.Command.PlayerId, right.Command.PlayerId)) is not null
                || (difference = Compare($"{prefix}.sequence", left.Command.Sequence, right.Command.Sequence)) is not null
                || (difference = Compare($"{prefix}.kind", left.Command.Kind, right.Command.Kind)) is not null
                || (difference = Compare($"{prefix}.entityId", left.Command.EntityId, right.Command.EntityId)) is not null
                || (difference = Compare($"{prefix}.mode", left.Command.Mode, right.Command.Mode)) is not null
                || (difference = Compare($"{prefix}.source", left.Command.Source, right.Command.Source)) is not null
                || (difference = Compare($"{prefix}.positionX", left.Command.Position.X, right.Command.Position.X)) is not null
                || (difference = Compare($"{prefix}.positionY", left.Command.Position.Y, right.Command.Position.Y)) is not null
                || (difference = Compare($"{prefix}.velocityX", left.Command.Velocity.X, right.Command.Velocity.X)) is not null
                || (difference = Compare($"{prefix}.velocityY", left.Command.Velocity.Y, right.Command.Velocity.Y)) is not null)
            {
                return difference;
            }
        }

        return null;
    }

    internal static SnapshotDifference? FindObstacles(MatchSnapshot expected, MatchSnapshot actual)
    {
        for (var i = 0; i < expected.PendingCommands.Count; i++)
        {
            var a = expected.PendingCommands[i].Command.Obstacle;
            var b = actual.PendingCommands[i].Command.Obstacle;
            var prefix = $"pendingCommands[{i}].command.obstacle";
            if ((a is null) != (b is null)) return Difference(prefix + ".present", a is not null, b is not null);
            if (a is null || b is null) continue;
            if (a.Id != b.Id) return Difference(prefix + ".id", a.Id, b.Id);
            if (a.Area != b.Area) return Difference(prefix + ".area", a.Area, b.Area);
        }
        return null;
    }

    internal static SnapshotDifference? FindMoves(MatchSnapshot expected, MatchSnapshot actual)
    {
        for (var i = 0; i < expected.PendingCommands.Count; i++)
            if (expected.PendingCommands[i].Command.Move != actual.PendingCommands[i].Command.Move)
                return Difference($"pendingCommands[{i}].command.move", expected.PendingCommands[i].Command.Move,
                    actual.PendingCommands[i].Command.Move);
        return null;
    }

    internal static SnapshotDifference? FindGroups(MatchSnapshot expected, MatchSnapshot actual)
    {
        for (var i = 0; i < expected.PendingCommands.Count; i++)
        {
            var a = expected.PendingCommands[i].Command;
            var b = actual.PendingCommands[i].Command;
            if (a.MovementDefinitionId != b.MovementDefinitionId)
                return new($"pendingCommands[{i}].movementDefinitionId", a.MovementDefinitionId.ToString(), b.MovementDefinitionId.ToString());
            var left = JsonSerializer.Serialize(a.Group);
            var right = JsonSerializer.Serialize(b.Group);
            if (left != right) return new($"pendingCommands[{i}].group", left, right);
        }
        return null;
    }

    private static SnapshotDifference Difference<T>(string path, T left, T right) =>
        new(path, Convert.ToString(left, CultureInfo.InvariantCulture) ?? "null",
            Convert.ToString(right, CultureInfo.InvariantCulture) ?? "null");

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

