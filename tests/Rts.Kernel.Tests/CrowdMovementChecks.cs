using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class CrowdMovementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new[] { new MovementDefinition(1, 60, 4) };
        var grid = new PathingGrid(32, 32, 10, SimVector2.Zero, new byte[1024]);
        RtsMatch Create(PathingGrid? map = null, double speed = 60)
        {
            var m = new RtsMatch(MatchConfig.Default, 7, map ?? grid,
                movementDefinitions: new[] { definition[0] with { Speed = speed } });
            return m;
        }
        var m = Create();
        m.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(35, 155), 1));
        m.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(155, 155), 1));
        m.Step();
        m.SubmitCommand(CommandEnvelope.Stop(2, 0, 2, new(2)));
        m.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 3, new(1), new(275, 155), 60));
        m.Step();
        var otherPosition = m.Entities.Single(e => e.Id.Value == 2).Position;
        for (var frame = 0; frame < 180; frame++)
        {
            m.Step();
            var entities = m.Entities.ToArray();
            var dx = entities[0].Position.X - entities[1].Position.X;
            var dy = entities[0].Position.Y - entities[1].Position.Y;
            check(dx * dx + dy * dy >= 64 - 1e-8, $"stationary body separation frame {frame}");
            if (frame % 10 == 0)
            {
                var resume = RtsMatch.Restore(SnapshotJson.Deserialize(SnapshotJson.Serialize(m.CaptureSnapshot())),
                    grid, movementDefinitions: definition);
                m.Step(); resume.Step();
                check(m.ComputeStateHash() == resume.ComputeStateHash(), $"detour/wait continuation restores frame {frame}");
            }
        }
        check(m.Entities.First().Position == new SimVector2(275, 155), "moving unit detours around stationary body");
        check(m.Entities.Last().Position == otherPosition && m.ReadCurrentOrder(new(2))!.Kind == UnitOrderKind.Stop,
            "stationary player Stop is never pushed");

        var flags = Enumerable.Repeat((byte)2, 32 * 3).ToArray();
        for (var x = 0; x < 32; x++) flags[32 + x] = 0;
        var corridor = new PathingGrid(32, 3, 10, SimVector2.Zero, flags);
        var blocked = Create(corridor);
        blocked.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(35, 15), 1));
        blocked.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(155, 15), 1));
        blocked.Step(); blocked.DrainEvents();
        blocked.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, new(1), new(275, 15), 60));
        blocked.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 3, new(1), new(25, 15), 60, mode: OrderMode.Append));
        for (var i = 0; i < 240; i++) blocked.Step();
        var events = blocked.DrainEvents();
        check(events.Count(e => e.Detail == "crowd_blocked_timeout") == 1, "three seconds of continuous crowd blockage reports one failure");
        check(blocked.Entities.First().Position == new SimVector2(25, 15), "timeout continues next queued goal");
        check(blocked.Entities.Last().Position == new SimVector2(155, 15), "corridor blocker remains stationary");

        var high = Create(speed: 6000);
        high.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(35, 155), 1));
        high.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(135, 155), 1)); high.Step();
        high.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, new(1), new(275, 155), 6000)); high.Step();
        check(high.Entities.First().Position != new SimVector2(235, 155), "swept test prevents high-speed tunneling through a body");

        var overlap = Create();
        overlap.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(35, 35), 1));
        overlap.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(36, 35), 1));
        overlap.SubmitCommand(CommandEnvelope.Spawn(1, 0, 2, new(1, 1), 1)); overlap.Step();
        check(overlap.Entities.Count == 1 && overlap.CaptureSnapshot().NextEntityId == 2, "invalid ground births reject before allocating IDs");
        var waiting = high.CaptureSnapshot();
        var moves = waiting.Moves!.ToArray();
        foreach (var invalidMove in new[] { moves[0] with { WaitFrames = -1 }, moves[0] with { WaitFrames = 90 },
            moves[0] with { RetryAfterFrame = waiting.Frame + 7 } })
        {
            var bad = moves.ToArray(); bad[0] = invalidMove;
            var refused = false;
            try { SnapshotValidator.Validate(waiting with { Moves = bad }); }
            catch (InvalidDataException) { refused = true; }
            check(refused, "invalid crowd wait/retry cannot enter a restored match");
        }
        var changed = moves.ToArray(); changed[0] = changed[0] with { WaitFrames = changed[0].WaitFrames + 1 };
        check(SnapshotDiff.FindFirst(waiting, waiting with { Moves = changed })?.Path == "moves[0].waitFrames",
            "crowd blockage clock participates in snapshot differences");
        var saved = m.CaptureSnapshot();
        var bodies = saved.Entities.ToArray();
        bodies[1] = bodies[1] with { PositionX = bodies[0].PositionX, PositionY = bodies[0].PositionY };
        var rejected = false;
        try { RtsMatch.Restore(saved with { Entities = bodies }, grid, movementDefinitions: definition); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "overlapping ground bodies cannot be forged through a snapshot");
        var opposing = Create();
        opposing.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(55, 155), 1));
        opposing.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(255, 155), 1)); opposing.Step();
        opposing.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, new(1), new(255, 155), 60));
        opposing.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 3, new(2), new(55, 155), 60));
        for (var frame = 0; frame < 240; frame++)
        {
            opposing.Step();
            var a = opposing.Entities.First().Position; var b = opposing.Entities.Last().Position;
            var dx = a.X - b.X; var dy = a.Y - b.Y;
            check(dx * dx + dy * dy >= 64 - 1e-8, $"opposing sweep separation frame {frame}");
        }
        check(opposing.Entities.First().Position == new SimVector2(255, 155)
            && opposing.Entities.Last().Position == new SimVector2(55, 155), "opposing movers route past each other on open ground");

        var mixedDefinitions = new[] { new MovementDefinition(1, 60, 4), new MovementDefinition(2, 45, 9) };
        var mixed = new RtsMatch(MatchConfig.Default, 7, grid, movementDefinitions: mixedDefinitions);
        mixed.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new(55, 155), 2));
        mixed.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new(155, 155), 1)); mixed.Step();
        mixed.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, new(1), new(255, 155), 45, 1));
        for (var frame = 0; frame < 220; frame++)
        {
            mixed.Step();
            var a = mixed.Entities.First().Position; var b = mixed.Entities.Last().Position;
            var dx = a.X - b.X; var dy = a.Y - b.Y;
            check(dx * dx + dy * dy >= 169 - 1e-8, $"mixed radius detour separation frame {frame}");
        }
        check(mixed.Entities.First().Position == new SimVector2(255, 155), "mixed-size actor preserves its frozen clearance during detour");
        Console.WriteLine("Crowd movement targeted checks passed");
    }
}
