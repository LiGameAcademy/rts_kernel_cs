namespace Rts.Kernel.Combat;

/// <summary>Owns one combatant's health and attack lifecycle; never owns entity movement.</summary>
internal sealed class Combatant(EntityId id, CombatDefinition definition, long activeFromFrame)
{
    internal EntityId Id { get; } = id;
    internal CombatDefinition Definition { get; } = definition;
    internal SimScalar Health { get; private set; } = definition.MaxHealth;
    internal EntityId Target { get; private set; }
    internal long? DamageFrame { get; private set; }
    internal long NextAttackFrame { get; private set; }
    internal ulong StrikeId { get; private set; }
    internal bool Stopped { get; private set; }
    internal long ActiveFromFrame { get; } = activeFromFrame;
    internal bool Alive => Health > SimScalar.Zero;

    internal CombatantState Read() =>
        new(Id, Definition, Health, Target, DamageFrame, NextAttackFrame, StrikeId, Stopped, ActiveFromFrame);

    internal CombatUnitSnapshot Capture() =>
        new(Id.Value, Health, Target.Value, DamageFrame, NextAttackFrame, StrikeId, Stopped, ActiveFromFrame);

    internal static Combatant Restore(CombatUnitSnapshot unit, CombatDefinition definition) =>
        new(new(unit.Id), definition, unit.ActiveFromFrame)
        {
            Health = unit.Health,
            Target = new(unit.Target),
            DamageFrame = unit.DamageFrame,
            NextAttackFrame = unit.NextAttackFrame,
            StrikeId = unit.StrikeId,
            Stopped = unit.Stopped
        };

    internal void SetTarget(EntityId target)
    {
        Target = target;
        StrikeId = 0;
        DamageFrame = null;
        Stopped = false;
    }

    internal void EndAttack(bool stopped = false)
    {
        Target = EntityId.None;
        StrikeId = 0;
        DamageFrame = null;
        Stopped = stopped;
        // Ending an intent never refunds the cooldown already reserved by a started swing.
    }

    internal void BeginSwing(long frame, ulong strikeId)
    {
        StrikeId = strikeId;
        DamageFrame = frame + Definition.Weapon.WindupFrames;
        NextAttackFrame = frame + Definition.Weapon.CooldownFrames;
    }

    internal PendingStrike ReleaseStrike()
    {
        DamageFrame = null;
        return new(Id, Target, StrikeId);
    }

    internal SimScalar ApplyDamage(SimScalar amount)
    {
        var applied = amount < Health ? amount : Health;
        Health -= applied;
        return applied;
    }
}
