using Rts.Movement.Prototype;
using System.Text.Json;

try
{
    if (args.Contains("--reference-checks")) RouteReferenceChecks.Run();
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
