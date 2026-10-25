using Rts.Movement.Prototype;
using Rts.Kernel.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class RouteNavigationChecks
{
    private static int _checks;
    internal static void Run()
    {
        var single = new RouteWorld();
        single.Spawn(new(400, 400));
        single.Move([1], new(1400, 400));
        single.Step();
        var state = JsonSerializer.Deserialize<RouteSnapshot>(single.Save())!;
        var task = state.Tasks[0] with { Points = new FixedVector2[] { new(400, 1000), new(1400, 1000) }, Corners = [] };
        // A deliberately remote shared reference must not pull a free-moving member towards it.
        var detached = RouteWorld.Load(JsonSerializer.Serialize(state with { Tasks = [task] }));
        detached.Step();
        Check(detached.Units[0].Position.Y == 400 && detached.FollowTarget(1) == detached.Units[0].Endpoint,
            "Shared centre line is not a hard intermediate target");
        Check(detached.Units[0].Navigation!.Kind == MemberReferenceKind.Direct, "Direct own-goal reference");

        var peers = RouteScenarios.Create(5);
        peers.Step();
        Check(peers.Units.All(body => body.Navigation!.Kind == MemberReferenceKind.Direct),
            "Moving teammates do not become static detours");
        var before = peers.Save();
        _ = peers.FollowTarget(1);
        Check(peers.Save() == before, "Reference preview is pure");
        var view = peers.Units[0].Navigation!;
        ((FixedVector2[])view.Points)[0] = new(0, 0);
        Check(peers.Save() == before, "Reference arrays are detached from authority");

        var restored = RouteWorld.Load(peers.Save());
        for (var i = 0; i < 40; i++)
        {
            peers.Step(); restored.Step();
            Check(peers.Hash() == restored.Hash() && peers.Events.SequenceEqual(restored.Events), "Reference JSON continuation");
        }
        var navigation = detached.Units[0].Navigation!;
        Check(detached.SetWall(new(30, 10, 2, 5), true), "Obstacle inserted away from body");
        detached.Step();
        Check(detached.Units[0].Navigation!.MapRevision == 1
            && detached.Units[0].Navigation!.Version > navigation.Version, "Map invalidates only affected member reference");
        var changed = JsonSerializer.Deserialize<RouteSnapshot>(single.Save())!;
        var member = changed.Units[0];
        changed = changed with { Units = [member with { Endpoint = member.Endpoint + new FixedVector2(0, 64) }] };
        var repaired = RouteWorld.Load(JsonSerializer.Serialize(changed));
        repaired.Step();
        Check(repaired.Units[0].Navigation!.Goal == repaired.Units[0].Endpoint, "Owned goal revision invalidates reference");
        var currentTask = repaired.Units[0].Task;
        var currentVersion = repaired.Units[0].Navigation!.Version;
        repaired.Move([1], new(1000, 1000), append: true);
        repaired.Step();
        Check(repaired.Units[0].Task == currentTask && repaired.Units[0].Navigation!.Version == currentVersion
            && repaired.Units[0].Pending.Count == 1, "Append preserves active reference");
        repaired.Stop([1]); repaired.Step();
        Check(repaired.Units[0].Navigation is null && repaired.Units[0].Task == 0
            && repaired.Units[0].Pending.Count == 0, "Stop releases reference and queue");
        var stoppedAt = repaired.Units[0].Position;
        for (var i = 0; i < 10; i++) repaired.Step();
        Check(repaired.Units[0].Position == stoppedAt, "Old task cannot reclaim Stop");
        var invalid = JsonSerializer.Deserialize<RouteSnapshot>(peers.Save())! with { Format = 6 };
        var rejected = false;
        try { RouteWorld.Load(JsonSerializer.Serialize(invalid)); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Runtime rejects snapshot6");
        foreach (var field in new[] { "Points", "Corners" })
        {
            var corrupt = JsonNode.Parse(peers.Save())!;
            corrupt["Units"]![0]!["Navigation"]![field] = null;
            rejected = false;
            try { RouteWorld.Load(corrupt.ToJsonString()); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Malformed reference rejected as invalid snapshot");
        }
        Console.WriteLine($"navigation_checks=PASS assertions={_checks} format={RouteSettings.SnapshotFormat}");
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
