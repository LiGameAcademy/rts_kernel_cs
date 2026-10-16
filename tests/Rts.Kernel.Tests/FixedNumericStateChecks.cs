using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FixedNumericStateChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var small = SimScalar.FromRaw(1);
        check(MotionRules.TurnSpeedScale(0, SimMath.Pi, 25, (SimScalar)25 + small, 0) == SimScalar.Zero,
            "one-raw angular interval clamps before an unrepresentable division");
        var ramp = new TerrainHeights(2, 2, 100, default, new SimScalar[] { 0, 100, 0, 100 });
        check(MotionRules.SlopeSpeedScale(ramp, new(10, 50), new(90, 50), maximumDegrees: small) == SimScalar.FromRatio(3, 5),
            "one-raw slope threshold clamps before division overflow");
        var grid = new PathingGrid(4, 4, 1, new(-2, -2), new byte[16]);
        check(grid.TryWorldToCell(FixedVector2.FromRaw(-1, -1), out var cell) && cell == new GridCell(1, 1),
            "negative sub-unit world coordinates floor exactly");
        check(!grid.TryWorldToCell(new(SimScalar.MinValue, 0), out _), "out-of-domain coordinate returns false before wide-to-index narrowing");
        var match = new RtsMatch(MatchConfig.Default, 89);
        check(match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(SimulationDomain.CoordinateLimit, 0))).Accepted,
            "world endpoint is admitted");
        match.Step();
        match.DrainEvents();
        match.SubmitCommand(CommandEnvelope.SetVelocity(2, 0, 1, new(1), new(1, 0)));
        match.Step();
        check(match.Entities.Single().Position.X == SimulationDomain.CoordinateLimit
            && match.Entities.Single().Velocity == FixedVector2.Zero && match.DrainEvents().Single().Detail == "world_bounds",
            "free diagnostic velocity stops at admitted world boundary without corrupting snapshots");
        check(!match.SubmitCommand(CommandEnvelope.Spawn(3, 0, 2, new(SimScalar.MaxValue, 0))).Accepted,
            "out-of-domain raw command is rejected before execution");
        var saved = match.CaptureSnapshot();
        var json = SnapshotJson.Serialize(saved);
        using var document = JsonDocument.Parse(json);
        check(saved.FormatVersion == 12 && document.RootElement.GetProperty("entities")[0].GetProperty("positionX").GetInt64()
            == SimulationDomain.CoordinateLimit.Raw, "v12 authoritative coordinates serialize raw integers");
        check(!json.Contains("length", StringComparison.OrdinalIgnoreCase), "derived vector length is not snapshot authority");
        check(RtsMatch.Restore(SnapshotJson.Deserialize(json)).ComputeStateHash() == match.ComputeStateHash(),
            "world boundary snapshot restores without loss");
        try { RtsMatch.Restore(saved with { FormatVersion = 10 }); check(false, "v10 snapshot rejected"); }
        catch (InvalidDataException) { check(true, "v10 snapshot rejected"); }
        try { SnapshotJson.Deserialize(json.Replace(SimulationDomain.CoordinateLimit.Raw.ToString(), "1.5")); check(false, "fractional coordinate wire value rejected"); }
        catch (JsonException) { check(true, "fractional coordinate wire value rejected"); }
        var motion = new MotionParameters(TurnRate: SimScalar.FromRatio(3, 4));
        check(JsonSerializer.Deserialize<MotionParameters>(JsonSerializer.Serialize(motion)) == motion,
            "typed motion defaults and custom raw values restore through JSON");
        var positions = new FixedVector2[] { new(-1000000, -1000000), new(1000000, -1000000), new(1000000, 1000000) };
        var slots = positions.Reverse().ToArray();
        var matching = new SlotMatchingWork(positions, slots, 1);
        matching.Advance(1);
        var state = matching.Capture();
        state.U[1] += 1;
        try { new SlotMatchingWork(positions, slots, 1).Restore(state); check(false, "forged wide matching potential rejected"); }
        catch (InvalidDataException) { check(true, "forged wide matching potential rejected"); }
        var segment = new MoveOrder(new(new(100, 99), 1), new FixedVector2[] { new(0, 0), new(100, 99) }, 1);
        check(!WaypointProgress.Contains(FixedVector2.Zero, FixedVector2.Zero, new(1, 0)),
            "zero-length segment accepts only its exact endpoint");
        var point = FixedVector2.Zero;
        for (var step = 0; step < 1000; step++)
        {
            point = WaypointProgress.Advance(segment, 1, point, SimScalar.FromRatio(1, 100));
            check(WaypointProgress.Contains(segment.Waypoints[0], segment.Waypoints[1], point),
                "many small steps remain within a two-raw-unit original-segment bound");
        }
        var tinyGrid = new PathingGrid(4, 4, 10, default, new byte[16]);
        var tiny = new RtsMatch(MatchConfig.Default, 89, tinyGrid);
        tiny.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(5, 5)));
        tiny.Step(); tiny.DrainEvents();
        tiny.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 1, new(1), new(25, 25), SimScalar.FromRaw(1)));
        tiny.Step();
        check(tiny.DrainEvents().Single().Detail == "movement_below_resolution" && !tiny.IsMoving(new(1)),
            "positive motion below one-frame resolution reports failure instead of stalling forever");
        var exact = FormationLayout.Match(positions, positions, 0);
        check(exact.SequenceEqual(new[] { 0, 1, 2 }), "extreme admitted matching costs preserve exact minimum without scalar narrowing");
    }
}
