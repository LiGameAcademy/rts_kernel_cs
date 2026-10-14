using System.Text.Json;
using NumericPrototype.Movement;

namespace NumericPrototype.Groups;

internal static class RealGroupProbe
{
    internal sealed record Result(string Scope, string UnitId, long RadiusRaw, int Checks,
        GroupPlanningChecks.ProbeResult Group);

    internal static Result Run(string assetsDirectory)
    {
        var world = new MovementWorld(File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/pathing.json")),
            File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/terrain-heightfield.json")));
        using var balance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(assetsDirectory, "slk-exported/Units/UnitBalance.json")));
        var definition = balance.RootElement.GetProperty("records").EnumerateArray()
            .Single(value => value.GetProperty("unitBalanceID").GetString() == "hfoo");
        var radius = MovementWorld.Number(definition, "collision");
        if (radius <= 0 || radius > world.CellSize) throw new ArgumentException("Real-group sample radius outside audited range.");
        // Stable scan chooses distinct body-clear positions, separated by at least two grid cells.
        // These are diagnostic input positions/goals, not evidence that formation generation has migrated.
        var points = new List<FixedPoint>();
        for (var y = 2; y < world.Height - 2 && points.Count < 140; y += 2)
        for (var x = 2; x < world.Width - 2 && points.Count < 140; x += 2)
        {
            var point = world.Center(x, y);
            if (CollisionOracle.Clear(world, point, point, radius)) points.Add(point);
        }
        if (points.Count < 140) throw new InvalidOperationException("Not enough real-map sample positions; no synthetic fallback.");
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        var result = GroupPlanningChecks.Trace(world, GroupPlanningChecks.Input(points.ToArray(), 65, 1), Check);
        return new("real-map coordinates and FIFO numeric harness; not final placement or moving crowd", "hfoo", radius, checks, result);
    }
}
