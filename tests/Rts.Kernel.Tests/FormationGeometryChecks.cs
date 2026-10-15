using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class FormationGeometryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var kind in Enum.GetValues<FormationKind>())
        foreach (var count in new[] { 1, 2, 3, 5, 12, 50, 500 })
        {
            var points = FormationLayout.Create(kind, count, 10, new FixedVector2(4, 9), SimScalar.FromRatio(7, 10));
            check(points.Count == count && points[0] == new FixedVector2(4, 9), $"{kind}/{count}: exact count and anchor");
            // The pinned trig error (<=1e-8) grows with ideal radius; final body placement is exact.
            var extent = points.Max(p => SimMath.Max(SimMath.Abs(p.X), SimMath.Abs(p.Y)));
            var error = extent * SimScalar.FromRatio(1, 10000000) + SimScalar.FromRaw(8);
            var minimum = (SimScalar)10 - error;
            var separated = true;
            for (var i = 0; i < count; i++)
            for (var j = i + 1; j < count; j++)
            {
                var dx = points[i].X - points[j].X;
                var dy = points[i].Y - points[j].Y;
                separated &= SquaredDistance.Between(points[i], points[j]).Raw >= (Int128)minimum.Raw * minimum.Raw;
            }
            check(separated, $"{kind}/{count}: all slots satisfy minimum separation");
            var unrotated = FormationLayout.Create(kind, count, 10, FixedVector2.Zero, 0);
            var rotated = FormationLayout.Create(kind, count, 10, FixedVector2.Zero, SimMath.Pi / 2);
            check(unrotated.Zip(rotated).All(pair => SimMath.Abs(pair.First.X - pair.Second.Y) <= error
                && SimMath.Abs(pair.First.Y + pair.Second.X) <= error), $"{kind}/{count}: common +X forward axis");
        }
        var starts = new[] { FixedVector2.Zero, new FixedVector2(10, 0), new FixedVector2(0, 10), new FixedVector2(10, 10) };
        var slots = new[] { FixedVector2.Zero, new FixedVector2(10, 10), new FixedVector2(10, 0), new FixedVector2(0, 10) };
        check(FormationLayout.Match(starts, slots, 0).SequenceEqual(new[] { 0, 2, 3, 1 }), "matching minimizes ideal travel");
        var tie = Enumerable.Repeat(FixedVector2.Zero, 10).ToArray();
        check(FormationLayout.Match(tie, tie, 7).SequenceEqual(FormationLayout.Match(tie, tie, 7)), "equal-cost assignment stable");
        check(FormationLayout.Match(tie, tie, 7)[7] == 0, "explicit anchor fixed in slot zero");
        var large = FormationLayout.Create(FormationKind.Compact, 500, 10, FixedVector2.Zero, 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var assignment = FormationLayout.Match(large.Reverse().ToArray(), large, 499);
        watch.Stop();
        check(assignment.Distinct().Count() == 500 && assignment[499] == 0, "500 member assignment is bijective");
        Console.WriteLine($"formation_matching_500 elapsed_ms={watch.Elapsed.TotalMilliseconds:F3}");
        foreach (var spacing in new SimScalar[] { 0, -1, SimScalar.MaxValue })
        {
            var rejected = false;
            try { FormationLayout.Create(FormationKind.Circle, 5, spacing, FixedVector2.Zero, 0); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "invalid spacing rejected");
        }
        var overflow = false;
        try { FormationLayout.Match(new[] { FixedVector2.Zero, new FixedVector2(SimScalar.MaxValue, 0) },
            new[] { FixedVector2.Zero, new FixedVector2(SimScalar.MinValue, 0) }, 0); }
        catch (ArgumentException) { overflow = true; }
        check(overflow, "out-of-domain matching input rejected");
    }
}
