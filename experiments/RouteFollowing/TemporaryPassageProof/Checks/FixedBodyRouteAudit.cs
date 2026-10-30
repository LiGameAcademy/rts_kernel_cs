using System.Diagnostics;
using System.Text.Json;
using Rts.Movement.Prototype;

internal static class FixedBodyRouteAudit
{
    internal static void Run(string input, ulong id)
    {
        var world = RouteWorld.Load(File.ReadAllText(input));
        var before = world.Save();
        var member = world.Bodies[id];
        var blockers = world.Bodies.Values.Where(body => body.Id != id && RoutePlacements.IsFixed(body)).ToArray();
        var timer = Stopwatch.StartNew();
        var found = RoutePlanning.TryRoute(world, member.Position, member.Endpoint, blockers, out var points, out var corners);
        timer.Stop();
        var clear = found && points.Zip(points.Skip(1)).All(edge =>
            RoutePlanning.TryDirect(world, edge.First, edge.Second, blockers) is not null);
        Console.WriteLine("fixed_body_route=" + JsonSerializer.Serialize(new
        {
            world.Frame, id, found, clear, elapsed_ms = timer.Elapsed.TotalMilliseconds,
            blockers = blockers.Select(body => body.Id), points, corners
        }));
        if (world.Save() != before) throw new InvalidOperationException("Route query mutated authority");
    }
}
