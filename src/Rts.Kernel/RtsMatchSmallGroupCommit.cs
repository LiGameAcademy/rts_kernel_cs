using Rts.Kernel.Navigation;

namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    private void SubmitSmallGroupGoals(CommandEnvelope command, ulong groupId, SimScalar heading,
        IReadOnlyList<PlacementGoal> goals, MatchCommandExecutor.MoveSubmission submission)
    {
        var request = command.Group!;
        var prepared = new List<(PlacementGoal Goal, MatchCommandExecutor.MovePreparation Move)>();
        string? error = null;
        foreach (var item in goals)
        {
            if (item.Goal is null)
            {
                error = "group_slot_unavailable";
                break;
            }
            var definition = item.Member.Definition;
            var move = CommandEnvelope.MoveTo(command.ExecuteFrame, command.PlayerId, command.Sequence,
                item.Member.Id, item.Goal.Value, definition.Speed, item.Member.Clearance, definition.Motion,
                command.Mode, command.Source);
            var slot = new GroupSlot(groupId, item.Slot, request.Formation, request.Goal, heading,
                item.Adjusted, true, request.EntityIds.Count);
            error = _commandExecutor.PrepareMove(move, _navigation, slot, submission, out var ready);
            if (error is not null) break;
            prepared.Add((item, ready!));
        }
        if (error is not null)
        {
            foreach (var item in goals)
                AddGroupEvent(MatchEventKind.CommandRejected, item.Member.Id, error,
                    new(groupId, item.Slot, item.Goal, item.Adjusted));
            return;
        }
        foreach (var (goal, move) in prepared)
        {
            _commandExecutor.CommitMove(Frame, move);
            AddGroupEvent(MatchEventKind.GroupMoveAssigned, goal.Member.Id, string.Empty,
                new(groupId, goal.Slot, goal.Goal, goal.Adjusted));
        }
    }
}
