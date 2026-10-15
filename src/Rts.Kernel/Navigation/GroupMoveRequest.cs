namespace Rts.Kernel.Navigation;

public sealed record GroupMoveRequest(IReadOnlyList<EntityId> EntityIds, FixedVector2 Goal,
    FormationKind Formation = FormationKind.Compact, EntityId LeaderId = default, SimScalar? Heading = null)
{
    internal bool IsValid => EntityIds is { Count: > 0 and <= FormationLayout.MaximumMembers }
        && EntityIds.All(id => !id.IsNone) && EntityIds.Distinct().Count() == EntityIds.Count
        && (LeaderId.IsNone || EntityIds.Contains(LeaderId)) && Enum.IsDefined(Formation)
        && SimulationDomain.Contains(Goal.X) && SimulationDomain.Contains(Goal.Y);
    internal GroupMoveRequest Freeze() => this with { EntityIds = Array.AsReadOnly(EntityIds.Order().ToArray()) };
}

public sealed record GroupSlot(ulong GroupId, int Slot, FormationKind Formation, FixedVector2 Anchor,
    SimScalar Heading, bool Adjusted)
{
    internal bool IsValid => GroupId > 0 && Slot is >= 0 and < FormationLayout.MaximumMembers
        && Enum.IsDefined(Formation) && SimulationDomain.Contains(Anchor.X) && SimulationDomain.Contains(Anchor.Y)
        && SimulationDomain.Contains(Heading) && Heading >= -SimMath.Pi && Heading < SimMath.Pi;
}

public sealed record GroupMoveOutcome(ulong GroupId, int Slot, FixedVector2? Goal, bool Adjusted);

internal sealed record PlacementMember(EntityId Id, FixedVector2 Start, MovementDefinition Definition, int Clearance, IReadOnlyList<FixedVector2> RetainedPositions);
internal sealed record PlacementBody(FixedVector2 Position, SimScalar Radius);
internal sealed record PlacementGoal(PlacementMember Member, int Slot, FixedVector2? Goal, bool Adjusted);
