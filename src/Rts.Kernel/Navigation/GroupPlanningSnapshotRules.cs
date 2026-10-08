using System.Text.Json;

namespace Rts.Kernel.Navigation;

internal static class GroupPlanningSnapshotRules
{
    internal static void Validate(MatchSnapshot snapshot)
    {
        if (snapshot.GroupPlans is null || snapshot.GroupPlans.Count > RtsMatch.MaximumGroupPlans
            || (snapshot.GroupPlans.Count > 0 && (snapshot.MovementHash is null || snapshot.Navigation is null)))
            throw new InvalidDataException("Invalid group planning collection.");
        ulong previous = 0;
        foreach (var job in snapshot.GroupPlans)
        {
            if (job is null || job.GroupId <= previous || job.GroupId >= snapshot.NextGroupId
                || job.Command is not { Kind: CommandKind.GroupMove } command || !command.HasValidGroupPayload
                || !command.HasValidOrderMetadata || !command.HasValidMovePayload || !command.HasValidObstaclePayload || command.ExecuteFrame <= 0 || command.ExecuteFrame > snapshot.Frame
                || command.PlayerId < 0 || command.Sequence < 0 || job.Members is null || job.Canceled is null
                || job.Members.Count <= RtsMatch.ImmediateGroupMembers || job.Members.Count > FormationLayout.MaximumMembers
                || !double.IsFinite(job.Heading) || job.Heading < -Math.PI || job.Heading >= Math.PI
                || !double.IsFinite(job.Spacing) || job.Spacing <= 0
                || job.PreparedCandidates < 0 || job.PreparedCandidates > job.Members.Count
                || (job.PreparedCandidates > 0 && job.Matching?.Row != job.Members.Count))
                throw new InvalidDataException("Invalid group planning identity or geometry.");
            previous = job.GroupId;
            ulong last = 0;
            foreach (var member in job.Members)
            {
                if (member is null || member.EntityId <= last
                    || !command.Group!.EntityIds.Contains(new EntityId(member.EntityId))
                    || !snapshot.Entities.Any(entity => entity.Id == member.EntityId)
                    || !double.IsFinite(member.Start.X) || !double.IsFinite(member.Start.Y))
                    throw new InvalidDataException("Invalid group planning member.");
                last = member.EntityId;
            }
            if (!job.Canceled.SequenceEqual(job.Canceled.Order().Distinct())
                || job.Canceled.Any(id => !job.Members.Any(member => member.EntityId == id)))
                throw new InvalidDataException("Invalid group planning cancellation.");
            if (!command.Group!.EntityIds.SequenceEqual(command.Group.EntityIds.Order()))
                throw new InvalidDataException("Planning command members must be canonical.");
            if (job.Matching is not null) SlotMatchingWork.Validate(job.Matching, job.Members.Count - 1);
        }
    }

    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        if (expected.GroupPlans!.Count != actual.GroupPlans!.Count)
            return new("groupPlans.count", expected.GroupPlans.Count.ToString(), actual.GroupPlans.Count.ToString());
        for (var i = 0; i < expected.GroupPlans.Count; i++)
        {
            var left = JsonSerializer.Serialize(expected.GroupPlans[i]);
            var right = JsonSerializer.Serialize(actual.GroupPlans[i]);
            if (left != right) return new($"groupPlans[{i}]", left, right);
        }
        return null;
    }
}
