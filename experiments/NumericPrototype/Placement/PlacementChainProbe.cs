using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FixPointCS;
using NumericPrototype.Groups;
using NumericPrototype.Movement;

namespace NumericPrototype.Placement;

internal static class PlacementChainProbe
{
    internal sealed record FormationInput(FixedFormationKind Kind, FixedPoint Anchor, long Heading,
        long Spacing, ulong Leader);
    private sealed record SavedChain(int Format, string WorldIdentity, int Frame, FormationInput Formation,
        PlacementMember[] Members, PlacementBody[] Bodies, GroupJobSnapshot Job);
    internal sealed record Result(string Kind, int Frames, int Succeeded, int Failed, int Adjusted, string TraceHash);
    internal sealed record RealResult(string WorldIdentity, long RadiusRaw, int Checks, Result[] Formations);

    internal static string RunSynthetic(Action<bool, string> check)
    {
        var world = PlacementChecks.World(256, 256);
        var points = Enumerable.Range(0, 140).Select(index => world.Center(index % 14 + 20, index / 14 + 20)).ToArray();
        var starts = GroupPlanningChecks.Input(points, 65, 1).Members;
        var members = starts.Select(member => new PlacementMember(member.Id, member.Start, 31 * FixedMath.One, [member.Start])).ToArray();
        var results = Enum.GetValues<FixedFormationKind>().Select(kind => Run(world, members,
            new(kind, world.Center(128, 128), Fixed64.Pi / 7, FixedPlacement.Spacing(world, members), starts[32].Id), [], check)).ToArray();
        check(results.All(result => result.Succeeded == 65), "all synthetic formations place every member");
        var edge = Run(world, members.Take(7).ToArray(),
            new(FixedFormationKind.Compact, world.Center(0, 0), 0, FixedPlacement.Spacing(world, members), starts[0].Id), [], check);
        check(edge.Succeeded == 7 && edge.Adjusted > 0, "outside-map ideal slots adjust locally into legal edge cells");
        return Hash(JsonSerializer.Serialize(results) + JsonSerializer.Serialize(edge));
    }

    internal static RealResult RunReal(string assetsDirectory)
    {
        var world = new MovementWorld(File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/pathing.json")),
            File.ReadAllText(Path.Combine(assetsDirectory, "map-parsed/echoisles/terrain-heightfield.json")));
        using var balance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(assetsDirectory, "slk-exported/Units/UnitBalance.json")));
        var radius = MovementWorld.Number(balance.RootElement.GetProperty("records").EnumerateArray()
            .Single(value => value.GetProperty("unitBalanceID").GetString() == "hfoo"), "collision");
        var points = new List<FixedPoint>();
        for (var y = 2; y < world.Height - 2 && points.Count < 140; y += 2)
        for (var x = 2; x < world.Width - 2 && points.Count < 140; x += 2)
        {
            var point = world.Center(x, y);
            if (CollisionOracle.Clear(world, point, point, radius)) points.Add(point);
        }
        if (points.Count < 140) throw new InvalidOperationException("Insufficient real placement input; no synthetic fallback.");
        var starts = GroupPlanningChecks.Input(points.ToArray(), 65, 1).Members;
        var members = starts.Select(member => new PlacementMember(member.Id, member.Start, radius, [member.Start])).ToArray();
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        var anchor = world.Center(world.Width / 2, world.Height / 2);
        var results = Enum.GetValues<FixedFormationKind>().Select(kind => Run(world, members,
            new(kind, anchor, Fixed64.Pi / 7, FixedPlacement.Spacing(world, members), starts[32].Id),
            [new(anchor, radius)], Check)).ToArray();
        return new(world.Identity, radius, checks, results);
    }

    private static Result Run(MovementWorld world, PlacementMember[] members, FormationInput formation,
        PlacementBody[] bodies, Action<bool, string> check)
    {
        var ideals = FixedFormation.Create(formation.Kind, members.Length, formation.Spacing, formation.Anchor, formation.Heading);
        var input = new GroupPlanInput(1, members.Select(member => new GroupMember(member.Id, member.Start)).ToArray(),
            ideals, formation.Leader, false);
        var job = new GroupPlanningJob(input, world);
        var trace = new StringBuilder();
        for (var frame = 1; frame <= 20000; frame++)
        {
            var saved = new SavedChain(1, world.Identity, frame - 1, formation, members, bodies, job.Capture());
            saved = JsonSerializer.Deserialize<SavedChain>(JsonSerializer.Serialize(saved))!;
            var restored = Restore(world, saved);
            if (frame == 1)
            {
                try
                {
                    Restore(world, saved with { Formation = saved.Formation with { Spacing = saved.Formation.Spacing + 1 } });
                    check(false, "changed formation input must not reuse frozen matching");
                }
                catch (InvalidDataException) { check(true, "formation mismatch rejected before restore"); }
            }
            check(job.Advance(world, 128, 64) == restored.Advance(world, 128, 64), "formation chain budget charge restores");
            check(JsonSerializer.Serialize(job.Capture()) == JsonSerializer.Serialize(restored.Capture()),
                "formation chain raw matching and preparation state restores");
            trace.Append(frame).Append(JsonSerializer.Serialize(job.Capture()));
            if (!job.Ready) continue;
            var assignment = job.Assignments().ToDictionary(item => item.Member, item => item.Slot);
            var outcomes = FixedPlacement.Plan(world, members, ideals, assignment, bodies);
            var restoredOutcomes = FixedPlacement.Plan(world, saved.Members, saved.Job.Input.Ideals,
                restored.Assignments().ToDictionary(item => item.Member, item => item.Slot), saved.Bodies);
            check(JsonSerializer.Serialize(outcomes) == JsonSerializer.Serialize(restoredOutcomes),
                "restored chain has identical final goals/failures at identical frame");
            // Caller applies only non-null goals; failure leaves the exact previous order payload untouched.
            var oldOrders = members.ToDictionary(member => member.Id, member => JsonSerializer.Serialize(member.Retained));
            var appliedOrders = new Dictionary<ulong, string>(oldOrders);
            foreach (var outcome in outcomes)
            {
                if (outcome.Goal is not FixedPoint point)
                {
                    check(appliedOrders[outcome.Member] == oldOrders[outcome.Member], "failed member retains previous order payload");
                    continue;
                }
                var member = members.Single(item => item.Id == outcome.Member);
                check(IntegerPathfinder.Find(world, member.Start, point, member.Radius).Length > 0,
                    "accepted final goal has an independent A* path");
                check(CollisionOracle.Clear(world, point, point, member.Radius), "accepted final goal clears static geometry");
                check(bodies.All(body => !FixedPlacement.Overlaps(point, member.Radius, body)), "accepted goal avoids external bodies");
                foreach (var other in outcomes.Where(other => other.Member < outcome.Member && other.Goal.HasValue))
                    check(!FixedPlacement.Overlaps(point, member.Radius,
                        new(other.Goal!.Value, members.Single(item => item.Id == other.Member).Radius)), "accepted goals do not overlap");
                foreach (var failed in members.Where(item => outcomes.Single(outcome => outcome.Member == item.Id).Goal is null))
                    check(failed.Retained.All(retained => !FixedPlacement.Overlaps(point, member.Radius, new(retained, failed.Radius))),
                        "successful placement respects failed members' retained positions");
                appliedOrders[outcome.Member] = JsonSerializer.Serialize(point);
            }
            trace.Append(JsonSerializer.Serialize(outcomes)).Append(JsonSerializer.Serialize(appliedOrders));
            return new(formation.Kind.ToString(), frame, outcomes.Count(item => item.Goal.HasValue),
                outcomes.Count(item => !item.Goal.HasValue), outcomes.Count(item => item.Adjusted), Hash(trace.ToString()));
        }
        throw new InvalidOperationException("Formation chain exceeded diagnostic frame bound.");
    }

    private static GroupPlanningJob Restore(MovementWorld world, SavedChain saved)
    {
        if (saved.Format != 1 || saved.WorldIdentity != world.Identity || saved.Frame < 0)
            throw new InvalidDataException("Invalid experimental formation chain identity.");
        var expected = FixedFormation.Create(saved.Formation.Kind, saved.Members.Length,
            saved.Formation.Spacing, saved.Formation.Anchor, saved.Formation.Heading);
        if (!expected.SequenceEqual(saved.Job.Input.Ideals) || saved.Formation.Leader != saved.Job.Input.Leader
            || !saved.Members.Select(member => new GroupMember(member.Id, member.Start)).SequenceEqual(saved.Job.Input.Members))
            throw new InvalidDataException("Formation specification and frozen job inputs disagree.");
        return GroupPlanningJob.Restore(saved.Job, world);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
