using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MotionRuleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static SimScalar Radians(SimScalar degrees) => degrees * SimMath.Pi / 180;
        void Near(SimScalar actual, SimScalar expected, string label) => check(SimMath.Abs(actual - expected) <= SimScalar.FromRaw(512), label);
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { check(true, label); return; }
            check(false, label);
        }

        Near(MotionRules.TurnTowards(0, SimMath.Pi / 2, SimScalar.Parse("0.5"), SimScalar.FromRatio(1, 30)), SimMath.Pi / 30,
            "turn rate converts revolutions to a fixed-frame angle");
        Near(MotionRules.TurnTowards(0, SimScalar.Parse("0.01"), SimScalar.Parse("0.5"), 1), SimScalar.Parse("0.01"), "turning does not overshoot");
        Near(MotionRules.TurnTowards(Radians(179), Radians(-179), SimScalar.Parse("0.25"), SimScalar.Parse("0.01")), Radians(SimScalar.Parse("179.9")),
            "turning takes shortest route across the angle boundary");
        Near(MotionRules.TurnTowards(0, SimMath.Pi, SimScalar.Parse("0.25"), 1), -SimMath.Pi / 2, "half turn has stable negative tie direction");
        Near(MotionRules.TurnTowards(0, 1, 0, 1), 0, "zero turn rate preserves heading");
        Near(MotionRules.TurnTowards(0, 1, 1, 0), 0, "zero time preserves heading");
        var once = MotionRules.TurnTowards(0, SimMath.Pi / 2, SimScalar.Parse("0.25"), SimScalar.Parse("0.5"));
        SimScalar incremental = 0;
        for (var i = 0; i < 15; i++) incremental = MotionRules.TurnTowards(incremental, SimMath.Pi / 2, SimScalar.Parse("0.25"), SimScalar.FromRatio(1, 30));
        Near(incremental, once, "constant target fixed steps obey angular budget");
        Near(MotionRules.TurnSpeedScale(0, Radians(25)), 1, "small turn preserves full speed");
        Near(MotionRules.TurnSpeedScale(0, Radians(140)), SimScalar.Parse("0.12"), "large turn uses minimum speed");
        Near(MotionRules.TurnSpeedScale(0, Radians(SimScalar.Parse("82.5"))), SimScalar.Parse("0.56"), "turn slowdown midpoint follows smoothstep");
        Near(MotionRules.TurnSpeedScale(0, Radians(-SimScalar.Parse("82.5"))), SimScalar.Parse("0.56"), "left and right turn slowdown are symmetric");
        Near(MotionRules.TurnSpeedScale(0, Radians(SimScalar.Parse("53.75"))), SimScalar.Parse("0.8625"), "turn slowdown preserves old smoothstep quarter point");
        Near(MotionRules.TurnSpeedScale(0, Radians(SimScalar.Parse("111.25"))), SimScalar.Parse("0.2575"), "turn slowdown preserves old smoothstep three-quarter point");
        Reject(() => MotionRules.TurnTowards(0, 1, SimScalar.MaxValue, 1), "out-of-domain turn rate rejected");
        Reject(() => MotionRules.TurnTowards(0, 1, -1, 1), "negative turn rate rejected");
        Reject(() => MotionRules.TurnTowards(0, 1, 1, -1), "negative time rejected");
        Reject(() => MotionRules.TurnSpeedScale(0, 1, 25, 25), "invalid angle interval rejected");
        Reject(() => MotionRules.TurnSpeedScale(0, 1, minimumScale: SimScalar.Parse("1.1")), "invalid turn scale rejected");

        var flat = new TerrainHeights(2, 2, 100, default, new SimScalar[] { 20, 20, 20, 20 });
        var center = new FixedVector2(50, 50);
        foreach (var end in new[] { new FixedVector2(10, 50), new FixedVector2(90, 50),
                     new FixedVector2(50, 10), new FixedVector2(50, 90), new FixedVector2(90, 90) })
            Near(MotionRules.SlopeSpeedScale(flat, center, end), 1, "flat ground does not vary speed by XY direction");
        Near(MotionRules.SlopeSpeedScale(flat, center, center), 1, "stationary slope query returns full scale");
        var ramp = new TerrainHeights(2, 2, 100, default, new SimScalar[] { 0, 100, 0, 100 });
        var low = new FixedVector2(10, 50);
        var high = new FixedVector2(90, 50);
        Near(MotionRules.SlopeSpeedScale(ramp, low, high), SimScalar.Parse("0.6"), "rising elevation slows uphill and clamps slope");
        Near(MotionRules.SlopeSpeedScale(ramp, high, low), SimScalar.Parse("0.85"), "falling elevation uses downhill limit");
        Near(MotionRules.SlopeSpeedScale(ramp, new FixedVector2(50, 10), new FixedVector2(50, 90)), 1,
            "moving along contour does not slow down");
        var rise = SimScalar.Parse((Math.Tan(Math.PI / 12) * 100).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        var gentle = new TerrainHeights(2, 2, 100, default, new SimScalar[] { 0, rise, 0, rise });
        Near(MotionRules.SlopeSpeedScale(gentle, low, high), SimScalar.Parse("0.8"), "gentle uphill uses actual height over planar distance");
        Near(MotionRules.SlopeSpeedScale(gentle, high, low), SimScalar.Parse("0.925"), "gentle downhill interpolates independently");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, new FixedVector2(101, 50)), "missing elevation is rejected explicitly");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, high, maximumDegrees: 0), "zero slope threshold rejected");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, high, uphillScale: SimScalar.MaxValue), "out-of-range slope scale rejected");
    }
}
