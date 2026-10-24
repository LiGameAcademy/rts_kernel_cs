using Rts.Kernel;
using Rts.Kernel.Combat;

internal static class CombatExecutionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Timers(check);
        StopAndRetarget(check);
        SimultaneousDeath(check);
        IsolationAndOrdering(check);
        CombatBoundaryChecks.Run(check);
        CombatLifecycleChecks.Run(check);
    }

    private static void Timers(Action<bool, string> check)
    {
        var w = CombatTestWorld.Three();
        var started = w.Step(w.Attack(1, 2));
        check(started.Events.Single().Kind == CombatEventKind.AttackStarted
            && w.Unit(1).DamageFrame == 3 && w.Unit(1).NextAttackFrame == 11,
            "frame one reserves full cooldown and damage deadline");
        w.Step(w.Attack(1, 2));
        check(w.Unit(1).DamageFrame == 3 && w.Unit(1).StrikeId == 1, "same target does not restart windup");
        var hit = w.Step();
        check(w.Unit(2).Health == 90 && w.Unit(1).DamageFrame is null, "exact damage frame, not a render callback");
        check(hit.Events.Select(e => e.Kind).SequenceEqual([CombatEventKind.StrikeResolved, CombatEventKind.DamageApplied]),
            "strike precedes damage event");
        w.Until(10);
        check(w.Unit(2).Health == 90, "remaining cooldown never deals additional damage");
        w.Step();
        check(w.Unit(1).DamageFrame == 13 && w.Unit(1).NextAttackFrame == 21, "next attack starts at reserved deadline");
        w.Until(13);
        check(w.Unit(2).Health == 80, "second strike has its own windup");

        w = CombatTestWorld.Three(windup: 0, cooldown: 1);
        var immediate = w.Step(w.Attack(1, 2));
        check(w.Unit(2).Health == 90 && immediate.Events.Count(e => e.Kind == CombatEventKind.StrikeResolved) == 1,
            "zero windup strikes once on command frame");
        w.Step();
        check(w.Unit(2).Health == 80, "one-frame cooldown never permits multiple strikes per frame");

        w = CombatTestWorld.Three(windup: 2, cooldown: 2);
        w.Step(w.Attack(1, 2));
        var boundary = w.Step();
        check(!boundary.Events.Any(e => e.Kind == CombatEventKind.StrikeResolved), "windup equal to cooldown does not strike early");
        boundary = w.Step();
        check(boundary.Events.Count(e => e.Kind == CombatEventKind.StrikeResolved) == 1
            && w.Unit(1).DamageFrame == 5, "completed strike can begin the next windup on the same deadline");
    }

    private static void StopAndRetarget(Action<bool, string> check)
    {
        var w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        w.Step(w.Attack(1, 3));
        check(w.Unit(1).Target == new EntityId(3) && w.Unit(1).DamageFrame is null
            && w.Unit(1).NextAttackFrame == 11, "retarget cancels unreleased strike without refunding cooldown");
        w.Until(10);
        check(w.Unit(2).Health == 100 && w.Unit(3).Health == 100, "retarget cannot borrow an old damage point");
        w.Step();
        check(w.Unit(1).DamageFrame == 13, "new target begins full windup after original cooldown");
        w.Until(13);
        check(w.Unit(3).Health == 90, "new target receives exactly one strike");

        w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        w.Step();
        w.Step(w.Stop(1));
        check(w.Unit(2).Health == 100 && w.Unit(1).Stopped && w.Unit(1).Target.IsNone,
            "Stop on damage frame cancels before collection");
        w.Step(w.Attack(1, 3));
        check(!w.Unit(1).Stopped && w.Unit(1).NextAttackFrame == 11, "new order replaces Stop but not cooldown");
        for (var frame = 5; frame <= 10; frame++)
            w.Step(w.Attack(1, frame % 2 == 0 ? 2UL : 3UL));
        check(w.Unit(2).Health == 100 && w.Unit(3).Health == 100 && w.Unit(1).NextAttackFrame == 11,
            "repeated target swapping cannot reset the attack deadline");
        w.Step();
        w.Until(13);
        check(w.Unit(2).Health == 90 && w.Unit(3).Health == 100, "only final selected target is hit after preserved cooldown");
        w.Step(w.Stop(1));
        w.Until(40);
        check(w.Unit(2).Health == 90 && w.Unit(1).Stopped, "Stop persists, no auto-retaliation or acquisition");

        w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2), w.Stop(1), w.Attack(1, 3));
        check(w.Unit(1).DamageFrame == 3 && w.Unit(1).Target == new EntityId(3),
            "commands in one batch finish before the first windup is committed");
        w.Step();
        w.Step(w.Attack(1, 2));
        check(w.Unit(2).Health == 100 && w.Unit(3).Health == 100, "retarget on damage frame cancels original hit");

        w = CombatTestWorld.Three(windup: 0);
        w.Step(w.Attack(1, 2));
        w.Step(w.Stop(1), w.Attack(1, 3));
        check(w.Unit(3).Health == 100 && w.Unit(1).NextAttackFrame == 11,
            "Stop after damage cannot refund released cooldown");
    }

    private static void SimultaneousDeath(Action<bool, string> check)
    {
        var w = new CombatTestWorld();
        w.Add(1, 0, 0, health: 10, damage: 10, windup: 0);
        w.Add(2, 1, 32, health: 10, damage: 10, windup: 0);
        var result = w.Step(w.Attack(2, 1, 1), w.Attack(1, 2));
        check(w.Unit(1).Health == 0 && w.Unit(2).Health == 0, "both valid lethal strikes survive same-frame death");
        check(result.Events.Count(e => e.Kind == CombatEventKind.UnitDied) == 2, "mutual kill produces exactly two deaths");
        check(result.Events.Where(e => e.Kind == CombatEventKind.StrikeResolved).Select(e => e.Attacker.Value).SequenceEqual([1UL, 2UL]),
            "simultaneous strikes are ordered by attacker ID");
        check(result.Events.Where(e => e.Kind == CombatEventKind.UnitDied).Select(e => e.Target.Value).SequenceEqual([1UL, 2UL]),
            "death cleanup has stable victim ordering");
        check(w.Unit(1).Target.IsNone && w.Unit(2).Target.IsNone, "dead attackers lose orders");
        check(!w.Step().Events.Any(), "dead units cannot strike or die again");

        w = new CombatTestWorld();
        w.Add(1, 0, 0, damage: 100, windup: 0);
        w.Add(2, 0, 1, damage: 100, windup: 0);
        w.Add(3, 1, 32, health: 10);
        result = w.Step(w.Attack(2, 3), w.Attack(1, 3));
        var damages = result.Events.Where(e => e.Kind == CombatEventKind.DamageApplied).ToArray();
        check(damages.Length == 2 && damages[0].AppliedDamage == 10 && damages[1].AppliedDamage == 0,
            "overkill preserves collected strike but clamps actual health removal");
        check(result.Events.Single(e => e.Kind == CombatEventKind.UnitDied).Attacker == new EntityId(1),
            "first deterministic lethal strike owns kill attribution");
        check(w.Unit(1).Target.IsNone && w.Unit(2).Target.IsNone, "all surviving attackers drop a dead target");

        w = new CombatTestWorld();
        w.Add(1, 0, 0, health: 10, windup: 3);
        w.Add(2, 1, 32, health: 100, damage: 10, windup: 1);
        w.Step(w.Attack(1, 2), w.Attack(2, 1, 1));
        w.Step();
        w.Until(10);
        check(w.Unit(1).Health == 0 && w.Unit(2).Health == 100, "death before damage frame cancels a later windup");
    }

    private static void IsolationAndOrdering(Action<bool, string> check)
    {
        var a = CombatTestWorld.Three();
        var b = CombatTestWorld.Three();
        var aRead = a.Combat.ReadCombatants();
        a.Step(a.Attack(1, 2));
        a.Until(3);
        check(aRead.Single(u => u.Id.Value == 2).Health == 100 && b.Unit(2).Health == 100,
            "detached reads and another match never observe mutable state leakage");
        check(b.Combat.Frame == 0 && b.Combat.RngState == 17, "match clocks and RNG are instance-owned");
        var oldResult = a.Step();
        a.Until(13);
        check(oldResult.Frame == 4 && oldResult.Events.Count == 0, "retained frame result is detached");

        a = CombatTestWorld.Three();
        b = CombatTestWorld.Three();
        b.Entities.Reverse();
        var commandA = a.Attack(1, 2);
        var commandB = b.Attack(1, 2);
        for (var frame = 1; frame <= 80; frame++)
        {
            var ar = a.Step(frame == 1 ? [commandA] : []);
            var br = b.Step(frame == 1 ? [commandB] : []);
            check(a.Combat.ReadCombatants().SequenceEqual(b.Combat.ReadCombatants())
                && ar.Events.SequenceEqual(br.Events) && a.Combat.RngState == b.Combat.RngState,
                "input entity iteration order does not change state or event ordering");
        }
    }
}
