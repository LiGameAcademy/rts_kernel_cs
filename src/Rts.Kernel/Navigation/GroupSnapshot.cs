namespace Rts.Kernel.Navigation;

internal static class GroupSnapshot
{
    internal static void Validate(MatchSnapshot snapshot)
    {
        if (snapshot.NextGroupId == 0 || (snapshot.MovementHash is not null &&
            (snapshot.Navigation is null || snapshot.MovementHash.Length != 64
                || snapshot.MovementHash.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))))
            || (snapshot.MovementHash is null && snapshot.Entities.Any(entity => entity.MovementDefinitionId != 0)))
            throw new InvalidDataException("Invalid movement identity or group counter.");
        var groups = new Dictionary<ulong, GroupSlot>();
        var slots = new HashSet<(ulong, int)>();
        foreach (var queue in snapshot.Orders!)
        foreach (var intent in queue.Pending.Prepend(queue.Current))
        {
            var group = intent?.Group;
            if (group is null) continue;
            if (snapshot.MovementHash is null || group.GroupId >= snapshot.NextGroupId
                || !slots.Add((group.GroupId, group.Slot)))
                throw new InvalidDataException("Invalid or duplicate group slot identity.");
            if (groups.TryGetValue(group.GroupId, out var other)
                && (other.Formation != group.Formation || other.Anchor != group.Anchor || other.Heading != group.Heading || other.SoftLanding != group.SoftLanding || other.MemberCount != group.MemberCount))
                throw new InvalidDataException("Group slots disagree on formation geometry.");
            groups[group.GroupId] = group;
        }
        foreach (var pending in snapshot.PendingCommands)
            if (pending.Command.Group is { } group && (!group.IsValid
                || !group.EntityIds.SequenceEqual(group.EntityIds.Order())))
                throw new InvalidDataException("Pending group members must be canonical and valid.");
    }

    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        if (expected.MovementHash != actual.MovementHash)
            return new("movementHash", expected.MovementHash ?? "null", actual.MovementHash ?? "null");
        if (expected.NextGroupId != actual.NextGroupId)
            return new("nextGroupId", expected.NextGroupId.ToString(), actual.NextGroupId.ToString());
        return null;
    }
}
