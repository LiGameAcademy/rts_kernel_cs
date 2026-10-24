namespace Rts.Kernel.Combat;

/// <summary>Shared wire invariants plus content-dependent restore validation.</summary>
internal static class CombatSnapshotRules
{
    internal static Dictionary<ulong, CombatUnitSnapshot> ValidateStructure(CombatSnapshot snapshot)
    {
        if (snapshot is null || snapshot.FormatVersion != CombatSnapshotJson.CurrentFormatVersion)
            throw new InvalidDataException("Unsupported combat snapshot version.");
        if (snapshot.Frame < 0 || snapshot.Frame > long.MaxValue - CombatTiming.MaxFrames
            || snapshot.NextStrikeId == 0 || snapshot.NextEventSequence < snapshot.NextStrikeId
            || snapshot.DefinitionsHash is null || snapshot.DefinitionsHash.Length != 64 || snapshot.Units is null)
            throw new InvalidDataException("Invalid combat snapshot envelope.");

        var units = new Dictionary<ulong, CombatUnitSnapshot>();
        var identities = new HashSet<ulong>();
        foreach (var unit in snapshot.Units)
        {
            if (unit is null || unit.Id == 0 || !units.TryAdd(unit.Id, unit)
                || unit.Health < 0 || unit.Health > CombatLimits.MaxHealth
                || unit.ActiveFromFrame < 1 || unit.ActiveFromFrame > snapshot.Frame + 1
                || unit.NextAttackFrame < 0 || unit.NextAttackFrame > snapshot.Frame + CombatTiming.MaxFrames
                || unit.StrikeId >= snapshot.NextStrikeId
                || (unit.StrikeId != 0 && !identities.Add(unit.StrikeId)))
                throw new InvalidDataException("Invalid combatant identity, health or timer.");

            if (unit.DamageFrame is { } due && (due <= snapshot.Frame || due > snapshot.Frame + CombatTiming.MaxFrames))
                throw new InvalidDataException("Damage deadline must be in the future.");
            if (unit.Target == 0 && (unit.DamageFrame is not null || unit.StrikeId != 0))
                throw new InvalidDataException("An ended order cannot retain a swing.");
            if (unit.Health == 0 && (unit.Target != 0 || unit.Stopped || unit.ActiveFromFrame > snapshot.Frame))
                throw new InvalidDataException("Dead combatants cannot retain active orders.");
            if (unit.Stopped && unit.Target != 0)
                throw new InvalidDataException("Stopped combatants cannot retain targets.");
        }
        foreach (var unit in units.Values)
        {
            if (unit.Target != 0 && (unit.Target == unit.Id || unit.ActiveFromFrame > snapshot.Frame
                || !units.TryGetValue(unit.Target, out var target) || target.Health == 0 || target.ActiveFromFrame > snapshot.Frame))
                throw new InvalidDataException("Invalid or dead combat target.");
        }
        return units;
    }

    internal static Dictionary<ulong, CombatDefinition> ValidateForRestore(
        CombatSnapshot snapshot, IReadOnlyList<CombatDefinitionEntry> definitions)
    {
        var units = ValidateStructure(snapshot);
        if (definitions is null) throw new InvalidDataException("Combat definitions are required.");
        var catalog = new Dictionary<ulong, CombatDefinition>();
        foreach (var entry in definitions)
        {
            if (entry is null || entry.Id == 0 || entry.Definition is null || !catalog.TryAdd(entry.Id, entry.Definition))
                throw new InvalidDataException("Invalid or duplicate combat definition.");
            try { entry.Definition.Validate(); }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid combat definition.", error); }
        }
        if (catalog.Count != units.Count || units.Keys.Any(id => !catalog.ContainsKey(id))
            || !StringComparer.Ordinal.Equals(snapshot.DefinitionsHash, CombatSnapshotJson.HashDefinitions(definitions)))
            throw new InvalidDataException("Combat definition fingerprint or entity set mismatch.");
        foreach (var unit in units.Values) ValidateTimers(unit, catalog[unit.Id], snapshot);
        return catalog;
    }

    private static void ValidateTimers(CombatUnitSnapshot unit, CombatDefinition definition, CombatSnapshot snapshot)
    {
        if (unit.Health > definition.MaxHealth)
            throw new InvalidDataException("Health exceeds configured maximum.");
        var weapon = definition.Weapon;
        if (unit.NextAttackFrame != 0)
        {
            var started = unit.NextAttackFrame - weapon.CooldownFrames;
            if (started < unit.ActiveFromFrame || started > snapshot.Frame || snapshot.NextStrikeId == 1)
                throw new InvalidDataException("Cooldown has no valid started swing.");
        }
        if (unit.ActiveFromFrame > snapshot.Frame
            && (unit.Health != definition.MaxHealth || unit.Target != 0 || unit.Stopped || unit.NextAttackFrame != 0))
            throw new InvalidDataException("New combatant state must be pristine.");
        if (unit.Target == 0) return;
        if (unit.NextAttackFrame <= snapshot.Frame)
            throw new InvalidDataException("Active intent cannot have an expired cooldown between steps.");
        var releasedAt = unit.NextAttackFrame - weapon.CooldownFrames + weapon.WindupFrames;
        if (unit.DamageFrame is { } due)
        {
            if (unit.StrikeId == 0 || due != releasedAt)
                throw new InvalidDataException("Windup and cooldown do not describe the same swing.");
        }
        else if (unit.StrikeId != 0 && releasedAt > snapshot.Frame)
            throw new InvalidDataException("A future strike cannot already be released.");
    }
}
