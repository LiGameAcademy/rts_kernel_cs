using System.Text.Json;
using Rts.Movement.Prototype;

/// <summary>Admission and restoration share the same finite capacity without changing per-tick application.</summary>
internal static class CommandCapacityChecks
{
    internal static void Run()
    {
        var world = MultiGroupChecks.Create(500);
        Check(MultiGroupChecks.Submit(world, 500).Length == 0, "All21 groups admitted together");
        var state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        Check(state.Commands.Length == 21, "All21 groups retained for processing");
        while (state.Commands.Length < RouteSettings.CommandCapacity)
        {
            Check(world.Stop([500]), "Stop admitted within shared limit");
            state = JsonSerializer.Deserialize<RouteSnapshot>(world.Save())!;
        }
        var full = world.Save();
        Check(!world.Move([1], new(928, 256)) && world.Save() == full, "Overflow move preserves old commands");
        Check(!world.Stop([1]) && world.Save() == full, "Overflow Stop preserves old commands");
        var restored = RouteWorld.Load(full);
        var accepted = new HashSet<ulong>();
        for (var tick = 0; tick < RouteSettings.CommandCapacity; tick++)
        {
            world.Step();
            restored.Step();
            Check(world.Hash() == restored.Hash()
                && JsonSerializer.Serialize(world.Events) == JsonSerializer.Serialize(restored.Events), "Queued restore continuation");
            var assigned = world.Events.Where(e => e.Kind == "accepted").ToArray();
            Check(assigned.Length == (tick < 20 ? 24 : tick == 20 ? 20 : 0), "Exactly one group applied per tick");
            foreach (var item in assigned) Check(accepted.Add(item.Id), "Each initial member accepted once");
        }
        Check(accepted.Count == 500 && world.Bodies[500].Phase == WalkPhase.Stopped, "All groups execute before following Stop");
        var malformed = state with { Commands = state.Commands.Append(state.Commands[0]).ToArray() };
        var rejected = false;
        try { _ = RouteWorld.Load(JsonSerializer.Serialize(malformed)); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Oversized snapshot queue rejected");
        Console.WriteLine("command_capacity=PASS groups=21 members=500 capacity=32 replay_ticks=32");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
