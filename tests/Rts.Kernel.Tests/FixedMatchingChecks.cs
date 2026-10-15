using System.Numerics;
using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FixedMatchingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var rng = new DeterministicRng(8901);
        var limit = SimulationDomain.CoordinateLimit.Raw;
        FixedVector2 Point() => FixedVector2.FromRaw((long)(rng.NextUInt64() % (ulong)(2 * limit)) - limit,
            (long)(rng.NextUInt64() % (ulong)(2 * limit)) - limit);
        static BigInteger Distance(FixedVector2 a, FixedVector2 b)
        {
            var x = (BigInteger)a.X.Raw - b.X.Raw;
            var y = (BigInteger)a.Y.Raw - b.Y.Raw;
            return x * x + y * y;
        }
        for (var sample = 0; sample < 64; sample++)
        {
            var positions = Enumerable.Range(0, 6).Select(_ => Point()).ToArray();
            var slots = Enumerable.Range(0, 6).Select(_ => Point()).ToArray();
            var anchor = sample % 6;
            var actual = FormationLayout.Match(positions, slots, anchor);
            var remaining = Enumerable.Range(0, 6).Where(i => i != anchor).ToArray();
            BigInteger? best = null;
            var used = new bool[6];
            used[0] = true;
            void Visit(int row, BigInteger sum)
            {
                if (row == remaining.Length)
                {
                    if (best is null || sum < best.Value) best = sum;
                    return;
                }
                for (var slot = 1; slot < 6; slot++)
                {
                    if (used[slot]) continue;
                    used[slot] = true;
                    Visit(row + 1, sum + Distance(positions[remaining[row]], slots[slot]));
                    used[slot] = false;
                }
            }
            Visit(0, Distance(positions[anchor], slots[0]));
            var cost = Enumerable.Range(0, 6).Aggregate(BigInteger.Zero,
                (sum, row) => sum + Distance(positions[row], slots[actual[row]]));
            check(actual[anchor] == 0 && actual.Distinct().Count() == 6 && cost == best,
                "wide Hungarian result equals exhaustive independent exact minimum");
            var work = new SlotMatchingWork(positions, slots, anchor);
            while (!work.Complete)
            {
                work.Advance(1);
                var state = JsonSerializer.Deserialize<SlotMatchingSnapshot>(JsonSerializer.Serialize(work.Capture()))!;
                var restored = new SlotMatchingWork(positions, slots, anchor);
                restored.Restore(state);
                while (!restored.Complete) restored.Advance(1);
                check(restored.Result().SequenceEqual(actual), "every matching checkpoint resumes to the same exact assignment");
            }
        }
    }
}
