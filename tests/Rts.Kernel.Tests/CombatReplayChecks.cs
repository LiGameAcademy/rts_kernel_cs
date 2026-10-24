using System.Text.Json;
using Rts.Kernel.Combat;

internal static class CombatReplayChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var savedFrame in new[] { 0, 1, 2, 3, 8, 11, 13, 20, 31 })
        {
            var original = Make();
            var replay = Make();
            while (original.Combat.Frame < savedFrame) original.Step();
            while (replay.Combat.Frame < savedFrame) replay.Step();
            var saved = original.Save();
            var clone = CombatReplayFixture.Load(saved, original.Combat.ReadDefinitions());
            check(original.Hash() == clone.Hash(), "host snapshot restores entity inputs, pending commands and admission counters");
            for (var frame = savedFrame + 1; frame <= 100; frame++)
            {
                if (frame == 40)
                {
                    original.Queue(43, 0, CombatCommandKind.Stop, 1);
                    clone.Queue(43, 0, CombatCommandKind.Stop, 1);
                    replay.Queue(43, 0, CombatCommandKind.Stop, 1);
                }
                var a = original.Step();
                var b = clone.Step();
                var c = replay.Step();
                check(original.Hash() == clone.Hash() && original.Hash() == replay.Hash()
                    && a.Events.SequenceEqual(b.Events) && a.Events.SequenceEqual(c.Events)
                    && a.Commands.SequenceEqual(b.Commands), "JSON resume and genesis command replay have identical state/events");
            }
            check(CombatReplayFixture.Load(saved, original.Combat.ReadDefinitions()).Save() == saved,
                "later execution cannot mutate a previously captured host save");
        }

        var fixture = Make();
        var root = JsonSerializer.Deserialize<ReplaySave>(fixture.Save(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        void Bad(ReplaySave bad)
        {
            try
            {
                CombatReplayFixture.Load(JsonSerializer.Serialize(bad, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    fixture.Combat.ReadDefinitions());
            }
            catch (InvalidDataException) { check(true, "invalid host queue/counter rejected"); return; }
            check(false, "host queue restore must reject");
        }
        Bad(root with { NextArrival = 0 });
        Bad(root with { NextSequence = 0 });
        Bad(root with { Pending = [root.Pending[0], root.Pending[0]] });
        Bad(root with { Pending = [root.Pending[0] with { Frame = 0 }] });
        Bad(root with { Pending = [root.Pending[0] with { Kind = (CombatCommandKind)99 }] });

        // An accepted future intent may legitimately refer to a target that dies before its due frame.
        var dead = new CombatReplayFixture(1);
        var profile = new CombatDefinition(1, 0, ArmorType.Light,
            new(1, 0, 1, AttackType.Normal, 0, 90, 0, 1));
        dead.Add(1, 0, 0, profile);
        dead.Add(2, 1, 32, profile);
        dead.Queue(1, 0, CombatCommandKind.Attack, 1, 2);
        dead.Queue(5, 0, CombatCommandKind.Attack, 1, 2);
        dead.Step();
        var restored = CombatReplayFixture.Load(dead.Save(), dead.Combat.ReadDefinitions());
        while (restored.Combat.Frame < 4) restored.Step();
        var refused = restored.Step();
        check(refused.Commands.Single().Accepted == false, "valid future command survives restore and is refused only at execution context");

        var born = new CombatReplayFixture(1);
        born.Step();
        born.Add(1, 0, 0, profile);
        var newborn = CombatReplayFixture.Load(born.Save(), born.Combat.ReadDefinitions());
        check(born.Hash() == newborn.Hash(), "next-frame newborn registration survives an intervening save");
    }

    private static CombatReplayFixture Make()
    {
        var host = new CombatReplayFixture(17);
        var profile = new CombatDefinition(1000, 0, ArmorType.Light,
            new(10, 2, 6, AttackType.Normal, 0, 90, 2, 10));
        host.Add(1, 0, 0, profile);
        host.Add(2, 1, 32, profile);
        host.Add(3, 1, 64, profile);
        host.Queue(1, 0, CombatCommandKind.Attack, 1, 2);
        host.Queue(1, 1, CombatCommandKind.Attack, 2, 1);
        host.Queue(4, 0, CombatCommandKind.Attack, 1, 3);
        host.Queue(12, 0, CombatCommandKind.Stop, 1);
        host.Queue(15, 0, CombatCommandKind.Attack, 1, 2);
        host.Queue(21, 1, CombatCommandKind.Stop, 2);
        host.Queue(25, 0, CombatCommandKind.Attack, 1, 3);
        return host;
    }
}
