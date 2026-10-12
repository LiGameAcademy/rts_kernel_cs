using Rts.Kernel.Navigation;

namespace Rts.Kernel;

/// <summary>Owns event publication order and undrained output for one match.</summary>
internal sealed class MatchEventBuffer
{
    private readonly List<MatchEvent> _pending = [];

    internal long NextSequence { get; private set; }

    internal void Publish(long frame, MatchEventKind kind, EntityId entityId, string detail,
        GroupMoveOutcome? group = null)
    {
        _pending.Add(new MatchEvent(frame, NextSequence++, kind, entityId, detail, group));
    }

    internal IReadOnlyList<MatchEvent> Drain()
    {
        var drained = _pending.ToArray();
        _pending.Clear();
        return drained;
    }

    /// <summary>Restore the validated counter. v9 does not store or replay undrained output.</summary>
    internal void Restore(long nextSequence)
    {
        _pending.Clear();
        NextSequence = nextSequence;
    }
}
