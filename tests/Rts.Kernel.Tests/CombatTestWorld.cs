using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal sealed class CombatTestWorld
{
    internal StationaryCombat Combat { get; } = new(17);
    internal List<EntityState> Entities { get; } = [];
    private long _sequence;

    internal void Add(ulong id, int owner, int x, int health = 100, int damage = 10,
        int windup = 2, int cooldown = 10, int dice = 0)
    {
        Combat.AddCombatant(new(id), new CombatDefinition(health, 0, ArmorType.Light,
            new WeaponDefinition(damage, dice, 6, AttackType.Normal, 0, 90, windup, cooldown)));
        Entities.Add(new(new(id), owner, new(x, 0), FixedVector2.Zero));
    }

    internal CombatCommand Attack(ulong id, ulong target, int player = 0) =>
        new(player, _sequence++, CombatCommandKind.Attack, new(id), new(target));
    internal CombatCommand Stop(ulong id, int player = 0) =>
        new(player, _sequence++, CombatCommandKind.Stop, new(id));

    internal CombatStepResult Step(params CombatCommand[] commands) =>
        Combat.Advance(Combat.Frame + 1, Entities, commands);

    internal CombatantState Unit(ulong id) => Combat.ReadCombatants().Single(u => u.Id.Value == id);
    internal void Until(long frame) { while (Combat.Frame < frame) Step(); }

    internal static CombatTestWorld Three(int windup = 2, int cooldown = 10)
    {
        var world = new CombatTestWorld();
        world.Add(1, 0, 0, windup: windup, cooldown: cooldown);
        world.Add(2, 1, 32);
        world.Add(3, 1, 64);
        return world;
    }
}
