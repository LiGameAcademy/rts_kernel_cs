using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel.Combat;

internal static class CombatResumeProfileChecks
{
    private const int Frames = 600;

    internal static void Create(int count, string destination)
    {
        var started = Stopwatch.GetTimestamp();
        var host = Make(count);
        host.Step();
        host.Step();
        var json = host.Save();
        Directory.CreateDirectory(destination);
        var path = Path.Combine(destination, $"resume-{count}.json");
        File.WriteAllText(path, json);
        var creationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        using var stateTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var eventTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < Frames; i++)
        {
            var result = host.Step();
            Append(host, result.Events, stateTrace, eventTrace);
        }
        var expected = new ResumeExpected(Convert.ToHexString(stateTrace.GetHashAndReset()), Convert.ToHexString(eventTrace.GetHashAndReset()));
        File.WriteAllText(path + ".expected.json", JsonSerializer.Serialize(expected));
        Console.WriteLine(JsonSerializer.Serialize(new { scenario = "resume_fixture_created", count, creation_ms = creationMs, expected }));
    }

    internal static void Run(Action<bool, string> check, int count, string path)
    {
        var definitions = Definitions(count);
        var json = File.ReadAllText(path);
        var expected = JsonSerializer.Deserialize<ResumeExpected>(File.ReadAllText(path + ".expected.json"))!;
        var restoreStarted = Stopwatch.GetTimestamp();
        var host = CombatReplayFixture.Load(json, definitions);
        var restoreMs = Stopwatch.GetElapsedTime(restoreStarted).TotalMilliseconds;
        check(host.Combat.Frame == 2, "frozen input is before the first damage frame");
        var samples = new double[Frames];
        var strikes = 0;
        using var stateTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var eventTrace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < Frames; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = host.Step();
            samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            check(result.Commands.All(command => command.Accepted), "pending identical target commands remain valid after restore");
            strikes += result.Events.Count(e => e.Kind == CombatEventKind.StrikeResolved);
            Append(host, result.Events, stateTrace, eventTrace);
        }
        var state = Convert.ToHexString(stateTrace.GetHashAndReset());
        var events = Convert.ToHexString(eventTrace.GetHashAndReset());
        var sorted = samples.Order().ToArray();
        var matches = state == expected.State && events == expected.Events;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "stationary_resume", count, frames = Frames, restored_frame = 2,
            runtime = Environment.Version.ToString(), restore_ms = restoreMs,
            first_ms = samples[0], max_ms = sorted[^1], p95_ms = sorted[(int)(Frames * .95) - 1],
            over_budget = samples.Count(ms => ms > 1000.0 / 30), expected_match = matches,
            state_trace_sha256 = state, event_trace_sha256 = events, strikes, samples_ms = samples
        }));
        check(matches && strikes == count * 20, "cold resumed suffix matches uninterrupted state/events and strike count");
    }

    private static void Append(CombatReplayFixture host, IReadOnlyList<CombatEvent> events, IncrementalHash state, IncrementalHash eventTrace)
    {
        state.AppendData(Encoding.UTF8.GetBytes(host.Hash()));
        eventTrace.AppendData(JsonSerializer.SerializeToUtf8Bytes(events));
    }

    private static CombatReplayFixture Make(int count)
    {
        var host = new CombatReplayFixture(253);
        foreach (var definition in Definitions(count))
        {
            var index = (int)definition.Id - 1;
            var owner = index % 2;
            host.Add(definition.Id, owner, index / 2 * 256 + owner * 32, definition.Definition);
            var target = (ulong)(index ^ 1) + 1;
            host.Queue(1, owner, CombatCommandKind.Attack, definition.Id, target);
            host.Queue(3, owner, CombatCommandKind.Attack, definition.Id, target);
        }
        return host;
    }

    private static IReadOnlyList<CombatDefinitionEntry> Definitions(int count)
    {
        if (count is not (24 or 300 or 500)) throw new ArgumentOutOfRangeException(nameof(count));
        var definition = new CombatDefinition(1000000, 0, ArmorType.Light,
            new(10, 1, 6, AttackType.Normal, 0, 90, 6, 30));
        return Enumerable.Range(1, count).Select(id => new CombatDefinitionEntry((ulong)id, definition)).ToArray();
    }
}

internal sealed record ResumeExpected(string State, string Events);
