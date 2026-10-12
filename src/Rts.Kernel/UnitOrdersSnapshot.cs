namespace Rts.Kernel;

internal static class UnitOrdersSnapshot
{
    internal static void Validate(MatchSnapshot snapshot, HashSet<ulong> entityIds)
    {
        if (snapshot.Orders is null) throw new InvalidDataException("Unit orders collection is required.");
        var moves = snapshot.Moves!.ToDictionary(move => move.EntityId);
        var states = snapshot.Entities.ToDictionary(entity => entity.Id);
        ulong previous = 0;
        var matchedMoves = 0;
        foreach (var queue in snapshot.Orders)
        {
            if (queue is null || queue.EntityId <= previous || !entityIds.Contains(queue.EntityId)
                || queue.Pending is null || queue.Pending.Count > RtsMatch.MaximumPendingOrders
                || (queue.Current is null && queue.Pending.Count == 0)
                || (queue.Current is not null && !queue.Current.IsValid)
                || (queue.Current?.Kind == UnitOrderKind.Stop && queue.Pending.Count > 0)
                || queue.Pending.Any(order => order is not { IsValid: true, Kind: UnitOrderKind.Move }))
                throw new InvalidDataException("Invalid unit order queue or noncanonical entity order.");
            previous = queue.EntityId;
            if (queue.Current?.Kind == UnitOrderKind.Move)
            {
                if (!moves.TryGetValue(queue.EntityId, out var move) || move.Request != queue.Current.Move)
                    throw new InvalidDataException("Current move intent does not match active path.");
                matchedMoves++;
            }
            else
            {
                var entity = states[queue.EntityId];
                if (moves.ContainsKey(queue.EntityId) || entity.VelocityX != 0 || entity.VelocityY != 0)
                    throw new InvalidDataException("Stopped or waiting order cannot have active movement.");
            }
            ValidateTerrain(snapshot, queue.Current);
            foreach (var intent in queue.Pending) ValidateTerrain(snapshot, intent);
        }
        if (matchedMoves != moves.Count)
            throw new InvalidDataException("Every active move requires its current unit order.");
    }

    private static void ValidateTerrain(MatchSnapshot snapshot, UnitOrderIntent? intent)
    {
        if (intent?.Kind != UnitOrderKind.Move) return;
        if (snapshot.Navigation is null || (intent.Move!.Motion is { ScaleSlopeSpeed: true }
            && snapshot.Navigation.HeightHash is null))
            throw new InvalidDataException("Move intent requires its navigation and enabled slope height identity.");
    }

    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        var a = expected.Orders!;
        var b = actual.Orders!;
        if (a.Count != b.Count) return Difference("orders.count", a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            var left = a[i];
            var right = b[i];
            var prefix = $"orders[{i}]";
            if (left.EntityId != right.EntityId) return Difference(prefix + ".entityId", left.EntityId, right.EntityId);
            var currentDiff = CompareIntent(prefix + ".current", left.Current, right.Current);
            if (currentDiff is not null) return currentDiff;
            if (left.Pending.Count != right.Pending.Count)
                return Difference(prefix + ".pending.count", left.Pending.Count, right.Pending.Count);
            for (var j = 0; j < left.Pending.Count; j++)
            {
                var pendingDiff = CompareIntent(prefix + $".pending[{j}]", left.Pending[j], right.Pending[j]);
                if (pendingDiff is not null) return pendingDiff;
            }
        }
        return null;
    }

    private static SnapshotDifference? CompareIntent(string prefix, UnitOrderIntent? a, UnitOrderIntent? b)
    {
        if ((a is null) != (b is null)) return Difference(prefix + ".present", a is not null, b is not null);
        if (a is null || b is null) return null;
        if (a.Kind != b.Kind) return Difference(prefix + ".kind", a.Kind, b.Kind);
        if (a.Source != b.Source) return Difference(prefix + ".source", a.Source, b.Source);
        if (a.Move != b.Move) return Difference(prefix + ".move", a.Move, b.Move);
        return a.Group != b.Group ? Difference(prefix + ".group", a.Group, b.Group) : null;
    }

    private static SnapshotDifference Difference<T>(string path, T left, T right) =>
        new(path, Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture) ?? "null");
}
