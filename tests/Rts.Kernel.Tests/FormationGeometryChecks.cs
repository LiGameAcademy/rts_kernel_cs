using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FormationGeometryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var kind in Enum.GetValues<FormationKind>())
        foreach (var count in new[] { 1, 2, 3, 5, 12, 50, 500 })
        {
            var points = FormationLayout.Create(kind, count, 10, new SimVector2(4, 9), 0.7);
            check(points.Count == count && points[0] == new SimVector2(4, 9), $"{kind}/{count}: exact count and anchor");
            var separated = true;
            for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                separated &= dx * dx + dy * dy >= 100 - 1e-8;
            }
            check(separated, $"{kind}/{count}: all slots satisfy minimum separation");
            var unrotated = FormationLayout.Create(kind, count, 10, SimVector2.Zero, 0);
            var rotated = FormationLayout.Create(kind, count, 10, SimVector2.Zero, Math.PI / 2);
            check(unrotated.Zip(rotated).All(pair => Math.Abs(pair.First.X - pair.Second.Y) < 1e-8
                && Math.Abs(pair.First.Y + pair.Second.X) < 1e-8), $"{kind}/{count}: common +X forward axis");
        }
        var starts = new[] { SimVector2.Zero, new SimVector2(10, 0), new SimVector2(0, 10), new SimVector2(10, 10) };
        var slots = new[] { SimVector2.Zero, new SimVector2(10, 10), new SimVector2(10, 0), new SimVector2(0, 10) };
        check(FormationLayout.Match(starts, slots, 0).SequenceEqual(new[] { 0, 2, 3, 1 }), "matching minimizes ideal travel");
        var tie = Enumerable.Repeat(SimVector2.Zero, 10).ToArray();
        check(FormationLayout.Match(tie, tie, 7).SequenceEqual(FormationLayout.Match(tie, tie, 7)), "equal-cost assignment stable");
        check(FormationLayout.Match(tie, tie, 7)[7] == 0, "explicit anchor fixed in slot zero");
        var large = FormationLayout.Create(FormationKind.Compact, 500, 10, SimVector2.Zero, 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var assignment = FormationLayout.Match(large.Reverse().ToArray(), large, 499);
        watch.Stop();
        check(assignment.Distinct().Count() == 500 && assignment[499] == 0, "500 member assignment is bijective");
        Console.WriteLine($"formation_matching_500 elapsed_ms={watch.Elapsed.TotalMilliseconds:F3}");
        foreach (var spacing in new[] { 0.0, -1, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try { FormationLayout.Create(FormationKind.Circle, 5, spacing, SimVector2.Zero, 0); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid spacing rejected");
        }
        var overflow = false;
        try { FormationLayout.Match(new[] { SimVector2.Zero, new SimVector2(1e308, 0) },
            new[] { SimVector2.Zero, new SimVector2(-1e308, 0) }, 0); }
        catch (ArgumentException) { overflow = true; }
        check(overflow, "nonfinite matching arithmetic rejected");
    }
}
