using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatBoundaryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        w.Entities[2] = w.Entities[2] with { Position = new(1000, 0) };
        var rejected = w.Step(w.Attack(1, 3), w.Stop(1, player: 1));
        check(rejected.Commands.All(c => !c.Accepted)
            && w.Unit(1).Target == new EntityId(2) && w.Unit(1).DamageFrame == 3,
            "out of range and ownership rejection preserve old attack");
        w.Step();
        check(w.Unit(2).Health == 90, "rejected retarget does not cancel due old strike");
        w.Entities[1] = w.Entities[1] with { Position = new(91, 0) };
        var ended = w.Step();
        check(w.Unit(1).Target.IsNone && ended.Events.Any(e => e.Reason == CombatReason.TargetOutOfRange),
            "target leaving range ends attack without pursuing");
        w.Entities[1] = w.Entities[1] with { Position = new(32, 0) };
        w.Until(20);
        check(w.Unit(2).Health == 90, "returning target does not restart ended order");

        w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        var saved = w.Unit(1);
        var rng = w.Combat.RngState;
        var invalid = new CombatCommand(0, 3, (CombatCommandKind)99, new(1), new(2));
        rejected = w.Step(invalid, w.Attack(1, 1), w.Attack(1, 999), w.Attack(2, 1));
        check(rejected.Commands.All(c => !c.Accepted) && w.Unit(1) == saved && w.Combat.RngState == rng,
            "invalid, self, missing and unowned commands cannot mutate combat state or RNG");
        var units = w.Combat.ReadCombatants();
        try { ((IList<CombatantState>)units)[0] = units[0]; check(false, "read collection is immutable"); }
        catch (NotSupportedException) { check(true, "read collection is immutable"); }

        var frame = w.Combat.Frame;
        var before = w.Combat.ReadCombatants().ToArray();
        Reject(() => w.Combat.Advance(frame, w.Entities, []), check);
        Reject(() => w.Combat.Advance(frame + 2, w.Entities, []), check);
        var corrupt = w.Entities.Concat([w.Entities[0]]).ToArray();
        Reject(() => w.Combat.Advance(frame + 1, corrupt, []), check);
        var moving = w.Entities.ToArray();
        moving[0] = moving[0] with { Velocity = new(1, 0) };
        Reject(() => w.Combat.Advance(frame + 1, moving, []), check);
        var unbounded = w.Entities.ToArray();
        unbounded[0] = unbounded[0] with { Position = new(SimScalar.MaxValue, 0) };
        Reject(() => w.Combat.Advance(frame + 1, unbounded, []), check);
        check(w.Combat.Frame == frame && w.Combat.ReadCombatants().SequenceEqual(before)
            && w.Combat.RngState == rng, "invalid frame input is rejected before any mutation");
        Reject(() => w.Combat.AddCombatant(new(1), w.Unit(1).Definition), check);
        Reject(() => w.Combat.AddCombatant(EntityId.None, w.Unit(1).Definition), check);

        w = CombatTestWorld.Three();
        w.Entities[1] = w.Entities[1] with { Position = new(90, 0) };
        check(w.Step(w.Attack(1, 2)).Commands.Single().Accepted, "maximum range contact is inclusive");
        w = CombatTestWorld.Three();
        w.Entities[1] = w.Entities[1] with { Position = FixedVector2.FromRaw(90 * SimScalar.Scale + 1, 0) };
        check(!w.Step(w.Attack(1, 2)).Commands.Single().Accepted, "one raw unit outside range is rejected");

        w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        w.Entities.RemoveAt(1);
        check(w.Step().Events.Any(e => e.Reason == CombatReason.TargetLost) && w.Unit(1).Target.IsNone,
            "disappeared target ends order before its old damage frame");

        w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        w.Entities[1] = w.Entities[1] with { OwnerId = 0 };
        check(w.Step().Events.Any(e => e.Reason == CombatReason.InvalidTarget), "allegiance changes revalidate existing intent");

        w = CombatTestWorld.Three(windup: 0);
        w.Step(w.Attack(1, 2));
        w.Combat.AddCombatant(new(4), new(100, 0, ArmorType.Light, new(10, 0, 1, AttackType.Normal, 0, 90, 0, 1)));
        w.Entities.Add(new(new(4), 0, new(1, 0), FixedVector2.Zero));
        check(w.Step(w.Attack(4, 2)).Commands.Single().Accepted && w.Unit(2).Health == 80,
            "unit registered between frames first participates on the following frame");
    }

    private static void Reject(Action action, Action<bool, string> check)
    {
        try { action(); }
        catch (ArgumentException) { check(true, "invalid API input rejected"); return; }
        check(false, "invalid API input must reject");
    }
}
