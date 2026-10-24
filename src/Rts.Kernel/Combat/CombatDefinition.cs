namespace Rts.Kernel.Combat;

public enum AttackType { Normal, Pierce, Siege, Magic, Chaos, Spells, Hero }
public enum ArmorType { Light, Medium, Heavy, Fortified, Hero, Divine, Unarmored }

/// <summary>Immutable ordinary-unit weapon parameters; all durations are logical frames.</summary>
public sealed record WeaponDefinition(
    SimScalar BaseDamage, int Dice, int Sides, AttackType AttackType,
    SimScalar MinRange, SimScalar MaxRange, int WindupFrames, int CooldownFrames)
{
    public void Validate()
    {
        CombatLimits.RequireRange(BaseDamage, 0, CombatLimits.MaxBaseDamage, nameof(BaseDamage));
        if (Dice is < 0 or > CombatLimits.MaxDice) throw new ArgumentOutOfRangeException(nameof(Dice));
        if (Sides is < 1 or > CombatLimits.MaxSides) throw new ArgumentOutOfRangeException(nameof(Sides));
        if (!Enum.IsDefined(AttackType)) throw new ArgumentOutOfRangeException(nameof(AttackType));
        CombatLimits.RequireRange(MinRange, 0, CombatLimits.MaxRange, nameof(MinRange));
        CombatLimits.RequireRange(MaxRange, MinRange, CombatLimits.MaxRange, nameof(MaxRange));
        if (CooldownFrames is < 1 or > CombatTiming.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(CooldownFrames));
        if (WindupFrames < 0 || WindupFrames > CooldownFrames)
            throw new ArgumentOutOfRangeException(nameof(WindupFrames));
    }
}

public sealed record CombatDefinition(
    SimScalar MaxHealth, SimScalar Armor, ArmorType ArmorType, WeaponDefinition Weapon)
{
    public void Validate()
    {
        CombatLimits.RequireRange(MaxHealth, SimScalar.FromRaw(1), CombatLimits.MaxHealth, nameof(MaxHealth));
        CombatLimits.RequireArmor(Armor, ArmorType);
        ArgumentNullException.ThrowIfNull(Weapon);
        Weapon.Validate();
    }
}

/// <summary>Supported input domain, chosen so each 32.32 intermediate is representable.</summary>
internal static class CombatLimits
{
    internal const int MaxBaseDamage = 1000000;
    internal const int MaxDice = 64;
    internal const int MaxSides = 1000000;
    internal const int MaxRoll = MaxBaseDamage + MaxDice * MaxSides;
    internal const int MaxHealth = 1000000000;
    internal const int MaxRange = 1000000;
    internal const int MaxArmor = 1000000;

    internal static void RequireRange(SimScalar value, SimScalar minimum, SimScalar maximum, string name)
    {
        if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
    }

    internal static void RequireArmor(SimScalar armor, ArmorType type)
    {
        RequireRange(armor, -MaxArmor, MaxArmor, nameof(armor));
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
    }
}
