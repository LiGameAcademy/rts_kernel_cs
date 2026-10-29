using Rts.Movement.Prototype;

/// <summary>A displaced member cannot retain a terrain-blocked shared entry merely because map/goal versions match.</summary>
internal static class ReferenceLegChecks
{
    internal static void Run()
    {
        using var stream = typeof(ReferenceLegChecks).Assembly.GetManifestResourceStream("multi-before-127-20.json")!;
        using var reader = new StreamReader(stream);
        var world = RouteWorld.Load(reader.ReadToEnd());
        var before = world.Save();
        var source = world.Bodies[20];
        var oldTarget = source.Navigation!.Points[source.Cursor];
        Check(source.Navigation.Kind == MemberReferenceKind.SharedSuffix
            && RoutePlanning.TryDirect(world, source.Position, oldTarget, []) is null, "Fixture contains a blocked shared entry");
        var (repaired, target, _) = RouteNavigation.Follow(world, source);
        Check(repaired.Navigation is { Kind: MemberReferenceKind.Independent }
            && RoutePlanning.TryDirect(world, repaired.Position, target, []) is not null, "Repair selects an executable terrain leg");
        Check(repaired.Endpoint == source.Endpoint && repaired.Preference == source.Preference
            && repaired.WaitTicks == source.WaitTicks && repaired.Recoveries == source.Recoveries,
            "Repair preserves goal ownership and recovery history");
        Check(world.Save() == before, "Reference query does not mutate authority");
        RouteBoundaryChecks.Run();
        Console.WriteLine("reference_leg=PASS command_stop_queue_map_restore_boundaries=PASS");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
