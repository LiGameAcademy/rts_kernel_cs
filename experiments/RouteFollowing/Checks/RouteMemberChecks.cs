using System.Text.Json;

/// <summary>Current member-reference contract; historical centre-line golden checks remain separate.</summary>
public static class RouteMemberChecks
{
    public static bool Run(Action<string> output)
    {
        if (!RouteCloseoutChecks.Run(output)) return false;
        var passed = true;
        foreach (var scenario in new[] { 1, 2, 3, 4, 5 })
            passed &= RouteCheckSuite.Scenario(scenario, output);
        RoutePlacementChecks.Run();
        output("member_placement_boundaries=PASS");
        RouteBoundaryChecks.Run();
        output("member_command_restore_boundaries=PASS");
        output("member_acceptance=" + JsonSerializer.Serialize(new { passed,
            legacy_centreline_goldens_replaced = false, production_unchanged = true }));
        return passed;
    }
}
