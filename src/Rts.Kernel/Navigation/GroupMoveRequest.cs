namespace Rts.Kernel.Navigation;

public sealed record GroupMoveRequest(IReadOnlyList<EntityId> EntityIds, SimVector2 Goal,
    FormationKind Formation = FormationKind.Compact, EntityId LeaderId = default, double? Heading = null)
{
    internal bool IsValid => EntityIds is { Count: > 0 and <= FormationLayout.MaximumMembers }
        && EntityIds.All(id => !id.IsNone) && EntityIds.Distinct().Count() == EntityIds.Count
        && (LeaderId.IsNone || EntityIds.Contains(LeaderId)) && Enum.IsDefined(Formation)
        && double.IsFinite(Goal.X) && double.IsFinite(Goal.Y) && (Heading is null || double.IsFinite(Heading.Value));
    internal GroupMoveRequest Freeze() => this with { EntityIds = Array.AsReadOnly(EntityIds.Order().ToArray()) };
}

public sealed record GroupSlot(ulong GroupId, int Slot, FormationKind Formation, SimVector2 Anchor,
    double Heading, bool Adjusted)
{
    internal bool IsValid => GroupId > 0 && Slot is >= 0 and < FormationLayout.MaximumMembers
        && Enum.IsDefined(Formation) && double.IsFinite(Anchor.X) && double.IsFinite(Anchor.Y)
        && double.IsFinite(Heading) && Heading >= -Math.PI && Heading < Math.PI;
}

public sealed record GroupMoveOutcome(ulong GroupId, int Slot, SimVector2? Goal, bool Adjusted);

internal sealed record PlacementMember(EntityId Id, SimVector2 Start, MovementDefinition Definition, int Clearance, IReadOnlyList<SimVector2> RetainedPositions);
internal sealed record PlacementBody(SimVector2 Position, double Radius);
internal sealed record PlacementGoal(PlacementMember Member, int Slot, SimVector2? Goal, bool Adjusted);
