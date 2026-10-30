namespace Rts.Movement.Prototype;

/// <summary>One bounded blocker search per tick; a round may span several real contacts.</summary>
internal static class RouteRecovery
{
    internal static void Advance(RouteWorld world)
    {
        var waiting = world.Bodies.Values.Where(body => body.Task != 0
            && body.Phase is WalkPhase.Waiting or WalkPhase.Following
            && world.Routes[body.Task].Yield is null && body.WaitTicks >= body.RetryAtWait)
            .OrderBy(body => body.Recovery?.StartedAt ?? world.Frame).ThenBy(body => body.Id).FirstOrDefault();
        if (waiting is null) return;
        var attempts = waiting.Recoveries + 1;
        if (attempts > RouteSettings.RecoveryLimit)
        {
            world.Bodies[waiting.Id] = waiting with
            {
                Task = 0, Phase = WalkPhase.Failed, Velocity = default, Blocker = null, Recovery = null, BlockingContacts = [],
                Navigation = null, Passage = null, Connection = null, TransitQuarters = 4,
                Progress = 0, ProgressCursor = 0, ProgressVersion = 0, PlacementCheckedRevision = -1
            };
            world.Publish(waiting.Id, "failed", "no_member_progress");
            return;
        }
        var contacts = Contacts(world, waiting);
        var probe = waiting.Recovery;
        if (probe is null || probe.Goal != waiting.Endpoint || probe.MapRevision != world.Revision)
            probe = new(waiting.Endpoint, world.Revision, world.Frame, contacts);
        var work = probe.PairWork;
        if (probe.Next < probe.Candidates.Count && work < 2048)
        {
            var id = probe.Candidates[probe.Next];
            // Recheck the actual next move against current waiting/settled bodies.
            // A departed peer, Stop, new task or queued helper cannot be recruited.
            if (contacts.Contains(id) && RouteYielding.TryStart(world,
                waiting with { Blocker = id, Recovery = null }, ref work)) return;
            probe = probe with { Next = probe.Next + 1, PairWork = Math.Min(work, 2048) };
            world.Publish(waiting.Id, "recovery_probe", id.ToString());
            if (probe.Next < probe.Candidates.Count && work < 2048)
            {
                world.Bodies[waiting.Id] = waiting with { Recovery = probe };
                return;
            }
        }
        var prepared = RouteNavigation.Prepare(world, waiting, rebuild: true);
        var unchanged = SameReference(waiting.Navigation, prepared.Navigation);
        if (unchanged) prepared = waiting; // No new geometry: retain cursor, version and signed progress.
        world.Bodies[waiting.Id] = prepared with
            { Recovery = null, Recoveries = attempts, RetryAtWait = waiting.WaitTicks + RouteSettings.WaitBeforeRetry };
        world.Publish(waiting.Id, "retry", prepared.Navigation is null ? "member_route_unavailable"
            : unchanged ? "no_recovery_candidate" : "member_reference_replaced");
    }

    internal static ulong[] Contacts(RouteWorld world, Walker source)
    {
        var (body, target, _) = RouteNavigation.Follow(world, source);
        var step = RouteMotionCandidates.Compute(world, body, target, RouteSettings.Speed);
        var proposed = new CrowdBody(new(body.Id), 31, step.Trace, step.Times);
        // New worlds retain contacts from the last rejected proposal against final timed reservations.
        // Historical fixtures have no such field; their first query is explicitly reconstructed.
        var found = world.Bodies.Values.Where(other => other.Id != body.Id
            && (source.BlockingContacts is null || source.BlockingContacts.Contains(other.Id))
            && RouteYielding.CanParticipate(other, body.Task)
            && TimedCrowdGeometry.Overlap(proposed, CrowdBody.Stationary(new(other.Id), 31, other.Position)))
            .Select(other => other.Id);
        return found.OrderBy(id => id == body.Blocker ? 0 : 1).ThenBy(id => id).ToArray();
    }

    internal static bool SameReference(MemberReference? prior, MemberReference? next) =>
        prior is null ? next is null : next is not null && prior.Goal == next.Goal
            && prior.MapRevision == next.MapRevision && prior.Kind == next.Kind
            && prior.Points.SequenceEqual(next.Points) && prior.Corners.SequenceEqual(next.Corners);
}
