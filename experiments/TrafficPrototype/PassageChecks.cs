using System.Text.Json;
using Rts.Kernel.Navigation;

namespace Rts.TrafficPrototype;

internal static class PassageChecks
{
    internal static IReadOnlyList<object> Run(Action<bool, string> check)
    {
        var cells = Enumerable.Range(0, 5).Select(x => new GridCell(x, 1)).Append(new(2, 0)).ToArray();
        var input = new PassageInput(cells, new(new(0, 1), new(2, 1)), new(new(4, 1), new(2, 1)));
        var baseline = Finish(new PassageReference(input), 1, check);
        var plan = baseline.ReadPlan();
        check(baseline.Stage == PassageStage.Found && plan.Count > 1, "shared plan reaches both original goals");
        check(plan.Any(state => state.B == new GridCell(2, 0)), "temporarily settled member voluntarily uses a passing pocket");
        check(plan[^1] == input.Goal, "yielding member returns to its own final slot");
        for (var step = 1; step < plan.Count; step++)
            check(PassageReference.SafeTransition(plan[step - 1], plan[step]), "entire shared transition is continuously safe");
        var canonical = JsonSerializer.Serialize(plan);
        foreach (var budget in new[] { 7, 25, 31, 1000 })
        {
            var split = Finish(new PassageReference(input), budget, check);
            check(split.SpentWork == baseline.SpentWork && JsonSerializer.Serialize(split.ReadPlan()) == canonical,
                "work chunk size changes physical scheduling only, not the chosen path");
        }
        var partial = new PassageReference(input);
        while (!partial.Complete)
        {
            partial.Advance(11);
            var saved = partial.CaptureJson();
            var restored = PassageReference.Restore(saved);
            check(saved == restored.CaptureJson(), "in-progress passage cursor restores exactly");
            check(JsonSerializer.Serialize(Finish(restored, 13, check).ReadPlan()) == canonical, "restored shared search completes identically");
        }
        var forged = new PassageReference(input).CaptureJson().Replace("\"SpentWork\":0", "\"SpentWork\":9999");
        try { PassageReference.Restore(forged); check(false, "impossible replay cursor is rejected"); }
        catch (InvalidDataException) { check(true, "impossible replay cursor is rejected"); }
        var stopped = Finish(new PassageReference(input with { StopB = true }), 17, check);
        check(stopped.Stage == PassageStage.Unreachable && stopped.ReadPlan().Count == 0,
            "Stop blocker stays fixed; narrow-map oracle exhausts all feasible joint states");
        var playback = new PassagePlayback(input);
        check(playback.Install(baseline), "current shared plan installs");
        while (playback.State.B != new GridCell(2, 0)) check(playback.Step(), "fixture reaches voluntary yield");
        var stoppedAt = playback.State;
        playback.StopB();
        check(!playback.Step() && playback.State == stoppedAt, "Stop cancels old return movement immediately");
        check(!playback.Install(baseline), "old order generation cannot reinstall after Stop");
        var revised = Finish(playback.PlanAgain(), 19, check);
        check(playback.Install(revised), "new plan respects Stop at its actual pocket position");
        while (playback.Step()) check(playback.State.B == stoppedAt.B, "Stop body remains fixed through all replanned steps");
        check(playback.State.A == input.Goal.A, "remaining member can finish around the stopped pocket");
        var alien = Finish(new PassageReference(input with { Goal = input.Goal with { B = new GridCell(3, 1) } }), 23, check);
        check(!new PassagePlayback(input).Install(alien), "same revision cannot install a different target plan");
        var alienMap = Finish(new PassageReference(input with { Cells = cells.Append(new(1, 0)).ToArray() }), 29, check);
        check(!new PassagePlayback(input).Install(alienMap), "same revision cannot install a different map plan");
        var invalidReplacement = new PassagePlayback(input);
        invalidReplacement.Install(baseline);
        try { invalidReplacement.ReplaceGoalB(new(16, 16)); check(false, "invalid replacement fails"); }
        catch (ArgumentException) { check(true, "invalid replacement fails"); }
        check(invalidReplacement.Revision == 0 && invalidReplacement.Step(), "failed replacement preserves current plan and generation");
        var ownedCells = cells.ToArray();
        var owned = new PassageReference(input with { Cells = ownedCells });
        var beforeMutation = owned.CaptureJson();
        ownedCells[0] = new GridCell(16, 16);
        check(owned.CaptureJson() == beforeMutation, "search owns a frozen map copy");
        var successor = new PassagePlayback(input);
        successor.Install(baseline);
        successor.ReplaceGoalB(new(3, 1));
        check(!successor.Step() && !successor.Install(baseline), "successor command invalidates the previous shared plan");
        return Array.AsReadOnly<object>([new { scenario = "two_unit_passing_pocket_reference", steps = plan,
            plannedWork = baseline.SpentWork, baseline.Expanded, stoppedStatus = stopped.Stage.ToString(), stoppedWork = stopped.SpentWork,
            stoppedAfterYield = playback.State, productionAlgorithmSelected = false }]);
    }

    private static PassageReference Finish(PassageReference job, int budget, Action<bool, string> check)
    {
        while (!job.Complete)
        {
            var used = job.Advance(budget);
            check(used > 0 && used <= budget, "search respects actual work quantum");
            if (job.SpentWork > 10000) throw new InvalidOperationException("Two-body oracle exceeded its stated finite domain.");
        }
        return job;
    }
}
