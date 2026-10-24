namespace Rts.Kernel.Combat;

public enum CombatCommandKind { Attack = 1, Stop = 2 }
public enum CombatEventKind { CommandRejected, AttackStarted, StrikeResolved, DamageApplied, AttackEnded, UnitDied }
public enum CombatReason
{
    None, InvalidCommand, MissingAttacker, NotOwner, DeadAttacker,
    TargetLost, InvalidTarget, TargetOutOfRange, Stopped, Replaced, AttackerMissing, Died
}

/// <summary>Commands due for a single frame; the caller owns admission and future scheduling.</summary>
public readonly record struct CombatCommand(
    int PlayerId, long Sequence, CombatCommandKind Kind, EntityId Attacker, EntityId Target = default);

public readonly record struct CombatCommandResult(CombatCommand Command, bool Accepted, CombatReason Reason);

/// <summary>Detached state. Positions/owners remain in the host's entity store, never here.</summary>
public sealed record CombatantState(
    EntityId Id, CombatDefinition Definition, SimScalar Health, EntityId Target,
    long? DamageFrame, long NextAttackFrame, ulong StrikeId, bool Stopped, long ActiveFromFrame)
{
    public bool Alive => Health > SimScalar.Zero;
}

public sealed record CombatEvent(
    long Frame, ulong Sequence, CombatEventKind Kind, EntityId Attacker, EntityId Target,
    ulong StrikeId = 0, SimScalar CalculatedDamage = default, SimScalar AppliedDamage = default,
    CombatReason Reason = CombatReason.None);

public sealed record CombatStepResult(
    long Frame, IReadOnlyList<CombatCommandResult> Commands, IReadOnlyList<CombatEvent> Events);

internal readonly record struct PendingStrike(EntityId Attacker, EntityId Target, ulong Id);

internal sealed class CombatEventLog(long frame, ulong nextSequence)
{
    private readonly List<CombatEvent> _events = [];
    internal ulong NextSequence { get; private set; } = nextSequence;

    internal void Add(CombatEventKind kind, EntityId attacker, EntityId target, ulong strikeId = 0,
        SimScalar calculatedDamage = default, SimScalar appliedDamage = default, CombatReason reason = CombatReason.None)
    {
        _events.Add(new(frame, NextSequence++, kind, attacker, target, strikeId, calculatedDamage, appliedDamage, reason));
    }

    internal IReadOnlyList<CombatEvent> Freeze() => Array.AsReadOnly(_events.ToArray());
}
