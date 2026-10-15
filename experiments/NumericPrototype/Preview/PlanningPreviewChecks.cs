using System.Text.Json.Nodes;
using System.Text.Json;

namespace NumericPrototype;

internal static class PlanningPreviewChecks
{
    internal static object Run(string assetsDirectory)
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        foreach (var count in new[] { 65, 300, 500 })
        {
            using var session = new PlanningPreviewSession(assetsDirectory, count);
            var map = session.ReadMap();
            string Number(long raw) => (raw / (decimal)PrototypeScalar.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
            session.Plan(0, Number(map.Origin.X + map.Width * map.CellSize / 2), Number(map.Origin.Y + map.Height * map.CellSize / 2));
            session.Tick();
            Check(session.Cancel(1), "pending member cancellation accepted");
            var snapshot = session.Capture();
            using var clone = new PlanningPreviewSession(assetsDirectory, count);
            clone.Restore(snapshot);
            var phases = new HashSet<string>();
            while (session.Planning)
            {
                if (phases.Add(session.PlanningStage)) clone.Restore(session.Capture());
                session.Tick();
                clone.Tick();
                Check(session.Capture() == clone.Capture(), "preview restore preserves every subsequent frame");
            }
            Check(session.Metrics().Succeeded + session.Metrics().Failed == count - 1,
                "canceled member excluded from new placement");
            Check(session.ReadUnits().Single(unit => unit.Id == 1).Canceled, "cancellation remains visible after commit");
            Check(session.MatchesPlacementReference(), "prepared/indexed placement equals independent linear reference");
            var completed = session.Capture();
            clone.Restore(completed);
            Check(clone.Capture() == completed, "completed preview snapshot restores");
            var corrupt = completed.Replace("\"Format\":3", "\"Format\":4");
            try { session.Restore(corrupt); Check(false, "invalid preview format accepted"); }
            catch (InvalidDataException) { Check(true, "invalid preview rejected"); }
            Check(session.Capture() == completed, "failed restore is atomic");
            var damaged = JsonNode.Parse(completed)!;
            var first = damaged["Outcomes"]![0]!;
            first["Adjusted"] = !first["Adjusted"]!.GetValue<bool>();
            try { session.Restore(damaged.ToJsonString()); Check(false, "tampered completed outcome accepted"); }
            catch (InvalidDataException) { Check(true, "tampered completed outcome rejected by replay"); }
            Check(session.Capture() == completed, "tampered outcome restore remains atomic");
        }
        VerifyLateCommands(assetsDirectory, Check);
        VerifyWarmCache(assetsDirectory, Check);
        VerifyBodyIndex(Check);
        return new { Checks = checks, Result = "planning_preview_restore_pass" };
    }
    private static void VerifyLateCommands(string assetsDirectory, Action<bool, string> check)
    {
        using var session = new PlanningPreviewSession(assetsDirectory, 500);
        using var clone = new PlanningPreviewSession(assetsDirectory, 500);
        var map = session.ReadMap();
        string Number(long raw) => (raw / (decimal)PrototypeScalar.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = Number(map.Origin.X + map.Width * map.CellSize / 2);
        var y = Number(map.Origin.Y + map.Height * map.CellSize / 2);
        session.Plan(0, x, y);
        while (session.PlanningStage != "Placement") session.Tick();
        check(session.ReadUnits().All(unit => unit.Goal is null), "pending placement never publishes partial goals");
        check(session.Cancel(1), "member can be canceled after matching and connectivity finish");
        clone.Restore(session.Capture());
        while (session.Planning)
        {
            session.Tick(); clone.Tick();
            check(session.Capture() == clone.Capture(), "late cancellation and restarted placement restore identically");
        }
        check(session.MatchesPlacementReference() && session.Metrics().Succeeded + session.Metrics().Failed == 499,
            "late cancellation retains exact reference results");
        var oldGoals = session.ReadUnits().Select(unit => unit.Goal).ToArray();
        session.Plan(1, x, y);
        while (session.PlanningStage != "Placement") session.Tick();
        check(session.ReadUnits().Select(unit => unit.Goal).SequenceEqual(oldGoals), "previous goals survive pending replacement");
        session.Plan(2, x, y);
        while (session.Planning) session.Tick();
        check(session.MatchesPlacementReference(), "new command replaces a pending final placement without stale commit");
        session.Plan(0, x, y);
        foreach (var unit in session.ReadUnits()) check(session.Cancel(unit.Id), "all pending members can be canceled");
        while (session.Planning) session.Tick();
        check(session.Metrics().Succeeded + session.Metrics().Failed == 0, "all-canceled plan commits no results");
    }

    private static void VerifyWarmCache(string assetsDirectory, Action<bool, string> check)
    {
        using var warm = new PlanningPreviewSession(assetsDirectory, 65);
        var map = warm.ReadMap();
        string Number(long raw) => (raw / (decimal)PrototypeScalar.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var x = Number(map.Origin.X + map.Width * map.CellSize / 2);
        var y = Number(map.Origin.Y + map.Height * map.CellSize / 2);
        warm.Plan(0, x, y);
        while (warm.Planning) warm.Tick();
        warm.Plan(1, x, y);
        using var cold = new PlanningPreviewSession(assetsDirectory, 65);
        cold.Restore(warm.Capture());
        while (warm.Planning)
        {
            warm.Tick();
            cold.Tick();
            check(warm.Capture() == cold.Capture(), "warm/cold reachability cache preserves authoritative frames");
        }
        check(warm.MatchesPlacementReference(), "cached reachability preserves reference outcomes");
    }

    private static void VerifyBodyIndex(Action<bool, string> check)
    {
        var world = Placement.PlacementChecks.World();
        var bodies = Enumerable.Range(0, 128).Select(index => new Placement.PlacementBody(
            world.Center(index * 17 % 64 - 16, index * 31 % 48 - 16),
            (index % 4 + 1) * world.CellSize)).ToArray();
        var spatial = new Placement.PlacementBodyIndex(world);
        foreach (var body in bodies) spatial.Add(body);
        for (var index = 0; index < 512; index++)
        {
            var point = world.Center(index * 19 % 80 - 24, index * 37 % 64 - 24);
            var radius = (index % 4 + 1) * world.CellSize;
            check(spatial.Overlaps(point, radius) == bodies.Any(body => Placement.FixedPlacement.Overlaps(point, radius, body)),
                "spatial broad phase equals exact scan across negative coordinates and mixed radii");
        }
    }

}
