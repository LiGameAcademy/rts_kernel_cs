namespace Rts.Kernel.Combat;

/// <summary>
/// Combat stage for explicitly targeted, stationary ordinary units. Call once per logical frame,
/// between entity updates; not thread-safe. Owns combat state/RNG only, never positions or the host clock.
/// </summary>
public sealed class StationaryCombat(ulong seed)
{
    private readonly SortedDictionary<EntityId, Combatant> _units = [];
    private readonly DeterministicRng _rng = new(seed);
    private ulong _nextStrikeId = 1;
    private ulong _nextEventSequence = 1;

    public long Frame { get; private set; }
    public ulong RngState => _rng.State;

    public IReadOnlyList<CombatDefinitionEntry> ReadDefinitions() =>
        Array.AsReadOnly(_units.Values.Select(unit => new CombatDefinitionEntry(unit.Id.Value, unit.Definition)).ToArray());

    /// <summary>Capture between frames; excludes host entities, command scheduling and already returned events.</summary>
    public CombatSnapshot CaptureSnapshot() => new(
        CombatSnapshotJson.CurrentFormatVersion, Frame, _rng.State, _nextStrikeId, _nextEventSequence,
        CombatSnapshotJson.HashDefinitions(ReadDefinitions()),
        Array.AsReadOnly(_units.Values.Select(unit => unit.Capture()).ToArray()));

    public string ComputeStateHash() => CombatSnapshotJson.Hash(CaptureSnapshot());

    public static StationaryCombat Restore(CombatSnapshot snapshot, IReadOnlyList<CombatDefinitionEntry> definitions)
    {
        var catalog = CombatSnapshotRules.ValidateForRestore(snapshot, definitions);
        var combat = new StationaryCombat(snapshot.RngState)
        {
            Frame = snapshot.Frame,
            _nextStrikeId = snapshot.NextStrikeId,
            _nextEventSequence = snapshot.NextEventSequence
        };
        foreach (var unit in snapshot.Units.OrderBy(unit => unit.Id))
            combat._units.Add(new(unit.Id), Combatant.Restore(unit, catalog[unit.Id]));
        return combat;
    }

    /// <summary>Register between frames. IDs, including dead IDs, cannot be reused.</summary>
    public void AddCombatant(EntityId id, CombatDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        if (id.IsNone || _units.ContainsKey(id)) throw new ArgumentException("Combatant ID must be new and nonzero.", nameof(id));
        if (Frame >= long.MaxValue - CombatTiming.MaxFrames) throw new ArgumentException("Combat frame limit reached.");
        _units.Add(id, new Combatant(id, definition, Frame + 1));
    }

    public IReadOnlyList<CombatantState> ReadCombatants() =>
        Array.AsReadOnly(_units.Values.Select(unit => unit.Read()).ToArray());

    /// <summary>
    /// Validates the complete entity view before mutation. Commands are ordered by player/sequence/input
    /// index like the existing match queue; admission, duplicate policy and future scheduling belong to its caller.
    /// Invalid business commands produce rejection results without replacing existing intent or cooldown.
    /// </summary>
    public CombatStepResult Advance(long frame, IReadOnlyList<EntityState> entities, IReadOnlyList<CombatCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(commands);
        if (frame <= 0 || frame != Frame + 1 || frame > long.MaxValue - CombatTiming.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(frame), "Combat frames must be consecutive and leave room for timers.");
        var view = ValidateEntities(entities);
        var orderedCommands = commands.OrderBy(command => command.PlayerId).ThenBy(command => command.Sequence).ToArray();
        // Reserve enough identities before any mutation, including rejected commands and terminal cleanup.
        if (_nextStrikeId > ulong.MaxValue - (ulong)_units.Count
            || _nextEventSequence > ulong.MaxValue - (ulong)commands.Count * 2 - (ulong)_units.Count * 8)
            throw new ArgumentException("Combat identity limit reached.");

        Frame = frame;
        var log = new CombatEventLog(frame, _nextEventSequence);
        var results = orderedCommands.Select(command => Execute(command, view, log)).ToArray();
        var strikes = CollectStrikes(view, log);
        ResolveStrikes(strikes, log);
        _nextEventSequence = log.NextSequence;
        return new(frame, Array.AsReadOnly(results), log.Freeze());
    }

    private Dictionary<EntityId, EntityState> ValidateEntities(IReadOnlyList<EntityState> entities)
    {
        var view = new Dictionary<EntityId, EntityState>(entities.Count);
        foreach (var entity in entities)
        {
            if (entity is null || entity.Id.IsNone || entity.OwnerId < 0
                || !SimulationDomain.Contains(entity.Position) || !view.TryAdd(entity.Id, entity))
                throw new ArgumentException("Invalid or duplicate entity view.", nameof(entities));
            if (_units.TryGetValue(entity.Id, out var unit) && unit.Alive && entity.Velocity != FixedVector2.Zero)
                throw new ArgumentException("Moving combatants are outside the stationary stage.", nameof(entities));
        }
        return view;
    }

    private CombatCommandResult Execute(CombatCommand command, Dictionary<EntityId, EntityState> view, CombatEventLog log)
    {
        var reason = CommandError(command, view);
        if (reason != CombatReason.None)
        {
            log.Add(CombatEventKind.CommandRejected, command.Attacker, command.Target, reason: reason);
            return new(command, false, reason);
        }
        var unit = _units[command.Attacker];
        if (command.Kind == CombatCommandKind.Stop)
        {
            EndAttack(unit, CombatReason.Stopped, log);
        }
        else if (unit.Target != command.Target)
        {
            EndAttack(unit, CombatReason.Replaced, log);
            unit.SetTarget(command.Target);
        }
        return new(command, true, CombatReason.None);
    }

    private CombatReason CommandError(CombatCommand command, Dictionary<EntityId, EntityState> view)
    {
        if (command.PlayerId < 0 || command.Sequence < 0 || !Enum.IsDefined(command.Kind)
            || command.Attacker.IsNone || (command.Kind == CombatCommandKind.Stop && !command.Target.IsNone))
            return CombatReason.InvalidCommand;
        if (!_units.TryGetValue(command.Attacker, out var unit) || !view.TryGetValue(command.Attacker, out var entity))
            return CombatReason.MissingAttacker;
        if (entity.OwnerId != command.PlayerId) return CombatReason.NotOwner;
        if (!unit.Alive || unit.ActiveFromFrame > Frame) return CombatReason.DeadAttacker;
        return command.Kind == CombatCommandKind.Attack ? TargetError(unit, command.Target, view) : CombatReason.None;
    }

    private CombatReason TargetError(Combatant unit, EntityId targetId, Dictionary<EntityId, EntityState> view)
    {
        if (targetId.IsNone || !_units.TryGetValue(targetId, out var target) || !target.Alive
            || target.ActiveFromFrame > Frame || !view.TryGetValue(targetId, out var targetEntity))
            return CombatReason.TargetLost;
        if (!view.TryGetValue(unit.Id, out var attacker)) return CombatReason.AttackerMissing;
        if (unit.Id == targetId || attacker.OwnerId == targetEntity.OwnerId) return CombatReason.InvalidTarget;
        var distance = SquaredDistance.Between(attacker.Position, targetEntity.Position).Raw;
        var weapon = unit.Definition.Weapon;
        Int128 minimum = (Int128)weapon.MinRange.Raw * weapon.MinRange.Raw;
        Int128 maximum = (Int128)weapon.MaxRange.Raw * weapon.MaxRange.Raw;
        return distance < minimum || distance > maximum ? CombatReason.TargetOutOfRange : CombatReason.None;
    }

    private List<PendingStrike> CollectStrikes(Dictionary<EntityId, EntityState> view, CombatEventLog log)
    {
        var strikes = new List<PendingStrike>();
        foreach (var unit in _units.Values)
        {
            if (!unit.Alive || unit.Target.IsNone || unit.ActiveFromFrame > Frame) continue;
            var reason = TargetError(unit, unit.Target, view);
            if (reason != CombatReason.None)
            {
                EndAttack(unit, reason, log);
                continue;
            }
            if (unit.DamageFrame is { } due && Frame >= due) strikes.Add(unit.ReleaseStrike());
            if (unit.DamageFrame is null && Frame >= unit.NextAttackFrame)
            {
                unit.BeginSwing(Frame, _nextStrikeId++);
                log.Add(CombatEventKind.AttackStarted, unit.Id, unit.Target, unit.StrikeId);
                if (unit.DamageFrame == Frame) strikes.Add(unit.ReleaseStrike());
            }
        }
        return strikes;
    }

    private void ResolveStrikes(List<PendingStrike> strikes, CombatEventLog log)
    {
        var killers = new SortedDictionary<EntityId, PendingStrike>();
        // The collection is already ordered by attacker ID. Do not revalidate attacker life after collection:
        // a lethal hit this frame must not retract another participant's already committed strike.
        foreach (var strike in strikes)
        {
            var attacker = _units[strike.Attacker];
            var target = _units[strike.Target];
            var roll = CombatDice.Roll(attacker.Definition.Weapon, _rng);
            var damage = CombatDamageRules.Resolve(roll, attacker.Definition.Weapon.AttackType,
                target.Definition.ArmorType, target.Definition.Armor).Amount;
            log.Add(CombatEventKind.StrikeResolved, strike.Attacker, strike.Target, strike.Id);
            var wasAlive = target.Alive;
            var applied = target.ApplyDamage(damage);
            log.Add(CombatEventKind.DamageApplied, strike.Attacker, strike.Target, strike.Id, damage, applied);
            if (wasAlive && !target.Alive) killers.Add(target.Id, strike);
        }

        foreach (var (victim, strike) in killers)
        {
            EndAttack(_units[victim], CombatReason.Died, log);
            log.Add(CombatEventKind.UnitDied, strike.Attacker, victim, strike.Id);
        }
        foreach (var unit in _units.Values)
            if (!unit.Target.IsNone && !_units[unit.Target].Alive) EndAttack(unit, CombatReason.TargetLost, log);
    }

    private static void EndAttack(Combatant unit, CombatReason reason, CombatEventLog log)
    {
        if (!unit.Target.IsNone)
            log.Add(CombatEventKind.AttackEnded, unit.Id, unit.Target, unit.StrikeId, reason: reason);
        unit.EndAttack(stopped: reason == CombatReason.Stopped);
    }
}
