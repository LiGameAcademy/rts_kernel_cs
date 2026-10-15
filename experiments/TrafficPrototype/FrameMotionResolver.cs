using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

/// <summary>
/// Counterfactual frame resolver: all candidates use one initial world.
/// Cancellation only removes movement; recheck dependencies until every remaining pair is safe.
/// This deliberately simple reference does not generate detours or guarantee progress.
/// </summary>
internal static class FrameMotionResolver
{
    internal const int MaximumMembers = 64;

    internal static MotionResolution Resolve(IReadOnlyList<MotionProposal> input)
    {
        if (input.Count is < 1 or > MaximumMembers) throw new ArgumentOutOfRangeException(nameof(input));
        var proposals = input.OrderBy(item => item.Id).ToArray();
        foreach (var item in proposals) item.Validate();
        if (proposals.Select(item => item.Id).Distinct().Count() != proposals.Length)
            throw new ArgumentException("Duplicate proposal ID.");
        var ends = proposals.Select(item => item.DesiredEnd).ToArray();
        var pairChecks = 0;
        for (var a = 0; a < proposals.Length; a++)
        for (var b = a + 1; b < proposals.Length; b++)
        {
            pairChecks++;
            if (LinearTimeCollision.Overlaps(proposals[a].Start, proposals[a].Start, proposals[a].Radius,
                proposals[b].Start, proposals[b].Start, proposals[b].Radius))
                throw new ArgumentException("Initial bodies overlap.");
        }
        var passes = 0;
        while (true)
        {
            passes++;
            var changed = false;
            for (var a = 0; a < proposals.Length; a++)
            for (var b = a + 1; b < proposals.Length; b++)
            {
                pairChecks++;
                if (!LinearTimeCollision.Overlaps(proposals[a].Start, ends[a], proposals[a].Radius,
                    proposals[b].Start, ends[b], proposals[b].Radius)) continue;
                var loser = ChooseHold(proposals, ends, a, b);
                ends[loser] = proposals[loser].Start;
                changed = true;
            }
            if (!changed) break;
            // Every changing pass removes at least one nonzero move; the final pass proves safety.
            if (passes > proposals.Length) throw new InvalidOperationException("Cancellation did not converge.");
        }
        var result = proposals.Select((item, index) => new ResolvedMotion(item.Id, item.Start, ends[index],
            item.Radius, ends[index] != item.DesiredEnd)).ToArray();
        return new MotionResolution(Array.AsReadOnly(result), pairChecks, passes);
    }

    private static int ChooseHold(MotionProposal[] items, FixedVector2[] ends, int a, int b)
    {
        var aMoves = ends[a] != items[a].Start;
        var bMoves = ends[b] != items[b].Start;
        if (!aMoves && !bMoves) throw new InvalidOperationException("Validated static bodies cannot conflict.");
        if (!aMoves) return b;
        if (!bMoves) return a;
        if (items[a].WaitAge != items[b].WaitAge) return items[a].WaitAge > items[b].WaitAge ? b : a;
        return items[a].Id < items[b].Id ? b : a;
    }
}
