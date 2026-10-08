namespace Rts.Kernel.Navigation;

public sealed record GroupPlanningMember(ulong EntityId, SimVector2 Start);
public sealed record GroupPlanningSnapshot(ulong GroupId, CommandEnvelope Command,
    IReadOnlyList<GroupPlanningMember> Members, double Heading, double Spacing,
    IReadOnlyList<ulong> Canceled, SlotMatchingSnapshot? Matching, int PreparedCandidates = 0);
public sealed record GroupPlanningView(ulong GroupId, int Members, int Canceled, int MatchingRow);

internal sealed class GroupPlanningJob
{
    internal readonly ulong Id;
    internal readonly CommandEnvelope Command;
    internal readonly GroupPlanningMember[] Members;
    internal readonly double Heading;
    internal readonly double Spacing;
    internal readonly IReadOnlyList<SimVector2> Ideals;
    internal readonly HashSet<ulong> Canceled = [];
    internal SlotMatchingWork? Matching;
    internal readonly List<IReadOnlyList<GridCell>> Candidates = [];

    internal GroupPlanningJob(ulong id, CommandEnvelope command, IReadOnlyList<PlacementMember> members,
        double heading, double spacing)
        : this(id, command, members.Select(member => new GroupPlanningMember(member.Id.Value, member.Start)).ToArray(),
            heading, spacing) { }

    private GroupPlanningJob(ulong id, CommandEnvelope command, GroupPlanningMember[] members,
        double heading, double spacing)
    {
        Id = id; Command = command.Freeze(); Members = members.ToArray(); Heading = heading; Spacing = spacing;
        Ideals = FormationLayout.Create(command.Group!.Formation, members.Length, spacing, command.Group.Goal, heading);
    }

    internal void StartMatching()
    {
        var anchor = Array.FindIndex(Members, member => member.EntityId == Command.Group!.LeaderId.Value);
        Matching ??= new SlotMatchingWork(Members.Select(member => member.Start).ToArray(), Ideals, Math.Max(0, anchor));
    }

    internal bool Contains(EntityId id) => !Canceled.Contains(id.Value)
        && Members.Any(member => member.EntityId == id.Value);
    internal GroupPlanningSnapshot Capture() => new(Id, Command.Freeze(),
        Array.AsReadOnly(Members.ToArray()), Heading, Spacing, Array.AsReadOnly(Canceled.Order().ToArray()), Matching?.Capture(), Candidates.Count);

    internal static GroupPlanningJob Restore(GroupPlanningSnapshot saved, NavigationState navigation)
    {
        var job = new GroupPlanningJob(saved.GroupId, saved.Command, saved.Members.ToArray(), saved.Heading, saved.Spacing);
        job.Canceled.UnionWith(saved.Canceled);
        if (saved.Matching is not null)
        { job.StartMatching(); job.Matching!.Restore(saved.Matching); }
        for (var i = 0; i < saved.PreparedCandidates; i++)
            job.Candidates.Add(GroupPlacementPlanner.Candidates(navigation, job.Ideals[i]));
        return job;
    }
}
