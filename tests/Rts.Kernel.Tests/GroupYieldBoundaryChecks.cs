using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class GroupYieldBoundaryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        StopAndReplacement(check);
        StoppedPeers(check);
        BlockedExit(check);
        QueuedExit(check);
        SnapshotValidation(check);
    }

    private static (RtsMatch Match, EntityId Requester) StartExit(Action<bool, string> check, bool hostCommand = true)
    {
        var match = GroupYieldChecks.Create(check);
        var centre = match.Entities.Aggregate(FixedVector2.Zero, (sum, entity) => sum + entity.Position) / 24;
        var exiting = match.Entities.OrderBy(entity => (entity.Position - centre).Length).First();
        // The Godot UI submits GroupMove even for a single selection.
        if (hostCommand)
            match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25,
                new([exiting.Id], exiting.Position + new FixedVector2(-800, 0))));
        else
            match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 25, exiting.Id,
                exiting.Position + new FixedVector2(-800, 0), GroupYieldChecks.Footman.Speed, 1, GroupYieldChecks.Footman.Motion));
        for (var i = 0; i < 90 && match.CaptureSnapshot().Cooperation!.Yields.Count == 0; i++) match.Step();
        check(match.CaptureSnapshot().Cooperation!.Yields.Count > 0, "single-selection host command asks old party for exit");
        return (match, exiting.Id);
    }

    private static void StopAndReplacement(Action<bool, string> check)
    {
        foreach (var replace in new[] { false, true })
        {
            var (match, requester) = StartExit(check);
            var helper = match.CaptureSnapshot().Cooperation!.Yields.First();
            var helperId = new EntityId(helper.EntityId);
            var position = match.Entities.Single(entity => entity.Id == helperId).Position;
            // Invalid replacement must preserve permission and the temporary path.
            match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 26, helperId,
                new(-100, -100), GroupYieldChecks.Footman.Speed, 1, GroupYieldChecks.Footman.Motion));
            match.Step();
            check(match.DrainEvents().Any(item => item.Kind == MatchEventKind.CommandRejected), "invalid helper replacement is rejected");
            check(match.CaptureSnapshot().Cooperation!.Yields.Any(task => task.EntityId == helper.EntityId),
                "invalid replacement preserves active yield");
            if (replace)
                match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 27, helperId,
                    position + new FixedVector2(0, 800), GroupYieldChecks.Footman.Speed, 1, GroupYieldChecks.Footman.Motion));
            else match.SubmitCommand(CommandEnvelope.Stop(match.Frame + 1, 0, 27, helperId));
            match.Step();
            position = match.Entities.Single(entity => entity.Id == helperId).Position;
            check(!match.CaptureSnapshot().Cooperation!.Yields.Any(task => task.EntityId == helper.EntityId), "player takes helper out of old yield");
            var resumed = Restore(match.CaptureSnapshot());
            for (var i = 0; i < 100; i++)
            {
                match.Step(); resumed.Step();
                check(match.ComputeStateHash() == resumed.ComputeStateHash(), "cancelled yield resumes identically");
                check(!match.CaptureSnapshot().Cooperation!.Yields.Any(task => task.EntityId == helper.EntityId), "old group never takes helper back");
                if (!replace) check(match.Entities.Single(entity => entity.Id == helperId).Position == position, "Stop helper remains immovable");
            }
            check(match.ReadCurrentOrder(helperId)?.Kind != UnitOrderKind.Move || replace, "Stop remains the current order");
        }
        var (cancelled, exiting) = StartExit(check);
        cancelled.SubmitCommand(CommandEnvelope.Stop(cancelled.Frame + 1, 0, 26, exiting));
        cancelled.Step();
        check(cancelled.CaptureSnapshot().Cooperation!.Yields.Count == 0, "stopping requester cancels all its temporary help");
        var settled = cancelled.Entities.ToArray();
        for (var i = 0; i < 90; i++) cancelled.Step();
        check(settled.SequenceEqual(cancelled.Entities), "cancelled helpers remain at safe positions without returning");
    }

    private static void StoppedPeers(Action<bool, string> check)
    {
        var (match, exiting) = StartExit(check, hostCommand: false);
        var positions = match.Entities.Where(entity => entity.Id != exiting).ToDictionary(entity => entity.Id, entity => entity.Position);
        long sequence = 26;
        foreach (var id in positions.Keys) match.SubmitCommand(CommandEnvelope.Stop(match.Frame + 1, 0, sequence++, id));
        match.DrainEvents();
        for (var i = 0; i < 180; i++) match.Step();
        check(positions.All(pair => match.Entities.Single(entity => entity.Id == pair.Key).Position == pair.Value),
            "Stop peers cannot be pushed apart even when the changed layout leaves an exit");
        var outcomes = match.DrainEvents();
        check(outcomes.Any(item => item.EntityId == exiting
            && item.Kind is MatchEventKind.MoveFailed or MatchEventKind.MoveCompleted)
            && !match.ReadMoveOrders().Any(order => order.EntityId == exiting.Value),
            "stopped peers leave the requester a finite completed or failed outcome");
    }

    private static void BlockedExit(Action<bool, string> check)
    {
        // A deterministic closed ring, independent of soft-party placement changes.
        // Adjacent body centres are >62 apart, but their requester-exclusion circles overlap.
        var match = new RtsMatch(MatchConfig.Default, 7, GroupYieldChecks.Grid,
            GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);
        var centre = new FixedVector2(2064, 2064);
        FixedVector2[] offsets = [new(82, 0), new(58, 58), new(0, 82), new(-58, 58),
            new(-82, 0), new(-58, -58), new(0, -82), new(58, -58)];
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, centre, 1));
        for (var i = 0; i < offsets.Length; i++)
            match.SubmitCommand(CommandEnvelope.Spawn(1, 0, i + 1, centre + offsets[i], 1));
        match.Step(); match.DrainEvents();
        check(match.Entities.Count == 9, "closed Stop ring spawns nine non-overlapping bodies");
        var positions = match.Entities.Skip(1).ToDictionary(entity => entity.Id, entity => entity.Position);
        long sequence = 9;
        foreach (var id in positions.Keys) match.SubmitCommand(CommandEnvelope.Stop(2, 0, sequence++, id));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, sequence, new(1), centre + new FixedVector2(-800, 0),
            270, 1, GroupYieldChecks.Footman.Motion));
        for (var frame = 0; frame < 180; frame++) match.Step();
        check(positions.All(pair => match.Entities.Single(entity => entity.Id == pair.Key).Position == pair.Value),
            "closed Stop ring bodies remain immovable");
        check(match.DrainEvents().Any(item => item.Kind == MatchEventKind.MoveFailed && item.EntityId.Value == 1),
            "a truly closed Stop ring produces finite failure without pushing through");
    }

    private static void QueuedExit(Action<bool, string> check)
    {
        var match = GroupYieldChecks.Create(check);
        // Start another group move, then append a reverse order to an interior member.
        var ids = match.Entities.Select(entity => entity.Id).ToArray();
        match.SubmitCommand(CommandEnvelope.MoveGroup(match.Frame + 1, 0, 25, new(ids, new(3056, 2064))));
        match.Step();
        var interior = match.ReadUnitOrders().Where(queue => queue.Current?.Group is not null)
            .OrderBy(queue => (queue.Current!.Move!.Goal - new FixedVector2(3056, 2064)).Length).First();
        var id = new EntityId(interior.EntityId);
        var goal = new FixedVector2(1808, 2064);
        match.SubmitCommand(CommandEnvelope.MoveTo(match.Frame + 1, 0, 26, id, goal,
            GroupYieldChecks.Footman.Speed, 1, GroupYieldChecks.Footman.Motion, OrderMode.Append));
        match.DrainEvents();
        var completed = 0;
        for (var i = 0; i < 600; i++)
        {
            match.Step();
            var events = match.DrainEvents();
            completed += events.Count(item => item.EntityId == id && item.Kind == MatchEventKind.MoveCompleted);
            check(!events.Any(item => item.EntityId == id && item.Kind == MatchEventKind.MoveFailed), "queued interior reverse order does not fail");
            if (i % 19 == 0) check(Restore(match.CaptureSnapshot()).ComputeStateHash() == match.ComputeStateHash(), "active party/queue/yield snapshot restores");
            if (completed == 2) break;
        }
        check(completed == 2 && match.Entities.Single(entity => entity.Id == id).Position == goal,
            "queued reverse command completes once after old group completion");
    }

    private static void SnapshotValidation(Action<bool, string> check)
    {
        var (match, _) = StartExit(check);
        var saved = match.CaptureSnapshot();
        Reject(saved with { FormatVersion = 12 }, check, "v12 cannot silently discard cooperation authority");
        Reject(saved with { Cooperation = null }, check, "missing cooperation state rejected");
        var tasks = saved.Cooperation!.Yields.ToArray();
        tasks[0] = tasks[0] with { RequesterId = tasks[0].EntityId };
        Reject(saved with { Cooperation = saved.Cooperation with { Yields = tasks } }, check, "self-requesting yield rejected");
        var members = saved.Cooperation.Members.ToArray();
        members[0] = members[0] with { GroupId = saved.NextGroupId };
        Reject(saved with { Cooperation = saved.Cooperation with { Members = members } }, check, "unknown party identity rejected");
        check(SnapshotDiff.FindFirst(saved, saved with { Cooperation = saved.Cooperation with { Yields = tasks } })?.Path == "cooperation",
            "snapshot differences include temporary yield authority");
    }

    private static RtsMatch Restore(MatchSnapshot saved) => RtsMatch.Restore(
        SnapshotJson.Deserialize(SnapshotJson.Serialize(saved)), GroupYieldChecks.Grid, GroupYieldChecks.Heights, [GroupYieldChecks.Footman]);

    private static void Reject(MatchSnapshot saved, Action<bool, string> check, string message)
    {
        var rejected = false;
        try { Restore(saved); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, message);
    }
}
