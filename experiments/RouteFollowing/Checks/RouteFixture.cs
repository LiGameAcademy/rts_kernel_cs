using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Kernel;
using Rts.Movement.Prototype;

/// <summary>Explicit setup of frozen historical inputs, never a runtime snapshot migration.</summary>
internal static class RouteFixture
{
    internal static RouteWorld Load(string name)
    {
        using var stream = typeof(RouteFixture).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing frozen fixture.");
        var state = JsonSerializer.Deserialize<RouteSnapshot>(stream) ?? throw new InvalidDataException("Empty fixture.");
        var historicalFormat = state.Format;
        state = state with
        {
            Format = RouteSettings.SnapshotFormat,
            Units = state.Units.Select(body => body with { Progress = 0, ProgressCursor = 0, ProgressVersion = 0,
                PlacementCheckedRevision = -1, TransitQuarters = historicalFormat == 1 ? 4 : body.TransitQuarters }).ToArray(),
            // These fixtures' initial version is the original straight interpolation;
            // later versions are unsampled grid routes with genuine vertices.
            Tasks = state.Tasks.Select(task => task with { Corners = task.Version == 1 ? [] : Corners(task) }).ToArray()
        };
        return RouteWorld.Load(JsonSerializer.Serialize(ReserveHistoricalGoals(state)));
    }

    private static RouteSnapshot ReserveHistoricalGoals(RouteSnapshot state)
    {
        var units = state.Units.ToDictionary(body => body.Id);
        foreach (var group in state.Units.Where(body => body.Task != 0).GroupBy(body => body.Task))
        {
            var occupied = new List<FixedVector2>();
            var terrain = new RouteWorld(state.Width, state.Height, state.Flags);
            foreach (var body in group.OrderByDescending(body => body.Phase == WalkPhase.Settled).ThenBy(body => body.Id))
            {
                // Historical snapshots may already contain competing targets. Preserve
                // positions, phases and routes, but explicitly repair goals for the new
                // ownership invariant before using them as unrelated motion fixtures.
                var candidates = new[] { body.Endpoint, body.Preference }.Concat(
                    Enumerable.Range(0, state.Width * state.Height)
                        .Select(index => new FixedVector2(index % state.Width * 32 + 16, index / state.Width * 32 + 16))
                        .Where(point => (point - body.Preference).Length <= 156)
                        .OrderBy(point => SquaredDistance.Between(point, body.Preference)).ThenBy(point => point.Y).ThenBy(point => point.X));
                var found = false;
                foreach (var point in candidates)
                {
                    if (occupied.Any(other => (other - point).Length < 64)) continue;
                    try { terrain.Spawn(point); }
                    catch (ArgumentException) { continue; }
                    if (body.Phase == WalkPhase.Settled && point != body.Endpoint)
                        throw new InvalidOperationException("Cannot change a settled fixture goal.");
                    occupied.Add(point);
                    units[body.Id] = body with { Endpoint = point, Connection = point == body.Endpoint ? body.Connection : null };
                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException("Cannot reserve historical fixture goals.");
            }
        }
        return state with { Units = state.Units.Select(body => units[body.Id]).ToArray() };
    }

    private static IReadOnlyList<int> Corners(RouteTask task)
    {
        var corners = new List<int>();
        for (var i = 1; i + 1 < task.Points.Count; i++)
        {
            var a = task.Points[i] - task.Points[i - 1];
            var b = task.Points[i + 1] - task.Points[i];
            if ((Int128)a.X.Raw * b.Y.Raw != (Int128)a.Y.Raw * b.X.Raw
                || (Int128)a.X.Raw * b.X.Raw + (Int128)a.Y.Raw * b.Y.Raw < 0) corners.Add(i);
        }
        return corners;
    }
}
