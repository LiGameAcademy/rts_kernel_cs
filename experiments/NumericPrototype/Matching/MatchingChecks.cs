using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rts.Kernel;

namespace NumericPrototype.Matching;

internal static class MatchingChecks
{
    internal static string Run(Action<bool, string> check)
    {
        var trace = new StringBuilder();
        var rng = new DeterministicRng(68);
        var searching = 0;
        var negativePotentials = 0;
        var scaleSquared = (Int128)PrototypeScalar.Scale * PrototypeScalar.Scale;
        for (var size = 1; size <= 7; size++)
            for (var sample = 0; sample < 12; sample++)
            {
                // Includes ties, a constant maximal matrix, and exact fractional raw costs.
                Int128[] costs = Enumerable.Range(0, size * size).Select(_ => sample switch
                {
                    0 => (Int128)0,
                    1 => WideMatchingWork.MaximumCost,
                    _ => (Int128)(rng.NextUInt64() % 100) * scaleSquared + sample
                }).ToArray();
                var work = new WideMatchingWork(size, costs);
                var initial = work.Capture();
                ValidateDual(initial, size, costs, check);
                var restoredInitial = WideMatchingWork.Restore(size, costs, RoundTrip(initial));
                check(JsonSerializer.Serialize(restoredInitial.Capture()) == JsonSerializer.Serialize(initial),
                    "initial snapshot restores");
                while (!work.Complete)
                {
                    var restored = WideMatchingWork.Restore(size, costs, RoundTrip(work.Capture()));
                    var budget = sample % 3 + 1;
                    var inspected = work.Advance(budget);
                    check(restored.Advance(budget) == inspected, "restored batch budget matches");
                    var state = work.Capture();
                    check(JsonSerializer.Serialize(restored.Capture()) == JsonSerializer.Serialize(state),
                        "every raw potential/cursor/assignment matches after resume");
                    check(inspected == 0 || inspected >= budget || work.Complete,
                        "budget stops only at a complete column batch");
                    ValidateDual(state, size, costs, check);
                    if (state.Searching)
                    {
                        searching++;
                        var broken = RoundTrip(state);
                        broken.Previous[state.Column] = size + 1;
                        Reject(broken, size, costs, check);
                        Reject(state with { Batches = state.Batches + 1 }, size, costs, check);
                    }
                    if (state.V.Any(value => Int128.Parse(value) < 0)) negativePotentials++;
                    trace.Append(JsonSerializer.Serialize(state)).Append(';');
                }
                var result = work.Result();
                check(result.Distinct().Count() == size, "result is a permutation");
                var total = Enumerable.Range(0, size).Aggregate(BigInteger.Zero,
                    (sum, row) => sum + (BigInteger)costs[row * size + result[row]]);
                check(total == ExhaustiveMinimum(size, costs), "Hungarian cost equals independent exhaustive optimum");
                var unbounded = new WideMatchingWork(size, costs);
                while (!unbounded.Complete) unbounded.Advance(int.MaxValue - size);
                check(unbounded.Result().SequenceEqual(result), "budget does not change stable tie result");
                var final = RoundTrip(work.Capture());
                check(WideMatchingWork.Restore(size, costs, final).Result().SequenceEqual(result),
                    "completed snapshot restores");
                Reject(final with { Row = 0 }, size, costs, check);
                var corrupted = RoundTrip(final);
                corrupted.U[1] = (Int128.Parse(corrupted.U[1]) + 1).ToString();
                Reject(corrupted, size, costs, check);
                Reject(final with { Identity = "other-input" }, size, costs, check);
                // Captured arrays belong to the caller; edits cannot corrupt the live work.
                final.Occupant[1] = 0;
                check(work.Result().SequenceEqual(result), "snapshot owns detached mutable arrays");
            }
        var maximum = new WideMatchingWork(500,
            Enumerable.Repeat(WideMatchingWork.MaximumCost, 500 * 500).ToArray());
        while (!maximum.Complete) maximum.Advance(1);
        check(maximum.Result().SequenceEqual(Enumerable.Range(0, 500)),
            "500 maximal equal costs retain deterministic row/column assignment");
        check(searching > 0 && negativePotentials > 0, "fixtures exercise searching and negative signed potentials");
        check((BigInteger)(2 * WideMatchingWork.MaximumMembers + 3) *
            (BigInteger)WideMatchingWork.MaximumCost < (BigInteger)Int128.MaxValue,
            "conservative intermediate envelope fits signed Int128");
        try
        {
            _ = new WideMatchingWork(1, [WideMatchingWork.MaximumCost + 1]);
            check(false, "cost outside experimental bound rejected");
        }
        catch (ArgumentOutOfRangeException) { check(true, "cost bound fails explicitly"); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString()))).ToLowerInvariant();
    }

    private static void ValidateDual(MatchingSnapshot state, int size, Int128[] costs, Action<bool, string> check)
    {
        var u = state.U.Select(Int128.Parse).ToArray();
        var v = state.V.Select(Int128.Parse).ToArray();
        var bound = (size + 1) * WideMatchingWork.MaximumCost;
        check(u.All(value => value >= 0 && value <= bound)
            && v.All(value => value >= -bound && value <= bound), "potential envelope");
        for (var row = 1; row <= size; row++)
            for (var column = 1; column <= size; column++)
                check(costs[(row - 1) * size + column - 1] - u[row] - v[column] >= 0,
                    "dual remains feasible at every suspension");
        for (var column = 1; column <= size; column++)
            if (state.Occupant[column] != 0)
                check(costs[(state.Occupant[column] - 1) * size + column - 1]
                    == u[state.Occupant[column]] + v[column], "assigned edge remains tight");
    }

    private static BigInteger ExhaustiveMinimum(int size, Int128[] costs)
    {
        BigInteger? minimum = null;
        void Visit(int row, int mask, BigInteger sum)
        {
            if (row == size)
            {
                if (minimum is null || sum < minimum) minimum = sum;
                return;
            }
            for (var column = 0; column < size; column++)
                if ((mask & (1 << column)) == 0)
                    Visit(row + 1, mask | (1 << column), sum + (BigInteger)costs[row * size + column]);
        }
        Visit(0, 0, BigInteger.Zero);
        return minimum!.Value;
    }

    private static MatchingSnapshot RoundTrip(MatchingSnapshot state) =>
        JsonSerializer.Deserialize<MatchingSnapshot>(JsonSerializer.Serialize(state))!;

    private static void Reject(MatchingSnapshot state, int size, Int128[] costs, Action<bool, string> check)
    {
        try
        {
            WideMatchingWork.Restore(size, costs, state);
            check(false, "corrupted snapshot must fail");
        }
        catch (InvalidDataException) { check(true, "corrupted snapshot rejected before publication"); }
    }
}
