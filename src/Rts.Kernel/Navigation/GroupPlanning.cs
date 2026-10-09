namespace Rts.Kernel.Navigation;

public sealed record GroupPlanningMember(ulong EntityId, SimVector2 Start);
public sealed record GroupPlanningSnapshot(ulong GroupId, CommandEnvelope Command,
    IReadOnlyList<GroupPlanningMember> Members, double Heading, double Spacing,
    IReadOnlyList<ulong> Canceled, SlotMatchingSnapshot? Matching, int PreparedCandidates = 0);
public sealed record GroupPlanningView(ulong GroupId, int Members, int Canceled, int MatchingRow);

internal sealed class GroupPlanningJob
{
    private readonly GroupPlanningMember[] _members;
    private readonly IReadOnlyList<SimVector2> _ideals;
    private readonly HashSet<ulong> _canceled = [];
    private readonly List<IReadOnlyList<GridCell>> _candidates = [];
    private SlotMatchingWork? _matching;

    internal ulong Id { get; }
    internal CommandEnvelope Command { get; }
    internal IReadOnlyList<GroupPlanningMember> Members { get; }
    internal double Heading { get; }
    internal double Spacing { get; }
    internal int CanceledCount => _canceled.Count;
    internal int MatchingRow => _matching?.NextRow ?? 0;
    internal bool AllCanceled => _canceled.Count == _members.Length;
    internal bool Ready => _matching is { Complete: true } && _candidates.Count == _members.Length;
    internal IEnumerable<GroupPlanningMember> ActiveMembers => _members.Where(member => !_canceled.Contains(member.EntityId));

    internal GroupPlanningJob(ulong id, CommandEnvelope command, IReadOnlyList<PlacementMember> members,
        double heading, double spacing)
        : this(id, command, members.Select(member => new GroupPlanningMember(member.Id.Value, member.Start)).ToArray(),
            heading, spacing)
    {
    }

    private GroupPlanningJob(ulong id, CommandEnvelope command, GroupPlanningMember[] members,
        double heading, double spacing)
    {
        Id = id;
        Command = command.Freeze();
        _members = members.ToArray();
        Members = Array.AsReadOnly(_members);
        Heading = heading;
        Spacing = spacing;
        _ideals = FormationLayout.Create(command.Group!.Formation, members.Length, spacing, command.Group.Goal, heading);
    }

    // Matching and candidate preparation progress independently; preparation may finish first.
    // Return the exact charged column work so the scheduler retains its existing frame budget.
    internal int Advance(NavigationState navigation, int columnBudget, int candidateBudget)
    {
        var spent = 0;
        if (_matching is null)
        {
            StartMatching();
            spent = 2 * (_members.Length - 1) * (_members.Length - 1);
        }
        spent += _matching!.Advance(Math.Max(1, columnBudget - spent));
        var end = Math.Min(_members.Length, _candidates.Count + candidateBudget);
        for (var i = _candidates.Count; i < end; i++)
            _candidates.Add(GroupPlacementPlanner.Candidates(navigation, _ideals[i]));
        return spent;
    }

    internal IReadOnlyList<PlacementGoal> PlanPlacement(NavigationState navigation,
        IReadOnlyList<PlacementMember> members, IReadOnlyList<PlacementBody> bodies)
    {
        var result = _matching!.Result();
        // Cancellation removes active members, not original matching rows or their stable slots.
        var slots = new int[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            var originalIndex = Array.FindIndex(_members, saved => saved.EntityId == members[i].Id.Value);
            slots[i] = result[originalIndex];
        }
        return GroupPlacementPlanner.PlanAssigned(navigation, members, _ideals, slots, bodies, _candidates);
    }

    private void StartMatching()
    {
        var anchor = Array.FindIndex(_members, member => member.EntityId == Command.Group!.LeaderId.Value);
        _matching = new SlotMatchingWork(_members.Select(member => member.Start).ToArray(), _ideals, Math.Max(0, anchor));
    }

    internal bool Contains(EntityId id) => !_canceled.Contains(id.Value)
        && _members.Any(member => member.EntityId == id.Value);

    internal bool Cancel(EntityId id) => Contains(id) && _canceled.Add(id.Value);

    internal GroupPlanningSnapshot Capture() => new(Id, Command.Freeze(),
        Array.AsReadOnly(_members.ToArray()), Heading, Spacing, Array.AsReadOnly(_canceled.Order().ToArray()),
        _matching?.Capture(), _candidates.Count);

    internal static GroupPlanningJob Restore(GroupPlanningSnapshot saved, NavigationState navigation)
    {
        var job = new GroupPlanningJob(saved.GroupId, saved.Command, saved.Members.ToArray(), saved.Heading, saved.Spacing);
        job._canceled.UnionWith(saved.Canceled);
        if (saved.Matching is not null)
        {
            job.StartMatching();
            job._matching!.Restore(saved.Matching);
        }
        for (var i = 0; i < saved.PreparedCandidates; i++)
            job._candidates.Add(GroupPlacementPlanner.Candidates(navigation, job._ideals[i]));
        return job;
    }
}
