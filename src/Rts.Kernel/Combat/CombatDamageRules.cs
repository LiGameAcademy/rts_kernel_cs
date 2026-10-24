namespace Rts.Kernel.Combat;

public readonly record struct DamageResult(
    SimScalar Roll, SimScalar TypeMultiplier, SimScalar ArmorFactor, SimScalar Amount);

/// <summary>Existing project damage formula, with explicit fixed-point operation order.</summary>
public static class CombatDamageRules
{
    // Columns: light, medium, heavy, fortified, hero, divine, unarmored. No unknown-type fallback.
    private static readonly int[,] Percent =
    {
        {100, 150, 100, 70, 100, 5, 100},
        {200, 75, 100, 35, 50, 5, 150},
        {100, 50, 100, 150, 50, 5, 150},
        {125, 75, 200, 35, 50, 5, 100},
        {100, 100, 100, 100, 100, 100, 100},
        {100, 100, 100, 100, 70, 5, 100},
        {100, 100, 100, 50, 100, 5, 100},
    };

    public static DamageResult Resolve(SimScalar roll, AttackType attack, ArmorType defense, SimScalar armor)
    {
        CombatLimits.RequireRange(roll, 0, CombatLimits.MaxRoll, nameof(roll));
        if (!Enum.IsDefined(attack)) throw new ArgumentOutOfRangeException(nameof(attack));
        CombatLimits.RequireArmor(armor, defense);
        var multiplier = SimScalar.FromRatio(Percent[(int)attack, (int)defense], 100);
        var absoluteArmor = armor < 0 ? -armor : armor;
        var coefficient = SimScalar.FromRatio(3, 50);
        var armorFactor = SimScalar.One - coefficient * armor / (SimScalar.One + coefficient * absoluteArmor);
        var amount = roll * multiplier * armorFactor;
        if (amount < SimScalar.Zero) amount = SimScalar.Zero;
        if (amount > SimScalar.Zero && amount < SimScalar.One) amount = SimScalar.One;
        return new DamageResult(roll, multiplier, armorFactor, amount);
    }
}
