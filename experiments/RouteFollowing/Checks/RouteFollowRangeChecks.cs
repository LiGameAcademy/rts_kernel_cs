using System.Reflection;
using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Compare the real private predicate with the previous floored-root contract.</summary>
internal static class RouteFollowRangeChecks
{
    internal static void Run()
    {
        // Bind the implementation without adding a public numerical API for an experimental optimization.
        var predicate = typeof(RouteWorld).Assembly.GetType("Rts.Movement.Prototype.RoutePlanning", throwOnError: true)!
            .GetMethod("WithinRoundedRange", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<SquaredDistance, int, bool>>();
        foreach (var radius in new[] { 9, 192, 200 })
        {
            var raw = (Int128)radius * SimScalar.Scale;
            var limit = (raw + 1) * (raw + 1);
            foreach (var square in new[] { (Int128)0, raw * raw - 1, raw * raw, raw * raw + 1,
                limit - 1, limit, limit + 1, ((Int128)1 << 126) - 1, (Int128)1 << 126, Int128.MaxValue })
                Compare(predicate, SquaredDistance.FromRaw(square), radius);
            for (var i = 0; i < 256; i++)
                Compare(predicate, SquaredDistance.FromRaw(limit * i / 127), radius);
            foreach (var delta in new[] { FixedVector2.Zero,
                FixedVector2.FromRaw((long)raw, 1), FixedVector2.FromRaw(-(long)raw, -1),
                FixedVector2.FromRaw((long)raw + 1, 0), FixedVector2.FromRaw(0, -(long)raw - 1) })
            {
                Compare(predicate, SquaredDistance.Between(FixedVector2.Zero, delta), radius);
                FollowBoundary(radius, delta);
            }
        }
    }

    private static void Compare(Func<SquaredDistance, int, bool> predicate, SquaredDistance distance, int radius)
    {
        bool expected;
        try { expected = distance.Length <= radius; }
        catch (OverflowException)
        {
            try { predicate(distance, radius); }
            catch (OverflowException) { return; }
            throw new InvalidOperationException("Follow range lost Length overflow.");
        }
        if (predicate(distance, radius) != expected)
            throw new InvalidOperationException($"Follow floored-root mismatch at radius {radius}, square {distance.Raw}.");
    }

    private static void FollowBoundary(int radius, FixedVector2 delta)
    {
        var world = new RouteWorld();
        world.Spawn(new(900, 640));
        world.Move([1], new(1500, 640));
        world.Step();
        var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        var position = new FixedVector2(900, 640);
        var near = position + (radius == 9 ? delta : new FixedVector2(80, 0));
        var next = position + (radius == 192 ? delta : new FixedVector2(500, 0));
        var preference = radius == 200 ? position + delta : new FixedVector2(1500, 640);
        var endpoint = preference + new FixedVector2(64, 0);
        var route = state.Tasks[0] with { Points = [near, next, endpoint], Corners = [1], Anchor = endpoint };
        state = state with
        {
            Tasks = [route],
            Units = [state.Units[0] with { Position = position, Preference = preference, Endpoint = endpoint,
                Offset = default, Cursor = 0, RouteVersion = route.Version,
                Progress = 0, ProgressCursor = 0, ProgressVersion = 0, Connection = null }]
        };
        world = RouteWorld.Load(JsonSerializer.Serialize(state));
        var within = delta.Length <= radius;
        var expected = within ? radius == 200 ? endpoint : next : near;
        if (world.FollowTarget(1) != expected)
            throw new InvalidOperationException($"Follow target changed at radius {radius}, delta {delta}.");
    }
}
