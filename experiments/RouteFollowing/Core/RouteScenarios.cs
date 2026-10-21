namespace Rts.Movement.Prototype;

public static class RouteScenarios
{
    public static RouteWorld Create(int scenario)
    {
        var world = new RouteWorld();
        switch (scenario)
        {
            case 1:
                world.Spawn(new(500, 640)); world.Spawn(new(564, 640));
                world.Move([1], new(1400, 640)); world.Move([2], new(1464, 640));
                break;
            case 2:
                world.Spawn(new(500, 640)); world.Spawn(new(1500, 640));
                world.Move([1], new(1500, 640)); world.Move([2], new(500, 640));
                break;
            case 3:
                world.Spawn(new(500, 640)); world.Spawn(new(900, 640), stopped: true);
                world.Move([1], new(1500, 640));
                break;
            default:
                if (scenario == 4)
                {
                    world.SetWall(new(30, 0, 2, 16), true);
                    world.SetWall(new(30, 24, 2, 16), true);
                }
                var points = FormationLayout.Create(FormationKind.Compact, 24, 80, new(450, 640), 0);
                foreach (var point in points) world.Spawn(point);
                world.Move(world.Units.Select(body => body.Id), new(1550, 640));
                break;
        }
        return world;
    }
}
