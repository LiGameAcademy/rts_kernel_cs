using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixPointCS;
using NumericPrototype.Groups;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal static class PlacementChecks
{
    internal static MovementWorld World(int width = 32, int height = 16, bool wall = false)
    {
        var flags = new byte[width * height];
        if (wall) for (var y = 0; y < height; y++) flags[y * width + width / 2] = 2;
        return new(JsonSerializer.Serialize(new { width, height, cellSize = 32,
            origin = new { x = -512, y = -256 }, cellsBase64 = Convert.ToBase64String(flags) }),
            JsonSerializer.Serialize(new { tilepointWidth = width / 4 + 1, tilepointHeight = height / 4 + 1,
                tileSize = 128, centerOffset = new { x = -512, y = -256 },
                heights = new int[(width / 4 + 1) * (height / 4 + 1)] }));
    }

    internal static string Run(Action<bool, string> check)
    {
        var trace = new StringBuilder();
        var spacing = 64 * FixedMath.One;
        foreach (var kind in Enum.GetValues<FixedFormationKind>())
        foreach (var count in new[] { 1, 2, 3, 7, 65, 500 })
        {
            var points = FixedFormation.Create(kind, count, spacing, default, 0);
            var rotated = FixedFormation.Create(kind, count, spacing, default, Fixed64.Pi / 2);
            check(points.Length == count && points[0] == default, "formation preserves anchor and member count");
            check(points.Distinct().Count() == count, "all ideal slots are distinct");
            for (var index = 0; index < count; index++)
            {
                check(Math.Abs((double)rotated[index].X + points[index].Y) / FixedMath.One < 0.01
                    && Math.Abs((double)rotated[index].Y - points[index].X) / FixedMath.One < 0.01,
                    "quarter turn error below 0.01 world units over tested formation range");
                // Floating arithmetic is solely an error oracle, never a slot-selection input.
                if (kind == FixedFormationKind.Circle && index > 0)
                {
                    var followers = count - 1;
                    var expectedRadius = followers < 3 ? 64 : Math.Max(64, 64 / (2 * Math.Sin(Math.PI / followers)));
                    check(Math.Abs(points[index].Length / (double)FixedMath.One - expectedRadius) < 0.01,
                        "circle radius error below 0.01 world units");
                }
            }
            check(FixedFormation.Create(kind, count, spacing, default, Fixed64.Pi2).SequenceEqual(points),
                "full-period heading has identical raw geometry");
            trace.Append(JsonSerializer.Serialize(points)).Append(JsonSerializer.Serialize(rotated));
        }
        var rectangle = FixedFormation.Create(FixedFormationKind.Rectangle, 4, spacing, default, 0);
        check(rectangle.SequenceEqual(new FixedPoint[] { default, new(0, -spacing), new(0, spacing), new(-spacing, 0) }),
            "rectangle four-member shape follows odd-column lateral ordering");
        var compact = FixedFormation.Create(FixedFormationKind.Compact, 7, spacing, default, 0);
        check(compact[1].X == -spacing / 2 && compact[4].X == spacing / 2
            && Math.Abs(compact[1].Y / (double)FixedMath.One + 32 * Math.Sqrt(3)) < 0.000001,
            "first compact ring matches analytical hexagon");
        var world = World(wall: true);
        var radius = 8 * FixedMath.One;
        var left = world.Center(3, 6);
        var right = world.Center(28, 6);
        PlacementMember[] members = [new(1, world.Center(20, 6), radius, [world.Center(20, 6)]),
            new(2, left, radius, [left, right])];
        FixedPoint[] ideals = [right, right];
        var outcomes = FixedPlacement.Plan(world, members, ideals, new Dictionary<ulong, int> { [1] = 0, [2] = 1 }, []);
        check(outcomes[1].Goal is null && outcomes[0].Goal is not null && outcomes[0].Goal != right,
            "unreachable member fails and its retained future goal forces survivor to adjust");
        check(outcomes[0].Adjusted, "adjustment is explicitly reported");
        check(!new PlacementReachability(world).Connected(left, right, radius), "solid wall separates components");
        check(!FixedPlacement.Overlaps(left, radius, new(left + new FixedPoint(2 * radius, 0), radius)),
            "exact body tangency is allowed");
        check(FixedPlacement.Overlaps(left, radius, new(left + new FixedPoint(2 * radius - 1, 0), radius)),
            "one raw unit body penetration is rejected");
        var tangent = world.Center(10, 7);
        var blocked = FixedPlacement.Plan(world, [new(1, left, radius, [left])], [tangent],
            new Dictionary<ulong, int> { [1] = 0 }, [new(tangent, radius)]);
        check(blocked[0].Goal is not null && blocked[0].Goal != tangent, "external stationary body displaces final goal");
        var distant = world.Center(50, 7);
        var failure = FixedPlacement.Plan(world, [new(1, left, radius, [left])], [distant],
            new Dictionary<ulong, int> { [1] = 0 }, []);
        check(failure[0].Goal is null, "no candidates within bounded neighborhood reports failure");
        var reach = new PlacementReachability(world);
        foreach (var goal in new[] { world.Center(8, 6), world.Center(19, 6), world.Center(16, 6) })
            check(reach.Connected(left, goal, radius) == (IntegerPathfinder.Find(world, left, goal, radius).Length > 0),
                "component connectivity agrees with independent integer A* oracle");
        trace.Append(JsonSerializer.Serialize(outcomes)).Append(JsonSerializer.Serialize(blocked)).Append(JsonSerializer.Serialize(failure));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString()))).ToLowerInvariant();
    }
}
