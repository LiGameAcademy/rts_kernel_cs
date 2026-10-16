namespace Rts.Kernel.Navigation;

/// <summary>Frame-local cancellation only. A removed move stays stationary until all dependencies are safe.</summary>
internal static class CrowdMotionResolver
{
    internal static HashSet<EntityId> Resolve(CrowdReservations reservations, IReadOnlyList<EntityId> priority)
    {
        var rank = priority.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
        var held = new HashSet<EntityId>();
        bool changed;
        do
        {
            changed = false;
            foreach (var id in priority)
            {
                if (held.Contains(id)) continue;
                var body = reservations.Read(id);
                var otherId = reservations.FirstMotionConflict(body);
                if (otherId is null) continue;
                var other = reservations.Read(otherId.Value);
                var loser = id;
                if (other.HasMotion && rank.TryGetValue(other.Id, out var otherRank)
                    && (!body.HasMotion || otherRank > rank[id])) loser = other.Id;
                if (!reservations.Read(loser).HasMotion)
                    throw new InvalidOperationException("Stationary frame bodies overlap.");
                held.Add(loser);
                var rejected = reservations.Read(loser);
                reservations.Put(CrowdBody.Stationary(loser, rejected.Radius, rejected.Trace[0]));
                changed = true;
            }
            // Each change permanently removes one proposal; a final unchanged pass proves consistency.
        } while (changed);
        return held;
    }
}
