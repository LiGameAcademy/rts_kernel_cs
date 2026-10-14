using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NumericPrototype.Movement;
using Rts.Kernel;

namespace NumericPrototype.Groups;

internal static class GroupPlanningChecks
{
    internal sealed record ProbeResult(string WorldIdentity, int Members, int Frames, int Activations,
        int SearchingSnapshots, bool PreparationFinishedWhileMatching, string TraceHash);

    internal static string Run(Action<bool, string> check)
    {
        const int width = 64;
        var world = new MovementWorld(JsonSerializer.Serialize(new
        {
            width, height = width, cellSize = 32, origin = new { x = -1024, y = -1024 },
            cellsBase64 = Convert.ToBase64String(new byte[width * width])
        }), JsonSerializer.Serialize(new
        {
            tilepointWidth = 17, tilepointHeight = 17, tileSize = 128,
            centerOffset = new { x = -1024, y = -1024 }, heights = new int[17 * 17]
        }));
        var points = Enumerable.Range(0, 140).Select(index => world.Center(index % 14 + 20, index / 14 + 20)).ToArray();
        var input = Input(points, 65, 1);
        var result = Trace(world, input, check);
        var productionBudget = Trace(world, input, check, GroupPlanningQueue.ProductionColumnsPerFrame);
        check(productionBudget.Activations == result.Activations, "production budget activates the same member count");
        check(result.SearchingSnapshots > 0 && result.PreparationFinishedWhileMatching,
            "fixtures exercise simultaneous prepared candidates and unfinished matching");
        var candidates = GroupPlanningJob.Candidates(world, world.Center(1, 1));
        check(candidates[0] == new CandidateCell(1, 1), "ideal center is first candidate");
        check(candidates.Take(5).SequenceEqual(new CandidateCell[]
            { new(1, 1), new(1, 0), new(0, 1), new(2, 1), new(1, 2) }),
            "equal-distance candidates sort by Y then X");
        var job = new GroupPlanningJob(input, world);
        job.Advance(world, 1, 64);
        var savedJob = RoundTrip(job.Capture());
        var resumedJob = GroupPlanningJob.Restore(savedJob, world);
        check(job.CandidateTrace() == resumedJob.CandidateTrace(), "candidate cache reconstructs from prepared count");
        RejectJob(savedJob with { Prepared = 66 }, world, check);
        RejectJob(savedJob with { Canceled = [999999] }, world, check);
        savedJob.Input.Ideals[0] = world.Center(0, 0);
        check(job.Capture().Input.Ideals[0] == input.Ideals[0], "snapshot input arrays are detached");
        var allCanceled = new GroupPlanningQueue(world);
        allCanceled.Enqueue(input);
        foreach (var member in input.Members) allCanceled.CancelMember(member.Id);
        check(allCanceled.Step().Length == 0 && allCanceled.Capture().Jobs.Length == 0,
            "fully canceled head is removed without activation");
        var control = new GroupPlanningJob(input, world);
        var canceledControl = new GroupPlanningJob(input, world);
        canceledControl.Cancel(input.Leader);
        canceledControl.Cancel(input.Members[1].Id);
        while (!control.Ready) control.Advance(world, 2097152, 64);
        while (!canceledControl.Ready) canceledControl.Advance(world, 2097152, 64);
        check(canceledControl.Assignments().SequenceEqual(control.Assignments()
            .Where(item => item.Member != input.Leader && item.Member != input.Members[1].Id)),
            "cancellation filters original assignment without rematching survivors");
        try
        {
            allCanceled.Enqueue(input);
            check(false, "reusing a retired group identity must fail");
        }
        catch (ArgumentException) { check(true, "retired group identity is not reused"); }
        var fallback = input with { Leader = ulong.MaxValue };
        var fallbackJob = new GroupPlanningJob(fallback, world);
        while (!fallbackJob.Ready) fallbackJob.Advance(world, 2097152, 64);
        check(fallbackJob.Assignments().Single(item => item.Member == input.Members[0].Id).Slot == 0,
            "absent leader falls back to first original member");
        RejectQueue(new GroupPlanningQueue(world).Capture() with { WorldIdentity = "different" }, world, check);
        RejectQueue(new GroupPlanningQueue(world).Capture() with { Frame = -1 }, world, check);
        return Hash(result.TraceHash + ";" + productionBudget.TraceHash);
    }

    internal static GroupPlanInput Input(FixedPoint[] points, int count, ulong id)
    {
        var rng = new DeterministicRng(68);
        var shuffled = points.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var other = (int)(rng.NextUInt64() % (ulong)(index + 1));
            (shuffled[index], shuffled[other]) = (shuffled[other], shuffled[index]);
        }
        var members = Enumerable.Range(0, count).Select(index => new GroupMember((ulong)index + 1,
            shuffled[index])).ToArray();
        return new(id, members, points.Take(count).ToArray(), members[count / 2].Id, false);
    }

    internal static ProbeResult Trace(MovementWorld world, GroupPlanInput input, Action<bool, string> check, int columnBudget = 128)
    {
        // Reduced diagnostic budget deliberately forces mid-search suspension; no production constant changes.
        var queue = new GroupPlanningQueue(world, columnBudget: columnBudget);
        queue.Enqueue(input);
        queue.Enqueue(input with { Id = input.Id + 1, UnitAi = true });
        check(queue.CancelMember(input.Members[0].Id, fromUnitAi: true) == 1,
            "unit AI cannot cancel protected player plan");
        check(queue.Capture().Jobs[0].Canceled.Length == 0, "player job remains protected");
        var trace = new StringBuilder();
        var searching = 0;
        var preparedDuringSearch = false;
        var activations = 0;
        var canceled = input.Members[1].Id;
        var anchor = input.Leader;
        var previousAssignments = new Dictionary<ulong, int>();
        for (var frame = 1; frame <= 20000; frame++)
        {
            if (frame == 2)
            {
                check(queue.CancelMember(canceled) == 2, "player replacement cancels member in both pending jobs");
                check(queue.CancelMember(anchor) == 2, "canceling leader preserves original matching dimension");
            }
            // An append does not call CancelMember or mutate the planner; order queue semantics are outside this harness.
            var saved = RoundTrip(queue.Capture());
            var restored = GroupPlanningQueue.Restore(world, saved);
            var events = queue.Step();
            var restoredEvents = restored.Step();
            var state = queue.Capture();
            check(JsonSerializer.Serialize(state) == JsonSerializer.Serialize(restored.Capture())
                && JsonSerializer.Serialize(events) == JsonSerializer.Serialize(restoredEvents),
                "full FIFO snapshot resumes with identical activation frame and event sequence");
            check(events.Select(item => item.Group).Distinct().Count() <= 1, "one group commits per frame");
            foreach (var job in state.Jobs)
                if (job.Matching is { Searching: true })
                {
                    searching++;
                    preparedDuringSearch |= job.Prepared == input.Members.Length;
                }
            if (events.Length == 0)
                check(state.Active.All(item => previousAssignments.TryGetValue(item.Member, out var slot) && slot == item.Slot),
                    "pending work leaves prior activation untouched");
            else
            {
                check(events.All(item => item.Member != canceled && item.Member != anchor), "canceled members never activate");
                check(events.All(item => item.Slot > 0), "canceling anchor never reallocates its slot zero");
                check(events.Select(item => item.Slot).Distinct().Count() == events.Length, "stable slots remain unique");
                activations += events.Length;
            }
            previousAssignments = state.Active.ToDictionary(item => item.Member, item => item.Slot);
            trace.Append(JsonSerializer.Serialize(state)).Append(JsonSerializer.Serialize(events));
            if (state.Jobs.Length == 0)
            {
                check(activations == 2 * input.Members.Length - 5, "each noncanceled member activates exactly once per job");
                check(queue.Step().Length == 0, "committed tasks never emit duplicate activation");
                return new(world.Identity, input.Members.Length, frame, activations, searching,
                    preparedDuringSearch, Hash(trace.ToString()));
            }
        }
        throw new InvalidOperationException("Group diagnostic frame bound exceeded.");
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void RejectJob(GroupJobSnapshot state, MovementWorld world, Action<bool, string> check)
    {
        try { GroupPlanningJob.Restore(state, world); check(false, "invalid job accepted"); }
        catch (InvalidDataException) { check(true, "invalid job rejected"); }
    }
    private static void RejectQueue(GroupQueueSnapshot state, MovementWorld world, Action<bool, string> check)
    {
        try { GroupPlanningQueue.Restore(world, state); check(false, "invalid queue accepted"); }
        catch (InvalidDataException) { check(true, "invalid queue rejected"); }
    }
}
