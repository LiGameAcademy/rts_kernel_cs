using Rts.Kernel.Navigation;

namespace Rts.TrafficPrototype;

/// <summary>Two-body fixture executor. Order changes invalidate the whole old shared plan before movement.</summary>
internal sealed class PassagePlayback(PassageInput input)
{
    private PassageInput _input = PassageReference.Freeze(input);
    private IReadOnlyList<PassageState> _plan = Array.Empty<PassageState>();
    private int _next;
    internal PassageState State { get; private set; } = input.Start;
    internal long Revision => _input.OrderRevision;
    internal PassageReference PlanAgain() => new(_input with { Start = State });

    internal bool Install(PassageReference plan)
    {
        var steps = plan.ReadPlan();
        if (!plan.Matches(_input with { Start = State }) || steps.Count == 0 || steps[0] != State) return false;
        _plan = steps;
        _next = 1;
        return true;
    }

    internal bool Step()
    {
        if (_next >= _plan.Count) return false;
        var next = _plan[_next];
        if ((_input.StopA && next.A != State.A) || (_input.StopB && next.B != State.B)
            || !PassageReference.SafeTransition(State, next))
            throw new InvalidOperationException("Stale or unsafe shared passage plan.");
        State = next;
        _next++;
        return true;
    }

    internal void StopB()
    {
        ReplaceIntent(_input with { StopB = true, Start = State, Goal = _input.Goal with { B = State.B } });
    }

    internal void ReplaceGoalB(GridCell goal)
    {
        ReplaceIntent(_input with { StopB = false, Start = State, Goal = _input.Goal with { B = goal } });
    }

    private void ReplaceIntent(PassageInput next)
    {
        var revised = next with { OrderRevision = checked(Revision + 1) };
        _input = PassageReference.Freeze(revised);
        _plan = Array.Empty<PassageState>();
        _next = 0;
    }
}
