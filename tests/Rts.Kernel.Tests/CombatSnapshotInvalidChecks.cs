using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatSnapshotInvalidChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var w = CombatTestWorld.Three();
        w.Step(w.Attack(1, 2));
        var s = w.Combat.CaptureSnapshot();
        var definitions = w.Combat.ReadDefinitions();
        var before = w.Combat.ComputeStateHash();
        void Bad(CombatSnapshot bad) => Reject(() => StationaryCombat.Restore(bad, definitions), check);
        CombatSnapshot Edit(Func<CombatUnitSnapshot, CombatUnitSnapshot> edit) =>
            s with { Units = s.Units.Select(u => u.Id == 1 ? edit(u) : u).ToArray() };

        Bad(s with { FormatVersion = 14 });
        Bad(s with { Frame = -1 });
        Bad(s with { NextStrikeId = 0 });
        Bad(s with { NextStrikeId = 1 });
        Bad(s with { NextEventSequence = 0 });
        Bad(s with { DefinitionsHash = new string('0', 64) });
        Bad(s with { Units = [] });
        Bad(s with { Units = [null!, s.Units[1], s.Units[2]] });
        Bad(s with { Units = [s.Units[0], s.Units[0], s.Units[2]] });
        Bad(Edit(u => u with { Health = -1 }));
        Bad(Edit(u => u with { Health = 101 }));
        Bad(Edit(u => u with { Target = 1 }));
        Bad(Edit(u => u with { Target = 999 }));
        Bad(Edit(u => u with { Stopped = true }));
        Bad(Edit(u => u with { StrikeId = s.NextStrikeId }));
        Bad(Edit(u => u with { DamageFrame = 1 }));
        Bad(Edit(u => u with { DamageFrame = 4 }));
        Bad(Edit(u => u with { NextAttackFrame = 10 }));
        Bad(Edit(u => u with { ActiveFromFrame = 0 }));
        Bad(Edit(u => u with { ActiveFromFrame = 3 }));
        Bad(Edit(u => u with { DamageFrame = null }));
        Reject(() => StationaryCombat.Restore(s, definitions.Select(d =>
            d.Id == 1 ? d with { Definition = d.Definition with { MaxHealth = 200 } } : d).ToArray()), check);
        Reject(() => StationaryCombat.Restore(s, [definitions[0], definitions[0], definitions[2]]), check);
        Reject(() => StationaryCombat.Restore(s, definitions.Select(d =>
            d.Id == 1 ? d with { Definition = d.Definition with { Weapon = d.Definition.Weapon with { Dice = 65 } } } : d).ToArray()), check);

        var json = CombatSnapshotJson.Serialize(s);
        Reject(() => CombatSnapshotJson.Deserialize(json.Replace("\"rngState\":17,", "")), check);
        Reject(() => CombatSnapshotJson.Deserialize(json.Replace("\"frame\":1", "\"frame\":1,\"frame\":2")), check);
        Reject(() => CombatSnapshotJson.Deserialize(json.Replace("\"health\":429496729600", "\"health\":1.5")), check);
        Reject(() => CombatSnapshotJson.Deserialize(json.Insert(json.Length - 1, ",\"unknown\":1")), check);
        Reject(() => CombatSnapshotJson.Deserialize("null"), check);
        Reject(() => CombatSnapshotJson.Deserialize(json.Replace("\"stopped\":false,", "")), check);
        Reject(() => CombatSnapshotJson.Deserialize(json.Replace("\"id\":1,", "\"id\":1,\"id\":1,")), check);
        check(w.Combat.ComputeStateHash() == before, "all rejected snapshots leave original match untouched");
    }

    private static void Reject(Action action, Action<bool, string> check)
    {
        try { action(); }
        catch (InvalidDataException) { check(true, "damaged snapshot rejected"); return; }
        catch (JsonException) { check(true, "damaged JSON rejected"); return; }
        check(false, "damaged snapshot must be rejected");
    }
}
