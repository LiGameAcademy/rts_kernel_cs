using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixPointCS;

namespace NumericPrototype.Movement;

internal static class MovementChecks
{
    internal static MovementWorld Fixture(bool wall = false, bool slope = true)
    {
        var flags = new byte[12 * 8];
        if (wall) for (var y = 0; y < 6; y++) flags[y * 12 + 5] = 2;
        var pathing = JsonSerializer.Serialize(new { width = 12, height = 8, cellSize = 32,
            origin = new { x = 0, y = 0 }, cellsBase64 = Convert.ToBase64String(flags) });
        var heights = Enumerable.Range(0, 12).Select(i => slope ? (i % 4) * 12 + (i / 4) * 4 : 0).ToArray();
        var terrain = JsonSerializer.Serialize(new { tilepointWidth = 4, tilepointHeight = 3, tileSize = 128,
            centerOffset = new { x = 0, y = 0 }, heights });
        return new MovementWorld(pathing, terrain);
    }

    internal static string Run(Action<bool, string> check)
    {
        var trace = new StringBuilder();
        check(FixedMath.Normalize(-Fixed64.Pi) == FixedMath.Normalize(-Fixed64.Pi + Fixed64.Pi2),
            "equivalent angle periods have one canonical raw representation");
        var flat = Fixture(slope: false);
        var start = FixedPoint.Parse("48", "112");
        var goal = FixedPoint.Parse("336", "112");
        var speed = InputQuantizer.Parse("270").Raw;
        var radius = InputQuantizer.Parse("8").Raw;
        var rate = InputQuantizer.Parse("0.5").Raw;
        var simple = new MovementSimulation(flat, start, goal, speed, radius, rate);
        simple.Step();
        check(simple.State.Position == FixedPoint.Parse("57", "112"), "flat aligned motion consumes exact 270/30 units");
        var almost = new MovementSimulation(flat, start, new(start.X + 9 * FixedMath.One + 1, start.Y), speed, radius, rate);
        almost.Step();
        check(almost.State.Status == MotionStatus.Moving && almost.State.Position.X == start.X + 9 * FixedMath.One,
            "a goal one raw unit beyond the frame distance cannot arrive early");
        check(new FixedPoint(3 * FixedMath.One, 4 * FixedMath.One).Length == 5 * FixedMath.One,
            "wide integer length matches the exact 3-4-5 triangle");
        check(MovementWorld.Floor(-1, 32 * FixedMath.One) == -1, "negative raw coordinate uses grid floor");
        check(!CollisionOracle.Clear(Fixture(wall: true), start, goal, radius), "direct wall crossing is rejected");
        var sloped = Fixture();
        check(sloped.Sample(FixedPoint.Parse("64", "64")) == 8 * FixedMath.One, "bilinear planar height sample");
        check(sloped.Sample(FixedPoint.Parse("384", "256")) == 44 * FixedMath.One, "last height vertex uses final quad");
        check(FixedMath.SlopeScale(10 * FixedMath.One, 32 * FixedMath.One) < FixedMath.One, "uphill reduces speed");
        check(FixedMath.TurnScale(0, Fixed64.PiHalf) < FixedMath.One, "turning reduces speed");
        var body = FixedPoint.Parse("192", "112");
        check(!CollisionOracle.Clear(flat, FixedPoint.Parse("48", "112"), FixedPoint.Parse("336", "112"), radius, body, radius),
            "swept collision catches crossing a body even if endpoint is clear");
        check(CollisionOracle.Clear(flat, FixedPoint.Parse("48", "128"), FixedPoint.Parse("336", "128"), radius, body, radius),
            "exact tangent contact does not overlap");
        check(IntegerPathfinder.Find(Fixture(wall: true), start, goal, InputQuantizer.Parse("31").Raw).Length == 0,
            "large-body clearance rejects the narrow gap instead of shrinking the unit");
        RunCase(Fixture(wall: true), null, MotionStatus.Arrived);
        RunCase(flat, body, MotionStatus.Failed);
        RunCase(sloped, null, MotionStatus.Arrived);
        var snapshot = JsonSerializer.Deserialize<MotionSnapshot>(simple.Capture())!;
        void Invalid(MotionSnapshot invalid)
        {
            var before = simple.Capture();
            try { simple.Restore(JsonSerializer.Serialize(invalid)); check(false, "invalid motion snapshot accepted"); }
            catch (InvalidDataException) { check(simple.Capture() == before, "rejected restore preserves current state"); }
        }
        Invalid(snapshot with { ContentIdentity = "other terrain" });
        Invalid(snapshot with { State = snapshot.State with { Waypoint = -1 } });
        Invalid(snapshot with { State = snapshot.State with { BlockedFrames = 90 } });
        Invalid(snapshot with { State = snapshot.State with { NextEvent = 1 } });
        var changedPath = snapshot.Path.ToArray();
        changedPath[0] = FixedPoint.Parse("64", "64");
        Invalid(snapshot with { Path = changedPath });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString()))).ToLowerInvariant();

        void RunCase(MovementWorld world, FixedPoint? blocker, MotionStatus expected)
        {
            MovementSimulation Create() => new(world, start, goal, speed, radius, rate, blocker, radius);
            var a = Create();
            var b = Create();
            var events = new List<MotionEvent>();
            var sawWaitRestore = false;
            for (var frame = 0; frame < 600; frame++)
            {
                if (frame % 7 == 0)
                {
                    b.Restore(a.Capture());
                    if (a.State.BlockedFrames > 0) sawWaitRestore = true;
                }
                var before = a.State;
                var left = a.Step();
                var right = b.Step();
                events.AddRange(left);
                check(a.Capture() == b.Capture() && JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right),
                    "per-frame raw state and event identity survive restoration");
                check(CollisionOracle.Clear(world, before.Position, a.State.Position, radius, blocker, radius),
                    "committed trace never crosses static terrain or the stationary body");
                trace.Append(a.Capture()).Append(JsonSerializer.Serialize(left));
                if (a.State.Status != MotionStatus.Moving) break;
            }
            check(a.State.Status == expected, "scenario has the expected arrival/failure outcome");
            check(events.Count == 1 && events[0].Sequence == 0, "one deterministic completion/failure event");
            check(a.Step().Length == 0 && a.State.NextEvent == 1, "terminal steps cannot duplicate completion events");
            if (expected == MotionStatus.Arrived) check(a.State.Position == goal, "arrival is the exact raw goal");
            else check(a.State.BlockedFrames == 90 && sawWaitRestore, "blocked 3-second counter survives snapshot");
        }
    }
}
