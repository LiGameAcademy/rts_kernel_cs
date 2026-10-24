using System.Text.Json.Serialization;

namespace Rts.Kernel.Combat;

public sealed record CombatDefinitionEntry(ulong Id, CombatDefinition Definition);

/// <summary>Combat-owned state only. The host separately saves entities and due/future commands.</summary>
public sealed record CombatSnapshot(
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] long Frame,
    [property: JsonRequired] ulong RngState,
    [property: JsonRequired] ulong NextStrikeId,
    [property: JsonRequired] ulong NextEventSequence,
    [property: JsonRequired] string DefinitionsHash,
    [property: JsonRequired] IReadOnlyList<CombatUnitSnapshot> Units);

public sealed record CombatUnitSnapshot(
    [property: JsonRequired] ulong Id,
    [property: JsonRequired] SimScalar Health,
    [property: JsonRequired] ulong Target,
    [property: JsonRequired] long? DamageFrame,
    [property: JsonRequired] long NextAttackFrame,
    [property: JsonRequired] ulong StrikeId,
    [property: JsonRequired] bool Stopped,
    [property: JsonRequired] long ActiveFromFrame);
