using System.Text.Json;
using Rts.Kernel.Numerics;
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
                TransitQuarters = historicalFormat == 1 ? 4 : body.TransitQuarters }).ToArray(),
            // These fixtures' initial version is the original straight interpolation;
            // later versions are unsampled grid routes with genuine vertices.
            Tasks = state.Tasks.Select(task => task with { Corners = task.Version == 1 ? [] : Corners(task) }).ToArray()
        };
        return RouteWorld.Load(JsonSerializer.Serialize(state));
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
