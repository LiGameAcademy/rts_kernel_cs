using System.Text.Json;
using NumericPrototype.Movement;

namespace NumericPrototype.Matching;

internal static class MatchingPreparationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var count in new[] { 1, 2, 7, 32 })
        foreach (var ties in new[] { false, true })
        foreach (var budget in new[] { 8, 127, 4096 })
        {
            var starts = Enumerable.Range(0, count).Select(i => ties ? default
                : FixedPoint.Parse((i * 13 - 200).ToString(), (i * 7 - 100).ToString())).ToArray();
            var goals = Enumerable.Range(0, count).Select(i => ties ? default
                : FixedPoint.Parse((200 - i * 17).ToString(), (150 - i * 11).ToString())).ToArray();
            var costs = starts.SelectMany(a => goals.Select(b => WideSquaredDistance.Between(
                new(a.X), new(a.Y), new(b.X), new(b.Y)).Raw)).ToArray();
            var reference = new WideMatchingWork(count, costs, WideMatchingWork.MaximumFormationCost);
            using var prepared = new MatchingPreparation(starts, goals, new Int128[costs.Length]);
            var seen = new HashSet<MatchingPreparationStage>();
            while (!prepared.Complete)
            {
                var before = prepared.Capture();
                var spent = prepared.Advance(budget);
                check(spent > 0 && spent <= budget, "preparation obeys exact integer budget");
                if (!seen.Add(prepared.Stage)) continue;
                using var restored = new MatchingPreparation(starts, goals, new Int128[costs.Length]);
                restored.Restore(JsonSerializer.Deserialize<MatchingPreparationSnapshot>(JsonSerializer.Serialize(before))!);
                check(restored.Advance(budget) == spent && restored.Capture() == prepared.Capture()
                    && restored.Stage == prepared.Stage, "every preparation phase resumes with identical budget/cursor");
                while (!restored.Complete) restored.Advance(4096);
                check(JsonSerializer.Serialize(restored.CreateWork().Capture()) == JsonSerializer.Serialize(reference.Capture()),
                    "restored phase produces the exact original identity, dual and stable seed");
            }
            var work = prepared.CreateWork();
            check(JsonSerializer.Serialize(work.Capture()) == JsonSerializer.Serialize(reference.Capture()),
                "scheduled preparation equals synchronous matching initial state including ties");
            while (!work.Complete)
            {
                var spent = work.Advance(127);
                check(reference.Advance(127) == spent && JsonSerializer.Serialize(work.Capture())
                    == JsonSerializer.Serialize(reference.Capture()), "matching trajectory is unchanged after scheduled preparation");
            }
            check(work.Result().SequenceEqual(reference.Result()), "scheduled preparation preserves exact assignments");
        }
        using var invalid = new MatchingPreparation([default], [default], new Int128[1]);
        try { invalid.Restore(new(1, 6)); check(false, "oversized cursor accepted"); }
        catch (InvalidDataException) { check(true, "oversized preparation cursor rejected"); }
    }
}
