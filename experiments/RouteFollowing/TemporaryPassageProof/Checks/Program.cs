using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Movement.Prototype;

var folder = Path.GetFullPath(args[0]);
Directory.CreateDirectory(folder);
var disposable = RouteScenarios.Create(1);
var authority = disposable.Save();
var preparation = Stopwatch.StartNew();
RoutePreparation.Run();
preparation.Stop();
if (disposable.Save() != authority) throw new Exception("Preparation changed authority");
Console.WriteLine("preparation_ms=" + preparation.Elapsed.TotalMilliseconds.ToString("R"));
var all = true;
var cases = new (int Frame, ulong Id)[] { (368, 1), (369, 23), (442, 22) };
foreach (var (frame, id) in cases.Where(item => args.Length < 2 || item.Id == ulong.Parse(args[1])))
{
    using var stream = typeof(Program).Assembly.GetManifestResourceStream($"before-{frame}-{id}-retry.json")!;
    using var reader = new StreamReader(stream);
    var input = reader.ReadToEnd();
    var state = JsonSerializer.Deserialize<RouteSnapshot>(input)!;
    var world = RouteWorld.Load(JsonSerializer.Serialize(state with { Format = RouteSettings.SnapshotFormat }));
    var fixedBodies = world.Units.Where(b => b.Phase is WalkPhase.Idle or WalkPhase.Stopped)
        .ToDictionary(b => b.Id, b => b.Position);
    var observations = new FrameSpikeLog(1200);
    using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var arrived = 0L;
    var stable = 0;
    var over = 0;
    var peak = 0.0;
    var events = new List<RouteEvent>();
    var firstClosed = false;
    var firstSucceeded = false;
    var originalGoals = new Dictionary<ulong, Rts.Kernel.Numerics.FixedVector2>();
    File.WriteAllText(Path.Combine(folder, $"input-{id}.json"), input);
    try
    {
        for (var tick = 0; tick < 1200; tick++)
        {
            var resumed = RouteWorld.Load(world.Save());
            var prior = world.Units.ToDictionary(b => b.Id);
            var counters = FrameSpikeLog.ReadCounters();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            world.Step();
            var end = Stopwatch.GetTimestamp();
            var after = FrameSpikeLog.ReadCounters();
            var ms = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            observations.Record((int)world.Frame, start, end, GC.GetAllocatedBytesForCurrentThread() - bytes, counters, after);
            peak = Math.Max(peak, ms); over += ms > 1000.0 / 30 ? 1 : 0;
            resumed.Step();
            if (world.Hash() != resumed.Hash() || !world.Events.SequenceEqual(resumed.Events))
                throw new Exception("Per-frame restore differs");
            digest.AppendData(Encoding.UTF8.GetBytes(world.Hash() + "\n" + JsonSerializer.Serialize(world.Events) + "\n"));
            var claims = RoutePlacements.Claims(world.Units);
            foreach (var claim in claims)
                if (claims.Any(c => c.Task == claim.Task && c.Member != claim.Member && (c.Point - claim.Point).Length < 64))
                    throw new Exception("Overlapping final claims");
            foreach (var body in world.Units)
            {
                if ((body.Position - prior[body.Id].Position).Length > 9 + Rts.Kernel.Numerics.SimScalar.FromRaw(32))
                    throw new Exception("Teleport");
                if (fixedBodies.TryGetValue(body.Id, out var point) && body.Position != point)
                    throw new Exception("Stop or ordinary Idle moved");
                if (world.Units.Any(other => other.Id > body.Id && (other.Position - body.Position).Length < 62))
                    throw new Exception("Body overlap");
            }
            events.AddRange(world.Events);
            var member = world.Units.Single(b => b.Id == id);
            if (tick == 0 && world.Paths.FirstOrDefault(t => t.Yield?.Requester == id)?.Yield is { Temporary: not null } initial)
                originalGoals = initial.Members.ToDictionary(helper => helper, helper => prior[helper].Endpoint);
            if (!firstClosed)
            {
                if (originalGoals.Any(pair => world.Units.Single(b => b.Id == pair.Key).Endpoint != pair.Value))
                    throw new Exception("Temporary passage changed final goal");
                firstSucceeded = world.Events.Any(e => e.Id == id && e.Kind == "yield_finished");
                firstClosed = firstSucceeded || world.Events.Any(e => e.Id == id && e.Kind == "yield_cancelled");
            }
            Console.WriteLine("proof_tick=" + JsonSerializer.Serialize(new { id, world.Frame, elapsed_ms = ms,
                first = tick == 0, hash = world.Hash(), world.Events, member, plans = world.Paths.Where(t => t.Yield is not null) }));
            if (world.Events.Any(e => e.Kind is "yield_started" or "yield_finished" or "yield_cancelled"))
                File.WriteAllText(Path.Combine(folder, $"world-{id}-{world.Frame}.json"), world.Save());
            if (member.Phase == WalkPhase.Failed || firstClosed && !firstSucceeded) break;
            if (member.Task == 0 && member.Pending.Count == 0 && member.Phase == WalkPhase.Idle)
            {
                if (arrived == 0) arrived = world.Frame;
                else if (++stable == 90) break;
            }
        }
    }
    finally { observations.Report(24, 0, $"temporary-passage-{id}"); }
    var helperFailures = world.Units.Where(body => originalGoals.ContainsKey(body.Id) && body.Phase == WalkPhase.Failed)
        .Select(body => body.Id).ToArray();
    var passed = firstSucceeded && helperFailures.Length == 0 && originalGoals.Count > 0 && arrived > 0 && stable == 90 && over == 0;
    File.WriteAllText(Path.Combine(folder, $"final-{id}.json"), world.Save());
    Console.WriteLine("proof_result=" + JsonSerializer.Serialize(new { id, passed, firstSucceeded, helperFailures, arrived, stable, peak_ms = peak, over_budget = over,
        hash = world.Hash(), trace_sha256 = Convert.ToHexString(digest.GetHashAndReset()), events,
        input_sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))) }));
    all &= passed;
    if (!passed) break;
}
Environment.ExitCode = all ? 0 : 1;
