using Rts.Kernel;
using Rts.Kernel.Navigation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>Frame-local holds must not force a detour once preceding reservations have cleared.</summary>
internal static class StaleHoldChecks
{
    private static readonly PathingGrid Grid = new(128, 128, 32, default, new byte[128 * 128]);
    private static readonly TerrainHeights Heights = new(129, 129, 32, default, new SimScalar[129 * 129]);
    private static readonly MovementDefinition Footman = new(1, 270, 31,
        new MotionParameters(TurnRate: SimScalar.Parse("0.6")));

    internal static void Run(Action<bool, string> check)
    {
        Scenario(check, "same-east", [new(3056, 2064)]);
        Scenario(check, "same-nw", [new(1200, 3000)]);
        Scenario(check, "reverse-nw-se", [new(1200, 3000), new(3000, 1200), new(1200, 3000),
            new(3000, 1200), new(1200, 3000)]);
    }

    private static void Scenario(Action<bool, string> check, string label, FixedVector2[] goals)
    {
        var match = OpenGroundPartyChecks.Create();
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        var lastCommand = 31 * (goals.Length - 1);
        var completed = 0;
        var failed = 0;
        var frames = 0;
        using var trace = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (; frames < lastCommand + 600 && (frames <= lastCommand || completed < 24); frames++)
        {
            if (frames % 31 == 0 && frames / 31 < goals.Length)
            {
                var index = frames / 31;
                check(match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25 + index,
                    new(ids, goals[index]))).Accepted, $"{label}: replacement accepted");
                completed = 0;
                failed = 0;
            }
            RtsMatch? resumed = null;
            if (frames % 37 == 0)
                resumed = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(match.CaptureSnapshot())),
                    Grid, Heights, [Footman]);
            var before = match.Entities.ToArray();
            match.Step();
            var events = match.DrainEvents();
            trace.AppendData(Encoding.UTF8.GetBytes(match.ComputeStateHash() + "\n" + JsonSerializer.Serialize(events) + "\n"));
            if (resumed is not null)
            {
                resumed.Step();
                check(match.ComputeStateHash() == resumed.ComputeStateHash(), $"{label}: JSON restored state");
                check(JsonSerializer.Serialize(events) == JsonSerializer.Serialize(resumed.DrainEvents()),
                    $"{label}: JSON restored events");
            }
            var after = match.Entities.ToArray();
            for (var i = 0; i < after.Length; i++)
            {
                check((after[i].Position - before[i].Position).Length <= 10, $"{label}: bounded movement");
                for (var j = i + 1; j < after.Length; j++)
                    check(!CrowdGeometry.Overlap(after[i].Position, after[i].Position, Footman.Radius,
                        after[j].Position, after[j].Position, Footman.Radius), $"{label}: no overlap after recovery");
            }
            if (label == "same-east" && match.Frame == 44)
            {
                // First stale hold in this fixture: a preceding reservation has already changed.
                // Unit24's original timed proposal is safe, so it must retain its forward step.
                var unit = after.Single(entity => entity.Id.Value == 24);
                check(unit.Position == FixedVector2.FromRaw(9701690230084, 9230976263472),
                    "cleared hold commits its original proposal instead of a sideways detour");
            }
            completed += events.Count(item => item.Kind == MatchEventKind.MoveCompleted);
            failed += events.Count(item => item.Kind is MatchEventKind.MoveFailed or MatchEventKind.CommandRejected);
        }
        check(completed == 24 && failed == 0, $"{label}: all24 complete without rejection or timeout");
        var settled = match.Entities.ToArray();
        for (var idle = 0; idle < 90; idle++)
        {
            match.Step();
            check(settled.SequenceEqual(match.Entities), $"{label}: settled units remain stationary");
            check(match.DrainEvents().Count == 0, $"{label}: no repeated completion events");
        }
        Console.WriteLine($"Stale hold {label}: 24/24; frames={frames}; trace={Convert.ToHexString(trace.GetHashAndReset())}");
    }
}
