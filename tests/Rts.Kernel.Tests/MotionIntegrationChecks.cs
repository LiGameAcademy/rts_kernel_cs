using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MotionIntegrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var id = new EntityId(1);
        var grid = new PathingGrid(20, 6, 10, default, new byte[120]);
        var ramp = new TerrainHeights(21, 7, 10, default,
            Enumerable.Range(0, 147).Select(i => (i % 21) * 10.0).ToArray());
        var flat = new TerrainHeights(21, 7, 10, default, new double[147]);
        bool Near(double left, double right) => Math.Abs(left - right) < 1e-10;
        RtsMatch Create(SimVector2 start, TerrainHeights? terrain, double facing = 0)
        {
            var match = new RtsMatch(MatchConfig.Default, 7, grid, terrain);
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, start));
            match.Step();
            match.DrainEvents();
            var saved = match.CaptureSnapshot();
            return RtsMatch.Restore(saved with { Entities = new[] { saved.Entities[0] with { Facing = facing } } }, grid, terrain);
        }
        void Move(RtsMatch match, SimVector2 goal, double speed, MotionParameters? motion)
        {
            check(match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, match.Frame, id, goal, speed,
                motion: motion)).Accepted, "motion command accepted at submission");
            match.Step();
        }
        var east = Create(new SimVector2(15, 15), flat);
        Move(east, new SimVector2(195, 15), 30, new MotionParameters());
        var north = Create(new SimVector2(15, 15), flat, Math.PI / 2);
        Move(north, new SimVector2(15, 55), 30, new MotionParameters());
        check(Near(east.Entities.Single().Position.X - 15, north.Entities.Single().Position.Y - 15),
            "flat ground speed independent of planar movement direction");
        var uphill = Create(new SimVector2(15, 15), ramp);
        Move(uphill, new SimVector2(195, 15), 30, new MotionParameters());
        check(Near(uphill.Entities.Single().Position.X, 15.6), "real elevation rise limits uphill speed");
        var downhill = Create(new SimVector2(185, 15), ramp, -Math.PI);
        Move(downhill, new SimVector2(5, 15), 30, new MotionParameters());
        check(Near(downhill.Entities.Single().Position.X, 184.15), "real elevation drop limits downhill speed");
        var reverse = Create(new SimVector2(15, 15), null);
        Move(reverse, new SimVector2(5, 15), 30, new MotionParameters(ScaleSlopeSpeed: false));
        check(Near(reverse.Entities.Single().Position.X, 14.88), "opposite heading applies minimum turn speed");
        check(Near(reverse.Entities.Single().Facing, -Math.PI / 30), "opposite heading rotates by one frame budget");
        var stoppedFacing = reverse.Entities.Single().Facing;
        var stoppedPosition = reverse.Entities.Single().Position;
        reverse.SubmitCommand(CommandEnvelope.Stop(3, 0, 3, id));
        reverse.Step();
        check(reverse.Entities.Single().Facing == stoppedFacing && reverse.Entities.Single().Position == stoppedPosition,
            "stop preserves authoritative facing and stops movement");
        var turnInPlace = Create(new SimVector2(15, 15), null);
        Move(turnInPlace, new SimVector2(5, 15), 30,
            new MotionParameters(MinimumTurnScale: 0, ScaleSlopeSpeed: false));
        check(turnInPlace.Entities.Single().Position == new SimVector2(15, 15)
            && turnInPlace.Entities.Single().Facing < 0, "zero speed scale still allows turning");
        for (var i = 0; i < 10; i++) turnInPlace.Step();
        check(turnInPlace.Entities.Single().Position.X < 15, "turning eventually releases zero speed threshold");
        var missing = Create(new SimVector2(15, 15), null);
        Move(missing, new SimVector2(195, 15), 30, null);
        Move(missing, new SimVector2(5, 15), 30, new MotionParameters());
        check(missing.DrainEvents().Single().Detail == "motion_requires_height_field"
            && missing.ReadMoveOrders().Single().Request.Goal == new SimVector2(195, 15),
            "missing terrain rejection preserves previous order");
        var frozen = Create(new SimVector2(15, 15), ramp, Math.PI / 2);
        Move(frozen, new SimVector2(195, 15), 30, new MotionParameters(UphillScale: 0));
        check(frozen.Entities.Single().Position == new SimVector2(15, 15)
            && frozen.Entities.Single().Facing < Math.PI / 2, "zero uphill speed still turns without translating");

        var walls = new byte[48];
        for (var y = 0; y < 4; y++) walls[y * 8 + 3] = 2;
        var cornerGrid = new PathingGrid(8, 6, 10, default, walls);
        var corners = new RtsMatch(MatchConfig.Default, 7, cornerGrid);
        corners.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new SimVector2(15, 15)));
        corners.Step();
        Move(corners, new SimVector2(65, 15), 1200,
            new MotionParameters(ScaleTurnSpeed: false, ScaleSlopeSpeed: false));
        check(Math.Abs(corners.Entities.Single().Facing) <= Math.PI / 30 + 1e-10,
            "crossing multiple waypoints cannot multiply turn time budget");
        for (var i = 0; i < 5; i++) corners.Step();
        check(corners.Entities.Single().Position == new SimVector2(65, 15) && !corners.IsMoving(id)
            && corners.Entities.Single().Velocity == SimVector2.Zero, "motion arrives precisely and clears velocity");
        var arrivalFacing = corners.Entities.Single().Facing;
        corners.Step();
        check(corners.Entities.Single().Facing == arrivalFacing, "arrival retains final authoritative facing");

        // A running slope order, dynamic replan, pending stop and JSON recovery must agree frame by frame.
        var snapshot = uphill.CaptureSnapshot();
        var restored = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), grid, ramp);
        var obstacle = CommandEnvelope.SetObstacle(4, 0, 10, 1, new GridArea(5, 1, 1, 1));
        var stop = CommandEnvelope.Stop(90, 0, 11, id);
        uphill.SubmitCommand(obstacle);
        restored.SubmitCommand(obstacle);
        uphill.SubmitCommand(stop);
        restored.SubmitCommand(stop);
        for (var i = 0; i < 100; i++)
        {
            uphill.Step();
            restored.Step();
            check(uphill.ComputeStateHash() == restored.ComputeStateHash(), "motion recovery and replan agree every frame");
            var roundTrip = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(uphill.CaptureSnapshot())), grid, ramp);
            check(roundTrip.ComputeStateHash() == uphill.ComputeStateHash(), "motion midsegment and pending state round trip");
        }
        var invalid = snapshot with { Navigation = snapshot.Navigation! with { HeightHash = null } };
        try
        {
            RtsMatch.Restore(invalid, grid);
            check(false, "active slope order without height identity rejected");
        }
        catch (InvalidDataException) { check(true, "active slope order without height identity rejected"); }
    }
}
