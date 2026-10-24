using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Combat;
using Rts.Kernel.Numerics;

internal static class CombatProfileChecks
{
    internal static void Run(Action<bool, string> check, int count)
    {
        if (count is not (24 or 300 or 500)) throw new ArgumentOutOfRangeException(nameof(count));
        const int frames = 600;
        var combat = new StationaryCombat(253);
        var entities = new EntityState[count];
        var commands = new CombatCommand[count];
        var definition = new CombatDefinition(1000000, 0, ArmorType.Light,
            new WeaponDefinition(10, 1, 6, AttackType.Normal, 0, 90, 6, 30));
        for (var i = 0; i < count; i++)
        {
            var id = new EntityId((ulong)i + 1);
            combat.AddCombatant(id, definition);
            var owner = i % 2;
            entities[i] = new(id, owner, new(i / 2 * 256 + owner * 32, 0), FixedVector2.Zero);
            commands[i] = new(owner, i, CombatCommandKind.Attack, id, new((ulong)(i ^ 1) + 1));
        }
        var unchanged = entities.ToArray();
        var samples = new double[frames];
        using var stateTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var eventTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var strikes = 0;
        var deaths = 0;
        for (var frame = 1; frame <= frames; frame++)
        {
            var start = Stopwatch.GetTimestamp();
            var result = combat.Advance(frame, entities, frame == 1 ? commands : []);
            samples[frame - 1] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            check(result.Commands.All(c => c.Accepted), "all issued profile commands accepted");
            strikes += result.Events.Count(e => e.Kind == CombatEventKind.StrikeResolved);
            deaths += result.Events.Count(e => e.Kind == CombatEventKind.UnitDied);
            // Fingerprints only, outside the timed interval; not a supported save format.
            stateTrace.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { combat.Frame, combat.RngState, Units = combat.ReadCombatants() }));
            eventTrace.AppendData(JsonSerializer.SerializeToUtf8Bytes(result.Events));
        }
        check(strikes == count * 20 && deaths == 0, "all paired attackers execute twenty strikes without a death");
        check(entities.SequenceEqual(unchanged), "combat does not mutate positions or entity ownership");
        var sorted = samples.Order().ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "stationary_pairs", count, frames, runtime = Environment.Version.ToString(),
            state_trace_sha256 = Convert.ToHexString(stateTrace.GetHashAndReset()),
            event_trace_sha256 = Convert.ToHexString(eventTrace.GetHashAndReset()),
            combat.RngState, strikes, deaths,
            first_ms = samples[0], max_ms = sorted[^1], p95_ms = sorted[(int)(frames * .95) - 1],
            over_budget = samples.Count(ms => ms > 1000.0 / 30), samples_ms = samples
        }));
    }
}

