using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatLifecycleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var world = new CombatTestWorld();
        world.Add(1, 0, 0, health: 10000, damage: 10, windup: 2, cooldown: 10, dice: 2);
        world.Add(2, 1, 32, health: 10000);
        world.Add(3, 1, 64, health: 10000);
        world.Step(world.Attack(1, 2));
        var rngBefore = world.Combat.RngState;
        world.Step(world.Attack(1, 1));
        check(world.Combat.RngState == rngBefore, "rejected self-target with dice does not consume RNG");
        var expectedRng = new DeterministicRng(rngBefore);
        var roll = CombatDice.Roll(world.Unit(1).Definition.Weapon, expectedRng);
        world.Step();
        check(world.Unit(2).Health == (SimScalar)10000 - roll && world.Combat.RngState == expectedRng.State,
            "first committed strike consumes the expected two dice in order");
        for (var i = 0; i < 20; i++) _ = world.Combat.ReadCombatants();
        check(world.Combat.RngState == expectedRng.State, "observation cannot affect future damage");

        long previousStart = 1;
        for (var frame = 4; frame <= 200; frame++)
        {
            var result = world.Step(world.Stop(1), world.Attack(1, frame % 2 == 0 ? 2UL : 3UL));
            foreach (var started in result.Events.Where(e => e.Kind == CombatEventKind.AttackStarted))
            {
                check(started.Frame - previousStart >= 10, "adversarial same-frame Stop and retarget never reset cooldown");
                previousStart = started.Frame;
            }
        }

        world = new CombatTestWorld();
        world.Add(1, 0, 0, windup: 2, dice: 2);
        world.Add(2, 1, 32);
        world.Step(world.Attack(1, 2));
        world.Step();
        rngBefore = world.Combat.RngState;
        world.Entities[1] = world.Entities[1] with { Position = new(100, 0) };
        world.Step();
        check(world.Combat.RngState == rngBefore && world.Unit(2).Health == 100,
            "target leaving on damage frame cancels before any dice are rolled");

        var stage = new StationaryCombat(1);
        var definition = new CombatDefinition(100, 0, ArmorType.Light,
            new WeaponDefinition(0, 0, 1, AttackType.Normal, 32, 90, 0, 1));
        stage.AddCombatant(new(1), definition);
        stage.AddCombatant(new(2), definition);
        EntityState[] view = [new(new(1), 0, new(0, 0), FixedVector2.Zero),
            new(new(2), 1, new(32, 0), FixedVector2.Zero)];
        var atMinimum = stage.Advance(1, view, [new(0, 0, CombatCommandKind.Attack, new(1), new(2))]);
        check(atMinimum.Commands.Single().Accepted && atMinimum.Events.Single(e => e.Kind == CombatEventKind.DamageApplied).AppliedDamage == 0,
            "minimum range is inclusive and zero damage never kills");
        view[1] = view[1] with { Position = FixedVector2.FromRaw(32 * SimScalar.Scale - 1, 0) };
        check(stage.Advance(2, view, []).Events.Single().Reason == CombatReason.TargetOutOfRange,
            "one raw unit below minimum range ends current order");
        check(stage.ReadCombatants().All(u => u.Health == 100), "zero strikes preserve both health values");

        world = CombatTestWorld.Three();
        world.Step(world.Attack(1, 2));
        world.Entities.RemoveAt(0);
        check(world.Step().Events.Single().Reason == CombatReason.AttackerMissing, "disappeared attacker loses pending intent");
        world = CombatTestWorld.Three(windup: 0);
        world.Step(world.Attack(1, 2));
        var stopped = world.Step(world.Stop(1));
        var original = stopped.Events.ToArray();
        world.Step(world.Attack(1, 3));
        check(stopped.Events.SequenceEqual(original), "subsequent frames cannot mutate retained events");
        try
        {
            ((IList<CombatEvent>)stopped.Events)[0] = original[0];
            check(false, "events must not expose mutable storage");
        }
        catch (NotSupportedException) { check(true, "events must not expose mutable storage"); }

        world = new CombatTestWorld();
        world.Add(1, 0, 0, health: 1, damage: 1, windup: 0);
        world.Add(2, 1, 32, health: 1, damage: 1, windup: 0);
        world.Step(world.Attack(1, 2), world.Attack(2, 1, 1));
        var rejection = world.Step(world.Attack(1, 2), world.Stop(2, 1));
        check(rejection.Commands.All(c => !c.Accepted && c.Reason == CombatReason.DeadAttacker),
            "dead entities cannot receive either attack or Stop orders");
        check(!rejection.Events.Any(e => e.Kind == CombatEventKind.UnitDied), "rejected dead orders cannot duplicate deaths");
    }
}
