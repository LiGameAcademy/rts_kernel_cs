using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Exact geometry parity and frozen short-connection state/event continuations.</summary>
internal static class RouteConnectionChecks
{
    private sealed record ExpectedFrame(long Frame, string Hash, string Events);

    internal static bool Run(Action<string> output)
    {
        output($"connection_geometry_cases={Geometry()}");
        var passed = true;
        foreach (var frame in new[] { 150, 251 }) passed &= Frozen(frame, output);
        return passed;
    }

    private static int Geometry()
    {
        var clear = typeof(RouteWorld).Assembly.GetType("Rts.Movement.Prototype.RouteConnections", true)!
            .GetMethod("ClearOfBodies", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<FixedVector2, FixedVector2, Walker[], bool>>();
        var overlap = typeof(SimScalar).Assembly.GetType("Rts.Kernel.Navigation.CrowdGeometry", true)!
            .GetMethod("Overlap", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<FixedVector2, FixedVector2, SimScalar, FixedVector2, FixedVector2, SimScalar, bool>>();
        var world = new RouteWorld();
        world.Spawn(new(400, 500));
        var template = world.Units[0];
        var segments = new (FixedVector2 From, FixedVector2 To)[]
        {
            (default, default), (default, new(128, 0)), (new(128, 0), default),
            (default, new(0, 128)), (default, new(128, 128)), (new(-128, 64), new(128, -64))
        };
        var cases = 0;
        foreach (var (from, to) in segments)
        {
            Check(clear(from, to, []), "empty body set");
            cases++;
            var reach = 62L * SimScalar.Scale;
            long[] Axes(long a, long b) => [Math.Min(a, b) - reach - 1, Math.Min(a, b) - reach,
                Math.Min(a, b) - reach + 1, (a + b) / 2,
                Math.Max(a, b) + reach - 1, Math.Max(a, b) + reach, Math.Max(a, b) + reach + 1];
            foreach (var x in Axes(from.X.Raw, to.X.Raw))
            foreach (var y in Axes(from.Y.Raw, to.Y.Raw))
            {
                var body = template with { Position = FixedVector2.FromRaw(x, y) };
                var far = template with { Position = from + new FixedVector2(-500, -500) };
                foreach (var bodies in new Walker[][] { [body], [far, body], [body, far] })
                {
                    var expected = bodies.All(other => !overlap(from, to, 31, other.Position, other.Position, 31));
                    Check(clear(from, to, bodies) == expected, "AABB versus exact capsule, contact/Raw/diagonal/point");
                    cases++;
                }
            }
        }
        return cases;
    }

    private static bool Frozen(int first, Action<string> output)
    {
        var fixture = "connection-before-" + first;
        var world = RouteWorld.Load(Read(fixture + ".json"));
        var expected = JsonSerializer.Deserialize<ExpectedFrame[]>(Read(fixture + ".expected.json"))!;
        Check(expected.Length == 30, "complete frozen reference");
        var samples = new List<object>();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var peak = 0.0;
        var over = 0;
        for (var tick = 0; tick < expected.Length; tick++)
        {
            var restored = RouteWorld.Load(world.Save());
            var timer = Stopwatch.StartNew();
            world.Step();
            timer.Stop();
            var ms = timer.Elapsed.TotalMilliseconds;
            peak = Math.Max(peak, ms);
            over += ms > 1000.0 / 30 ? 1 : 0;
            var hash = world.Hash();
            var events = JsonSerializer.Serialize(world.Events);
            Check(world.Frame == expected[tick].Frame && hash == expected[tick].Hash
                && events == expected[tick].Events, $"old frozen state/events at frame {world.Frame}");
            restored.Step();
            Check(restored.Hash() == hash && restored.Events.SequenceEqual(world.Events), "frozen JSON continuation");
            RoutePlacementChecks.AssertReserved(world);
            digest.AppendData(Encoding.UTF8.GetBytes(hash + "\n" + events + "\n"));
            samples.Add(new { fixture, frame = world.Frame, elapsed_ms = ms, first = tick == 0 });
        }
        foreach (var sample in samples) output("connection_tick=" + JsonSerializer.Serialize(sample));
        output("connection_result=" + JsonSerializer.Serialize(new { fixture, frames = samples.Count, peak_ms = peak,
            over_budget = over, hash = world.Hash(), trace_sha256 = Convert.ToHexString(digest.GetHashAndReset()) }));
        return over == 0;
    }

    private static string Read(string name)
    {
        using var stream = typeof(RouteConnectionChecks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing connection fixture: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
