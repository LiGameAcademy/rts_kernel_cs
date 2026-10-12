namespace Rts.Kernel;

/// <summary>Owns accepted commands and their deterministic arrival order for one match.</summary>
internal sealed class MatchCommandQueue
{
    private readonly List<QueuedCommand> _pending = [];

    internal long NextArrivalOrder { get; private set; }

    internal CommandAcceptance Submit(CommandEnvelope command, long currentFrame)
    {
        if (command.ExecuteFrame <= currentFrame)
            return CommandAcceptance.Reject("execute_frame_must_be_in_the_future");

        if (command.GetStructureError() is { } error)
            return CommandAcceptance.Reject(error);

        _pending.Add(new QueuedCommand(NextArrivalOrder++, command.Freeze()));
        return CommandAcceptance.Accept();
    }

    internal IReadOnlyList<CommandEnvelope> TakeForFrame(long frame)
    {
        var due = _pending
            .Where(item => item.Command.ExecuteFrame == frame)
            .OrderBy(item => item.Command.PlayerId)
            .ThenBy(item => item.Command.Sequence)
            .ThenBy(item => item.ArrivalOrder)
            .Select(item => item.Command)
            .ToArray();

        // Remove before execution: this batch cannot run twice, and future commands stay queued.
        _pending.RemoveAll(item => item.Command.ExecuteFrame <= frame);
        return due;
    }

    internal IReadOnlyList<QueuedCommandSnapshot> Capture() => _pending
        .OrderBy(item => item.Command.ExecuteFrame)
        .ThenBy(item => item.Command.PlayerId)
        .ThenBy(item => item.Command.Sequence)
        .ThenBy(item => item.ArrivalOrder)
        .Select(item => new QueuedCommandSnapshot(item.ArrivalOrder, item.Command))
        .ToArray();

    /// <summary>Restore counters and frozen commands after the complete snapshot has been validated.</summary>
    internal void Restore(long nextArrivalOrder, IReadOnlyList<QueuedCommandSnapshot> pending)
    {
        _pending.Clear();
        _pending.AddRange(pending.Select(item => new QueuedCommand(item.ArrivalOrder, item.Command.Freeze())));
        NextArrivalOrder = nextArrivalOrder;
    }

    private sealed record QueuedCommand(long ArrivalOrder, CommandEnvelope Command);
}
