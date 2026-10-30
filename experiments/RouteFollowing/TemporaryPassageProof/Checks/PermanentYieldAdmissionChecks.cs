using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>The old275-only plan must not consume a round while270 still blocks the requester.</summary>
internal static class PermanentYieldAdmissionChecks
{
    internal static void Run()
    {
        using var stream = typeof(PermanentYieldAdmissionChecks).Assembly.GetManifestResourceStream("multi-before-288-265.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        var before = world.Save();
        var requester = world.Bodies[265] with { Blocker = 275, Recovery = null };
        var accepted = RoutePlacements.TryBeginYield(world, requester, new RouteYieldObservation(world),
            [275], [FixedVector2.FromRaw(14224931684352, 8177617731584)]);
        if (accepted || world.Save() != before)
            throw new InvalidOperationException("Invalid partial clearance changed goals or consumed recovery");
        Console.WriteLine("permanent_yield_admission=PASS rejected_original_275_only_plan authority_unchanged=true");
    }
}
