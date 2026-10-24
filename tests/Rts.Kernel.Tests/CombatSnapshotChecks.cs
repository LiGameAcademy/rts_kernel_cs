using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatSnapshotChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var savedFrame in new[] { 0, 1, 2, 3, 4, 10, 11, 12, 13, 22, 40 })
        {
            var world = CombatTestWorld.Three();
            for (var frame = 1; frame <= savedFrame; frame++) Step(world.Combat, world.Entities, frame);
            var before = world.Combat.ComputeStateHash();
            var snapshot = world.Combat.CaptureSnapshot();
            var json = CombatSnapshotJson.Serialize(snapshot);
            var restored = StationaryCombat.Restore(CombatSnapshotJson.Deserialize(json), world.Combat.ReadDefinitions());
            check(world.Combat.ComputeStateHash() == before && restored.ComputeStateHash() == before,
                $"save/restore frame {savedFrame} is pure and exact");
            for (var frame = savedFrame + 1; frame <= 60; frame++)
            {
                var a = Step(world.Combat, world.Entities, frame);
                var b = Step(restored, world.Entities, frame);
                check(world.Combat.ComputeStateHash() == restored.ComputeStateHash() && a.Events.SequenceEqual(b.Events)
                    && a.Commands.SequenceEqual(b.Commands), $"restored frame {savedFrame} continues exactly at {frame}");
            }
        }

        var killed = new CombatTestWorld();
        killed.Add(1, 0, 0, health: 1, damage: 1, windup: 0);
        killed.Add(2, 1, 32, health: 1, damage: 1, windup: 0);
        killed.Step(killed.Attack(1, 2), killed.Attack(2, 1, 1));
        var dead = StationaryCombat.Restore(CombatSnapshotJson.Deserialize(CombatSnapshotJson.Serialize(killed.Combat.CaptureSnapshot())),
            killed.Combat.ReadDefinitions());
        check(!dead.Advance(2, killed.Entities, []).Events.Any(), "death restore does not replay death or damage");
        check(dead.ReadCombatants().All(u => !u.Alive), "death tombstones survive restore");

        var random = new CombatTestWorld();
        random.Add(1, 0, 0, health: 10000, dice: 2);
        random.Add(2, 1, 32, health: 10000, dice: 2);
        random.Step(random.Attack(1, 2), random.Attack(2, 1, 1));
        var clone = StationaryCombat.Restore(random.Combat.CaptureSnapshot(), random.Combat.ReadDefinitions());
        for (var frame = 2; frame <= 100; frame++)
        {
            var a = random.Combat.Advance(frame, random.Entities, []);
            var b = clone.Advance(frame, random.Entities, []);
            check(random.Combat.ComputeStateHash() == clone.ComputeStateHash() && a.Events.SequenceEqual(b.Events),
                "dice state and event/strike identities continue through restore");
        }
        check(random.Combat.RngState != 17, "random continuation test actually consumed dice");

        var pristine = CombatTestWorld.Three();
        var saved = pristine.Combat.CaptureSnapshot();
        var read = pristine.Combat.CaptureSnapshot();
        pristine.Step(pristine.Attack(1, 2));
        check(CombatSnapshotJson.Serialize(saved) == CombatSnapshotJson.Serialize(read), "captured snapshot is detached from live state");
        var reversed = saved with { Units = saved.Units.Reverse().ToArray() };
        check(CombatSnapshotJson.Serialize(saved) == CombatSnapshotJson.Serialize(reversed),
            "canonical wire ordering ignores source collection order");
        check(StationaryCombat.Restore(reversed, pristine.Combat.ReadDefinitions().Reverse().ToArray()).ComputeStateHash()
            == StationaryCombat.Restore(saved, pristine.Combat.ReadDefinitions()).ComputeStateHash(),
            "catalog and unit enumeration order do not affect canonical state");

        foreach (var timing in new[] { (Windup: 0, Cooldown: 1), (Windup: 2, Cooldown: 2) })
        {
            var boundary = CombatTestWorld.Three(timing.Windup, timing.Cooldown);
            for (var frame = 1; frame <= 30; frame++)
            {
                var resumed = StationaryCombat.Restore(boundary.Combat.CaptureSnapshot(), boundary.Combat.ReadDefinitions());
                CombatCommand[] input = frame == 1 ? [new(0, 1, CombatCommandKind.Attack, new(1), new(2))] : [];
                var a = boundary.Combat.Advance(frame, boundary.Entities, input);
                var b = resumed.Advance(frame, boundary.Entities, input);
                check(boundary.Combat.ComputeStateHash() == resumed.ComputeStateHash() && a.Events.SequenceEqual(b.Events),
                    "zero/equal windup boundaries and eventual death remain restorable at every frame");
            }
        }
        var metadata = pristine.Combat.CaptureSnapshot();
        var eventChanged = metadata with { NextEventSequence = metadata.NextEventSequence + 1 };
        var randomChanged = metadata with { RngState = metadata.RngState ^ 1 };
        check(CombatSnapshotJson.Serialize(metadata) != CombatSnapshotJson.Serialize(eventChanged)
            && CombatSnapshotJson.Serialize(metadata) != CombatSnapshotJson.Serialize(randomChanged),
            "canonical state includes future event identities and RNG, not only visible health");

        CombatSnapshotInvalidChecks.Run(check);
        CombatReplayChecks.Run(check);
    }

    private static CombatStepResult Step(StationaryCombat combat, IReadOnlyList<EntityState> entities, int frame)
    {
        CombatCommand[] commands = frame switch
        {
            1 => [new(0, frame, CombatCommandKind.Attack, new(1), new(2))],
            4 => [new(0, frame, CombatCommandKind.Attack, new(1), new(3))],
            12 => [new(0, frame, CombatCommandKind.Stop, new(1))],
            15 => [new(0, frame, CombatCommandKind.Attack, new(1), new(2))],
            _ => []
        };
        return combat.Advance(frame, entities, commands);
    }
}
