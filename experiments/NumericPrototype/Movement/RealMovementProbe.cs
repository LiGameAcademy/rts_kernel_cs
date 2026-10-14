using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NumericPrototype.Movement;

internal static class RealMovementProbe
{
    internal sealed record ScenarioResult(string Outcome, int Frames, FixedPoint Position, int BlockedFrames, int Events, string TraceHash);
    internal sealed record ProbeResult(string WorldIdentity, string UnitDataSha256, string BalanceSha256,
        string UnitId, long SpeedRaw, long RadiusRaw, long TurnRateRaw, FixedPoint Start, FixedPoint Goal,
        int PathWaypoints, int CandidateRoutesTried, int FailedMovingCandidates, long TerrainRiseRaw, ScenarioResult Moving, ScenarioResult Blocked);

    internal static ProbeResult Run(string assetsDirectory)
    {
        var world = new MovementWorld(File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/pathing.json")),
            File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/terrain-heightfield.json")));
        var unitBytes = File.ReadAllBytes(Path.Combine(assetsDirectory, "slk-exported/Units/UnitData.json"));
        var balanceBytes = File.ReadAllBytes(Path.Combine(assetsDirectory, "slk-exported/Units/UnitBalance.json"));
        using var units = JsonDocument.Parse(unitBytes);
        using var balance = JsonDocument.Parse(balanceBytes);
        var unit = units.RootElement.GetProperty("records").EnumerateArray().Single(v => v.GetProperty("unitID").GetString() == "hfoo");
        var definition = balance.RootElement.GetProperty("records").EnumerateArray().Single(v => v.GetProperty("unitBalanceID").GetString() == "hfoo");
        var speed = MovementWorld.Number(definition, "spd");
        var radius = MovementWorld.Number(definition, "collision");
        var turnRate = MovementWorld.Number(unit, "turnRate");
        // Stable scan selects a traversable sample with nonzero endpoint elevation difference.
        var attempts = 0;
        var movingFailures = 0;
        for (var y = 1; y < world.Height - 1; y++)
        for (var x = 1; x < world.Width - 13; x++)
        {
            var start = world.Center(x, y);
            var goal = world.Center(x + 12, y);
            if (!CollisionOracle.Clear(world, start, start, radius) || !CollisionOracle.Clear(world, goal, goal, radius)
                || world.Sample(start) == world.Sample(goal)) continue;
            var path = IntegerPathfinder.Find(world, start, goal, radius);
            if (path.Length < 12) continue;
            if (++attempts > 128) throw new InvalidOperationException("No successful sample in the bounded real-map search.");
            if (path.Any(p => !CollisionOracle.Clear(world, p, p, radius))) continue;
            var moving = Trace(world, start, goal, speed, radius, turnRate, null);
            if (moving.Outcome != nameof(MotionStatus.Arrived)) { movingFailures++; continue; }
            var blocker = path[path.Length / 2];
            var blocked = Trace(world, start, goal, speed, radius, turnRate, blocker);
            if (blocked.Outcome != nameof(MotionStatus.Failed)) throw new InvalidOperationException("Expected body blockage was not observed.");
            return new(world.Identity, Hash(unitBytes), Hash(balanceBytes), "hfoo", speed, radius, turnRate,
                start, goal, path.Length, attempts, movingFailures, world.Sample(goal) - world.Sample(start), moving, blocked);
        }
        throw new InvalidOperationException("No appropriate real-map route found; no synthetic fallback is substituted.");
    }

    private static ScenarioResult Trace(MovementWorld world, FixedPoint start, FixedPoint goal,
        long speed, long radius, long turnRate, FixedPoint? blocker)
    {
        MovementSimulation Create() => new(world, start, goal, speed, radius, turnRate, blocker, radius);
        var a = Create();
        var b = Create();
        var trace = new StringBuilder();
        var eventCount = 0;
        for (var frame = 1; frame <= 900; frame++)
        {
            if (frame % 13 == 0) b.Restore(a.Capture());
            var events = a.Step();
            var restoredEvents = b.Step();
            if (a.Capture() != b.Capture() || JsonSerializer.Serialize(events) != JsonSerializer.Serialize(restoredEvents))
                throw new InvalidOperationException("Real-map per-frame restore diverged.");
            eventCount += events.Length;
            trace.Append(a.Capture()).Append(JsonSerializer.Serialize(events));
            if (a.State.Status != MotionStatus.Moving)
            {
                if (eventCount != 1 || a.State.Status == MotionStatus.Arrived && a.State.Position != goal)
                    throw new InvalidOperationException("Real-map terminal event/position mismatch.");
                return new(a.State.Status.ToString(), frame, a.State.Position, a.State.BlockedFrames, eventCount, Hash(Encoding.UTF8.GetBytes(trace.ToString())));
            }
        }
        return new("FrameLimit", 900, a.State.Position, a.State.BlockedFrames, eventCount, Hash(Encoding.UTF8.GetBytes(trace.ToString())));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
