using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Command and restore ownership while the real 22/8/2 connection executes.</summary>
internal static class RequesterConnectionChecks
{
    private static int _assertions;

    internal static void Run()
    {
        using var stream = typeof(RequesterConnectionChecks).Assembly.GetManifestResourceStream("before-442-22-retry.json")!;
        using var reader = new StreamReader(stream);
        var input = JsonSerializer.Deserialize<RouteSnapshot>(reader.ReadToEnd())!;
        var world = RouteWorld.Load(JsonSerializer.Serialize(input with { Format = RouteSettings.SnapshotFormat }));
        Advance(world);
        var outward = world.Save();
        for (var tick = 0; tick < 90 && Plan(world)?.Temporary?.Phase != TemporaryYieldPhase.Passing; tick++)
            Advance(world);
        Check(Plan(world)?.Temporary is { HasRequesterConnection: true, Phase: TemporaryYieldPhase.Passing },
            "Real helper execution opens the requester connection");
        var passing = world.Save();
        foreach (var seed in new[] { outward, passing })
        {
            StopAndReplace(seed);
            QueueAndReject(seed);
            MapAndRestore(seed);
        }
        Console.WriteLine("requester_boundary_assertions=" + _assertions);
    }

    private static void StopAndReplace(string seed)
    {
        foreach (var id in new ulong[] { 22, 8, 2 })
        {
            var stopped = RouteWorld.Load(seed);
            var position = stopped.Units.Single(body => body.Id == id).Position;
            Check(stopped.Stop([id]), "Stop is queued");
            Advance(stopped);
            Check(Plan(stopped) is null, "Stop releases the original connection owner");
            for (var tick = 0; tick < 90; tick++)
            {
                var body = stopped.Units.Single(body => body.Id == id);
                Check(body.Position == position && body.Phase == WalkPhase.Stopped
                    && body.Task == 0 && body.Pending.Count == 0 && body.Velocity == default,
                    "The old plan never moves a stopped participant");
                Advance(stopped);
            }

            var replaced = RouteWorld.Load(seed);
            Check(replaced.Move([id], position + new FixedVector2(1, 0)), "Replacement is queued");
            Advance(replaced);
            Check(replaced.Events.Any(ev => ev.Id == id && ev.Kind == "accepted"), "Replacement is accepted");
            Check(Plan(replaced) is null, "Replacement releases the original connection owner");
            var task = replaced.Units.Single(body => body.Id == id).Task;
            for (var tick = 0; tick < 12; tick++)
            {
                Advance(replaced);
                var body = replaced.Units.Single(body => body.Id == id);
                Check(body.Task == task || body.Task == 0, "The old task cannot retake a replaced participant");
            }
        }
    }

    private static void QueueAndReject(string seed)
    {
        foreach (var id in new ulong[] { 22, 8 })
        {
            var queued = RouteWorld.Load(seed);
            var body = queued.Units.Single(body => body.Id == id);
            Check(queued.Move([id], body.Position + new FixedVector2(1, 0), append: true), "Append is queued");
            Advance(queued);
            body = queued.Units.Single(member => member.Id == id);
            Check(queued.Events.Any(ev => ev.Id == id && ev.Kind == "accepted") && body.Pending.Count == 1,
                "Append preserves the current task and the queued task");
            Check((Plan(queued) is not null) == (id == 22),
                "Requester append retains passage; helper append releases cooperation");
            for (var tick = 0; tick < 12; tick++) Advance(queued);
        }
        var rejected = RouteWorld.Load(seed);
        var before = rejected.Save();
        Check(!rejected.Move([22], new FixedVector2(-32, -32)), "Out-of-map command is rejected");
        Check(rejected.Save() == before, "Invalid replacement leaves authority untouched");
        Advance(rejected);
        Check(Plan(rejected)?.Temporary?.HasRequesterConnection == true,
            "Invalid replacement does not cancel the admitted connection");
    }

    private static void MapAndRestore(string seed)
    {
        var changed = RouteWorld.Load(seed);
        Check(changed.SetWall(new(3, 3, 1, 1), true), "A valid map revision is accepted");
        Advance(changed);
        Check(Plan(changed) is null, "Map revision releases the old connection and its derived lease");
        for (var tick = 0; tick < 12; tick++) Advance(changed);

        var world = RouteWorld.Load(seed);
        var original = world.Save();
        var publicPlan = Plan(world)!;
        ((FixedVector2[])publicPlan.Temporary!.Targets)[0] = default;
        ((FixedVector2[])publicPlan.Temporary.Origins)[0] = default;
        ((ulong[])publicPlan.Members)[0] = 999;
        Check(world.Save() == original, "Public plan arrays do not mutate authority");
        var state = JsonSerializer.Deserialize<RouteSnapshot>(seed)!;
        Reject(state with { Format = 9 });
        var task = state.Tasks.Single(route => route.Yield?.Requester == 22);
        var plan = task.Yield!;
        var badTarget = task with { Yield = plan with
        {
            Temporary = plan.Temporary! with { RequesterTarget = plan.Temporary.RequesterStart }
        } };
        Reject(state with { Tasks = state.Tasks.Select(route => route.Id == task.Id ? badTarget : route).ToArray() });
    }

    private static RouteYieldPlan? Plan(RouteWorld world) =>
        world.Paths.Select(task => task.Yield).SingleOrDefault(plan => plan?.Requester == 22 && plan.StartedAt == 442);

    private static void Advance(RouteWorld world)
    {
        var resumed = RouteWorld.Load(world.Save());
        world.Step();
        resumed.Step();
        Check(world.Save() == resumed.Save() && world.Events.SequenceEqual(resumed.Events),
            "Pending commands and active connection restore identically");
    }

    private static void Reject(RouteSnapshot state)
    {
        try { RouteWorld.Load(JsonSerializer.Serialize(state)); }
        catch (InvalidDataException) { _assertions++; return; }
        throw new Exception("Corrupt or old connection snapshot was accepted");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _assertions++;
    }
}
