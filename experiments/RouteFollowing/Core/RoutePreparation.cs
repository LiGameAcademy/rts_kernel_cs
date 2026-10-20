namespace Rts.Movement.Prototype;

/// <summary>Explicit loading work on disposable worlds; never reads or mutates the real world.</summary>
public static class RoutePreparation
{
    public static void Run()
    {
        foreach (var scenario in new[] { 1, 2, 3, 4, 5 })
        {
            var disposable = RouteScenarios.Create(scenario);
            for (var tick = 0; tick < 400; tick++)
            {
                disposable.Step();
                if (tick % 90 == 0) _ = RouteWorld.Load(disposable.Save());
            }
        }
    }
}
