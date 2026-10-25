using Rts.Movement.Prototype;
using System.Text.Json;

try
{
    if (args.Contains("--yield-boundaries-only")) { RouteYieldBoundaries.Run(); Console.WriteLine("yield_boundaries_only=PASS"); }
    else if (args.Contains("--lifecycle-checks")) RouteLifecycleChecks.Run();
    else if (args.Contains("--closeout-failure-audit")) RouteCloseoutFailureAudit.Run(Console.WriteLine);
    else if (args.Contains("--closeout-gates")) Environment.ExitCode = RouteCloseoutChecks.Run(Console.WriteLine) ? 0 : 1;
    else if (args.Contains("--navigation-checks")) RouteNavigationChecks.Run();
    else if (args.Contains("--yield-checks"))
    {
        var untouched = RouteYieldChecks.Load("yield-single-329.json");
        var before = untouched.Save();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        RoutePreparation.Run();
        RouteYieldChecks.Warmup();
        timer.Stop();
        if (untouched.Save() != before) throw new Exception("Yield preparation changed authority.");
        Console.WriteLine($"yield_prepare_ms={timer.Elapsed.TotalMilliseconds:R} authority_unchanged=true");
        RouteYieldChecks.Run();
    }
    else if (args.Contains("--placement-checks")) RoutePlacementChecks.Run();
    else if (args.Contains("--connection-checks")) Environment.ExitCode = RouteConnectionChecks.Run(Console.WriteLine) ? 0 : 1;
    else if (args.Contains("--follow-range-checks")) RouteFollowRangeChecks.Run();
    else if (args.Contains("--reference-checks")) RouteReferenceChecks.Run();
    else if (args.Contains("--inward-checks")) RouteInwardChecks.Run();
    else if (args.Contains("--diagnose"))
    {
        var world = RouteScenarios.Create(int.Parse(args[^1]));
        for (var tick = 0; tick < 1200; tick++)
        {
            world.Step();
            if (world.Events.Any(item => item.Kind is "retry" or "failed") || tick % 120 == 0)
                Console.WriteLine(JsonSerializer.Serialize(new { world.Frame, world.Events, units = world.Units,
                    world.Paths, targets = world.Units.Select(body => new { body.Id, target = world.FollowTarget(body.Id) }) }));
        }
    }
    else Environment.ExitCode = RouteCheckSuite.Run(Console.WriteLine) ? 0 : 1;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
