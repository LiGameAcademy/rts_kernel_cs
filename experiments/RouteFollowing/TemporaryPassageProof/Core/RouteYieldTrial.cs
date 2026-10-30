namespace Rts.Movement.Prototype;

/// <summary>One search's derived occupancy; projected positions are always restored after a trial.</summary>
internal sealed class RouteYieldTrial
{
    private readonly CrowdReservations _reservations = new(64);

    internal RouteYieldTrial(IEnumerable<Walker> bodies)
    {
        foreach (var body in bodies)
            _reservations.Put(CrowdBody.Stationary(new(body.Id), RouteSettings.Radius, body.Position));
    }

    internal bool CanAdvance(RouteWorld world, Walker source, ulong[] members, FixedVector2[] targets)
    {
        var original = members.Select(id => _reservations.Read(new(id)))
            .Append(_reservations.Read(new(source.Id))).ToArray();
        try
        {
            for (var i = 0; i < members.Length; i++)
                _reservations.Put(CrowdBody.Stationary(new(members[i]), RouteSettings.Radius, targets[i]));
            return RouteYieldClearance.Simulate(world, source, _reservations);
        }
        finally
        {
            // Put retains conservative old bucket memberships. Every query reads
            // the current body and exact geometry, so stale buckets only overselect.
            // The whole index dies with this bounded synchronous search.
            foreach (var body in original) _reservations.Put(body);
        }
    }
}
