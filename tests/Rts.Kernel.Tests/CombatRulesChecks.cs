using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatRulesChecks
{
    internal static void Run(Action<bool, string> check)
    {
        int[][] expected =
        [
            [100, 150, 100, 70, 100, 5, 100],
            [200, 75, 100, 35, 50, 5, 150],
            [100, 50, 100, 150, 50, 5, 150],
            [125, 75, 200, 35, 50, 5, 100],
            [100, 100, 100, 100, 100, 100, 100],
            [100, 100, 100, 100, 70, 5, 100],
            [100, 100, 100, 50, 100, 5, 100],
        ];
        foreach (var attack in Enum.GetValues<AttackType>())
        foreach (var defense in Enum.GetValues<ArmorType>())
        {
            var result = CombatDamageRules.Resolve(100, attack, defense, 0);
            check(result.Amount == SimScalar.FromRaw((long)expected[(int)attack][(int)defense] * SimScalar.Scale / 100 * 100),
                $"attack/armor table {attack}/{defense}");
        }

        var positive = CombatDamageRules.Resolve(160, AttackType.Normal, ArmorType.Light, 10);
        var negative = CombatDamageRules.Resolve(160, AttackType.Normal, ArmorType.Light, -10);
        check(positive.Amount.Raw == 100L * SimScalar.Scale + 480,
            "positive armor preserves the declared fixed truncation at each operation");
        check(negative.Amount.Raw == 220L * SimScalar.Scale - 480,
            "negative armor follows existing project formula with fixed truncation");
        check(CombatDamageRules.Resolve(0, AttackType.Normal, ArmorType.Light, 100).Amount == SimScalar.Zero,
            "zero damage does not trigger minimum damage");
        check(CombatDamageRules.Resolve(SimScalar.FromRaw(1), AttackType.Normal, ArmorType.Light, 0).Amount == SimScalar.One,
            "positive sub-unit damage rounds up to minimum one");
        check(CombatDamageRules.Resolve(1, AttackType.Normal, ArmorType.Divine, 100).Amount == SimScalar.One,
            "type and armor reduction precede minimum damage");
        check(CombatTiming.FramesFromSeconds(SimScalar.FromRatio(1, 10), 30) == 3,
            "fixed representation of 0.1 seconds stays no earlier than its input");
        check(CombatTiming.FramesFromSeconds(SimScalar.FromRaw(1), 30) == 1, "tiny nonzero duration uses one frame");
        check(CombatTiming.FramesFromSeconds(1, 30) == 30, "whole second frame conversion");
        check(CombatTiming.FramesFromSeconds(0, 30) == 0, "zero windup remains zero");
        check(CombatTiming.FramesFromSeconds(3600, 240) == 864000, "largest admitted timer conversion");

        var oneFrame = SimScalar.FromRatio(1, 30);
        check(CombatTiming.FramesFromSeconds(oneFrame, 30) == 1
            && CombatTiming.FramesFromSeconds(SimScalar.FromRaw(oneFrame.Raw + 1), 30) == 2,
            "one raw unit across a duration boundary changes ceiling, never attacks early");
        check(CombatDamageRules.Resolve(SimScalar.FromRaw(1), AttackType.Normal, ArmorType.Divine, 0).Amount == 0,
            "positive values below fixed precision truncate before the minimum-damage test");
        var previous = CombatDamageRules.Resolve(1000, AttackType.Normal, ArmorType.Light, -100).Amount;
        for (var armor = -99; armor <= 100; armor++)
        {
            var current = CombatDamageRules.Resolve(1000, AttackType.Normal, ArmorType.Light, armor).Amount;
            check(current <= previous && current > 0, "armor monotonically reduces damage across zero");
            previous = current;
        }
        var vector = new DeterministicRng(0);
        check(vector.NextUInt64() == 0xE220A8397B1DCDAFUL
            && vector.NextUInt64() == 0x6E789E6AA1B965F4UL
            && vector.NextUInt64() == 0x06C45D188009454FUL,
            "pinned SplitMix64 vectors include unsigned state wraparound");

        var weapon = Weapon();
        weapon.Validate();
        var rng = new DeterministicRng(0);
        var roll = CombatDice.Roll(weapon, rng);
        check(roll == 13, "seed zero, base ten plus two d6 known vector");
        check(rng.State == 0x3C6EF372FE94F82AUL, "exactly two accepted dice draws consume stream");
        var restored = new DeterministicRng(rng.State);
        for (var i = 0; i < 100; i++)
            check(CombatDice.Roll(weapon, rng) == CombatDice.Roll(weapon, restored), "restored dice sequence");
        var before = rng.State;
        check(CombatDice.Roll(weapon with { Dice = 0 }, rng) == 10 && rng.State == before,
            "no dice consumes no RNG");
        weapon.Validate();
        _ = CombatDamageRules.Resolve(10, AttackType.Normal, ArmorType.Light, 0);
        check(rng.State == before, "definition validation and damage calculation are pure");
        for (var i = 0; i < 100; i++)
        {
            var value = CombatDice.Roll(weapon with { BaseDamage = 0, Dice = 1, Sides = 1 }, rng);
            check(value == SimScalar.One, "one-sided die closed endpoint");
        }
        check(CombatDice.Roll(weapon with { BaseDamage = 1000000, Dice = 64, Sides = 1000000 }, rng) <= 65000000,
            "maximum configured roll stays in supported numeric domain");
        var biggest = CombatDamageRules.Resolve(65000000, AttackType.Pierce, ArmorType.Light, -1000000);
        check(biggest.Amount > 130000000 && biggest.Amount < 260000000, "maximum damage remains representable");

        Reject<ArgumentOutOfRangeException>(() => (weapon with { Dice = 65 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { Sides = 0 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { BaseDamage = -1 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { MaxRange = 1000001 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { MinRange = 1000 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { CooldownFrames = 0 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { WindupFrames = 31 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (weapon with { AttackType = (AttackType)99 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => CombatTiming.FramesFromSeconds(-1, 30), check);
        Reject<ArgumentOutOfRangeException>(() => CombatTiming.FramesFromSeconds(1, 0), check);
        Reject<ArgumentOutOfRangeException>(() => CombatTiming.FramesFromSeconds(3601, 30), check);
        Reject<ArgumentOutOfRangeException>(() => CombatDamageRules.Resolve(1, (AttackType)99, ArmorType.Light, 0), check);
        Reject<ArgumentOutOfRangeException>(() => CombatDamageRules.Resolve(1, AttackType.Normal, (ArmorType)99, 0), check);
        Reject<ArgumentOutOfRangeException>(() => CombatDamageRules.Resolve(-1, AttackType.Normal, ArmorType.Light, 0), check);
        Reject<ArgumentOutOfRangeException>(() => CombatDamageRules.Resolve(65000001, AttackType.Normal, ArmorType.Light, 0), check);
        Reject<ArgumentOutOfRangeException>(() => CombatDamageRules.Resolve(1, AttackType.Normal, ArmorType.Light, SimScalar.MinValue), check);
        before = rng.State;
        Reject<ArgumentOutOfRangeException>(() => CombatDice.Roll(weapon with { Dice = 65 }, rng), check);
        check(rng.State == before, "invalid public record copy is rejected before consuming RNG");
        var profile = new CombatDefinition(100, 0, ArmorType.Light, weapon);
        profile.Validate();
        Reject<ArgumentOutOfRangeException>(() => (profile with { MaxHealth = 0 }).Validate(), check);
        Reject<ArgumentOutOfRangeException>(() => (profile with { Armor = 1000001 }).Validate(), check);
        Reject<ArgumentNullException>(() => (profile with { Weapon = null! }).Validate(), check);
    }

    private static WeaponDefinition Weapon() => new(10, 2, 6, AttackType.Normal, 0, 90, 6, 30);

    private static void Reject<T>(Action action, Action<bool, string> check) where T : Exception
    {
        try { action(); }
        catch (T) { check(true, $"expected {typeof(T).Name}"); return; }
        check(false, $"expected {typeof(T).Name}");
    }
}
