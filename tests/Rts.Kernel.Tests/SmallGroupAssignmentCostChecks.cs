using System.Security.Cryptography;
using System.Text;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class SmallGroupAssignmentCostChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new MovementDefinition(1, 30, 2);
        var trace = new StringBuilder();
        for (var count = 1; count <= 24; count++)
        foreach (var preserveAnchor in new[] { false, true })
        {
            var members = Enumerable.Range(0, count).Select(i => new PlacementMember(
                new EntityId((ulong)(count - i)), new FixedVector2(i * 37 % 53, i * 19 % 41),
                definition, 0, [])).ToArray();
            // Successive calls change the goals and include ties. No cost can survive a call.
            for (var round = 0; round < 3; round++)
            {
                var goals = Enumerable.Range(0, count).Select(i => round == 2
                    ? new FixedVector2(100, 100)
                    : new FixedVector2(100 + (i * 29 + round * 13) % 61,
                        200 + (i * 17 + round * 23) % 47)).ToArray();
                var anchor = (count / 2 + round) % count;
                var assigned = SmallGroupAssignment.Assign(members, goals, SimScalar.FromRatio(round, 8),
                    anchor, preserveAnchor);
                check(assigned.Order().SequenceEqual(Enumerable.Range(0, count)), "small assignment remains bijective");
                check(!preserveAnchor || assigned[anchor] == 0, "explicit anchor remains at its original slot");
                trace.AppendLine(string.Join(',', assigned));
            }
        }
        // Frozen using the unoptimized 7179989 assembly, independent of the optimized loop.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString().Replace("\r\n", "\n"))));
        check(digest == "8EF0AA6A9D7EA77883CE60AD55A097BA4CD9F8383B93023B6370E54AD9B6CD5E",
            "all sizes, repeated exchanges, ties and changed goals retain baseline assignments");
    }
}
