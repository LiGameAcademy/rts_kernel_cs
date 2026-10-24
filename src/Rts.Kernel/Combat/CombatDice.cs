namespace Rts.Kernel.Combat;

/// <summary>Only committed, valid strikes should call this method; queries must not advance RNG.</summary>
public static class CombatDice
{
    public static SimScalar Roll(WeaponDefinition weapon, DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(rng);
        weapon.Validate();
        var roll = weapon.BaseDamage;
        for (var die = 0; die < weapon.Dice; die++) roll += NextDie(rng, weapon.Sides);
        return roll;
    }

    private static int NextDie(DeterministicRng rng, int sides)
    {
        ulong bound = (ulong)sides;
        // Reject the short remainder of the 2^64 domain; every result has equal preimage count.
        ulong threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = rng.NextUInt64(); } while (value < threshold);
        return checked((int)(value % bound)) + 1;
    }
}
