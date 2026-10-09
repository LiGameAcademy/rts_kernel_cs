namespace Rts.Kernel.Navigation;

internal static class MovementSnapshot
{
    internal static void Validate(MatchSnapshot snapshot, HashSet<ulong> entityIds)
    {
        if (snapshot.Moves is null || (snapshot.Moves.Count > 0 && snapshot.Navigation is null))
            throw new InvalidDataException("Move orders require navigation and a non-null collection.");
        ulong previous = 0;
        foreach (var move in snapshot.Moves)
        {
            if (move is null || move.EntityId <= previous || !entityIds.Contains(move.EntityId)
                || move.Request is not { IsValid: true } || move.Waypoints is null || move.Waypoints.Count == 0
                || move.WaitFrames < 0 || move.WaitFrames >= RtsMatch.CrowdBlockedSeconds * snapshot.TickRate
                || move.RetryAfterFrame < 0 || move.RetryAfterFrame > snapshot.Frame + RtsMatch.LocalRetryFrames
                || (snapshot.Entities.Single(entity => entity.Id == move.EntityId).MovementDefinitionId == 0
                    && (move.WaitFrames != 0 || move.RetryAfterFrame != 0))
                || move.NextWaypoint < 0 || move.NextWaypoint >= move.Waypoints.Count
                || move.Waypoints.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))
                || move.Waypoints[^1] != move.Request.Goal)
                throw new InvalidDataException("Invalid move order state or noncanonical entity order.");
            previous = move.EntityId;
        }
    }

    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        var a = expected.Moves!;
        var b = actual.Moves!;
        if (a.Count != b.Count) return Difference("moves.count", a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            var left = a[i];
            var right = b[i];
            var prefix = $"moves[{i}]";
            if (left.EntityId != right.EntityId) return Difference(prefix + ".entityId", left.EntityId, right.EntityId);
            if (left.Request != right.Request) return Difference(prefix + ".request", left.Request, right.Request);
            if (left.WaitFrames != right.WaitFrames) return Difference(prefix + ".waitFrames", left.WaitFrames, right.WaitFrames);
            if (left.RetryAfterFrame != right.RetryAfterFrame) return Difference(prefix + ".retryAfterFrame", left.RetryAfterFrame, right.RetryAfterFrame);
            if (left.NextWaypoint != right.NextWaypoint) return Difference(prefix + ".nextWaypoint", left.NextWaypoint, right.NextWaypoint);
            if (left.Waypoints.Count != right.Waypoints.Count)
                return Difference(prefix + ".waypoints.count", left.Waypoints.Count, right.Waypoints.Count);
            for (var j = 0; j < left.Waypoints.Count; j++)
                if (left.Waypoints[j] != right.Waypoints[j])
                    return Difference(prefix + $".waypoints[{j}]", left.Waypoints[j], right.Waypoints[j]);
        }
        for (var i = 0; i < expected.PendingCommands.Count; i++)
            if (expected.PendingCommands[i].Command.Move != actual.PendingCommands[i].Command.Move)
                return Difference($"pendingCommands[{i}].command.move", expected.PendingCommands[i].Command.Move,
                    actual.PendingCommands[i].Command.Move);
        return null;
    }

    private static SnapshotDifference Difference<T>(string path, T left, T right) =>
        new(path, Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture) ?? "null");
}
