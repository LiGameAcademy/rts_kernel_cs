using System.Diagnostics.CodeAnalysis;

namespace Rts.Movement.Prototype;

/// <summary>Only contracts the current reference offset; never creates a route or moves another body.</summary>
internal static class RouteContraction
{
    internal static FixedVector2 Offset(Walker body) =>
        body.Offset * SimScalar.FromRatio(body.TransitQuarters, 4);

    internal static bool TryStep(RouteWorld world, Walker body, int startingQuarter, EntityId? blocker,
        CrowdReservations reservations, out Walker inwardBody, [NotNullWhen(true)] out MovementStep? inwardStep)
    {
        inwardBody = body;
        inwardStep = null;
        if (body.Connection is not null || startingQuarter == 0 || blocker is null
            || world.Bodies[blocker.Value.Value].Task != body.Task) return false;
        var route = world.Routes[body.Task];
        if (body.Cursor >= route.Points.Count - 1) return false;
        var reference = route.Points[body.Cursor];
        for (var quarter = startingQuarter - 1; quarter >= 0; quarter--)
        {
            var target = reference + body.Offset * SimScalar.FromRatio(quarter, 4);
            if ((target - body.Position).Length > 192) continue;
            var direct = StraightMovePath.TryFind(world.Navigation, body.Position, target, 1);
            if (direct is null) continue;
            var step = MovementStepper.Compute(new(new(body.Id), 0, body.Position, body.Velocity, body.Facing),
                new(new(target, RouteSettings.Speed, 1, RouteSettings.Motion), direct), 30, null);
            if (step.Position == body.Position) continue;
            var motion = new CrowdBody(new(body.Id), 31, step.Trace, step.Times);
            if (reservations.FirstMotionConflict(motion) is not null) continue;
            reservations.Put(motion);
            inwardBody = body with { TransitQuarters = quarter, Connection = null };
            inwardStep = step;
            return true;
        }
        return false;
    }
}
