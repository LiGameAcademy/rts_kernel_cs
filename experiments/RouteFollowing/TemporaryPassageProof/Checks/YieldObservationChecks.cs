using Rts.Kernel.Numerics;
using Rts.Movement.Prototype;

/// <summary>Search observations cannot leak candidate projections or survive map/restore boundaries.</summary>
internal static class YieldObservationChecks
{
    private static int _assertions;

    internal static void Run()
    {
        var world = new RouteWorld();
        world.Spawn(new(320, 320));
        world.Spawn(new(384, 320));
        world.Spawn(new(448, 320));
        var observation = new RouteYieldObservation(world);
        var saved = world.Save();
        Check(observation.BodyAtGoal(1, new(384, 320), 0, default), "Original blocking body is present");
        Check(!observation.BodyAtGoal(1, new(384, 320), 2, new(384, 448)),
            "Projected first helper vacates the goal; exact64 spacing stays legal");
        Check(observation.BodyAtGoal(1, new(386, 320), 2, new(384, 448)), "Less than64 remains blocked");
        Check(!observation.BodyOnLeg(1, new(320, 320), new(384, 320), 2, new(384, 448)),
            "Second leg sees the first helper's replacement");
        Check(observation.BodyOnLeg(1, new(320, 320), new(384, 320), 0, default),
            "The next candidate still sees the original body");
        var passage = new RequesterPassage(new(384, 320), new FixedVector2[] { new(320, 320), new(384, 320) }, true);
        Check(observation.Clear(passage, 1, [2], [new(384, 448)]), "Requester clearance uses projected bodies");
        Check(!observation.Clear(passage, 1, [2], [new(384, 320)]), "Candidate clearance is never cached across projections");
        Check(world.Save() == saved, "Queries do not mutate world authority");

        var member = world.Units[0];
        Check(observation.CanTravel(member, new(800, 320)), "Static leg initially exists");
        Check(observation.CanTravel(member, new(800, 320)), "Repeated static query agrees");
        Check(world.SetWall(new(22, 10, 1, 1), true), "Map changes outside the search");
        Check(!new RouteYieldObservation(world).CanTravel(member, new(800, 320)), "A new search sees the wall");
        var restored = RouteWorld.Load(world.Save());
        Check(!new RouteYieldObservation(restored).CanTravel(restored.Units[0], new(800, 320)),
            "Restored searches do not retain old cache entries");
        Check(world.SetWall(new(22, 10, 1, 1), false), "Wall removal is accepted");
        Check(new RouteYieldObservation(world).CanTravel(member, new(800, 320)), "Next search sees restored passage");
        Console.WriteLine("yield_observation_assertions=" + _assertions);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _assertions++;
    }
}
