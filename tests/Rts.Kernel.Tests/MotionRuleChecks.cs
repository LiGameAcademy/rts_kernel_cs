using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class MotionRuleChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        void Near(double actual, double expected, string label) => check(Math.Abs(actual - expected) < 1e-10, label);
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { check(true, label); return; }
            check(false, label);
        }

        Near(MotionRules.TurnTowards(0, Math.PI / 2, 0.5, 1.0 / 30), Math.PI / 30,
            "turn rate converts revolutions to a fixed-frame angle");
        Near(MotionRules.TurnTowards(0, 0.01, 0.5, 1), 0.01, "turning does not overshoot");
        Near(MotionRules.TurnTowards(Radians(179), Radians(-179), 0.25, 0.01), Radians(179.9),
            "turning takes shortest route across the angle boundary");
        Near(MotionRules.TurnTowards(0, Math.PI, 0.25, 1), -Math.PI / 2, "half turn has stable negative tie direction");
        Near(MotionRules.TurnTowards(0, 1, 0, 1), 0, "zero turn rate preserves heading");
        Near(MotionRules.TurnTowards(0, 1, 1, 0), 0, "zero time preserves heading");
        var once = MotionRules.TurnTowards(0, Math.PI / 2, 0.25, 0.5);
        var incremental = 0.0;
        for (var i = 0; i < 15; i++) incremental = MotionRules.TurnTowards(incremental, Math.PI / 2, 0.25, 1.0 / 30);
        Near(incremental, once, "constant target fixed steps obey angular budget");
        Near(MotionRules.TurnSpeedScale(0, Radians(25)), 1, "small turn preserves full speed");
        Near(MotionRules.TurnSpeedScale(0, Radians(140)), 0.12, "large turn uses minimum speed");
        Near(MotionRules.TurnSpeedScale(0, Radians(82.5)), 0.56, "turn slowdown midpoint follows smoothstep");
        Near(MotionRules.TurnSpeedScale(0, Radians(-82.5)), 0.56, "left and right turn slowdown are symmetric");
        Near(MotionRules.TurnSpeedScale(0, Radians(53.75)), 0.8625, "turn slowdown preserves old smoothstep quarter point");
        Near(MotionRules.TurnSpeedScale(0, Radians(111.25)), 0.2575, "turn slowdown preserves old smoothstep three-quarter point");
        Reject(() => MotionRules.TurnTowards(double.NaN, 0, 1, 1), "nonfinite facing rejected");
        Reject(() => MotionRules.TurnTowards(0, 1, -1, 1), "negative turn rate rejected");
        Reject(() => MotionRules.TurnTowards(0, 1, 1, -1), "negative time rejected");
        Reject(() => MotionRules.TurnSpeedScale(0, 1, 25, 25), "invalid angle interval rejected");
        Reject(() => MotionRules.TurnSpeedScale(0, 1, minimumScale: 1.1), "invalid turn scale rejected");

        var flat = new TerrainHeights(2, 2, 100, default, new double[] { 20, 20, 20, 20 });
        var center = new SimVector2(50, 50);
        foreach (var end in new[] { new SimVector2(10, 50), new SimVector2(90, 50),
                     new SimVector2(50, 10), new SimVector2(50, 90), new SimVector2(90, 90) })
            Near(MotionRules.SlopeSpeedScale(flat, center, end), 1, "flat ground does not vary speed by XY direction");
        Near(MotionRules.SlopeSpeedScale(flat, center, center), 1, "stationary slope query returns full scale");
        var ramp = new TerrainHeights(2, 2, 100, default, new double[] { 0, 100, 0, 100 });
        var low = new SimVector2(10, 50);
        var high = new SimVector2(90, 50);
        Near(MotionRules.SlopeSpeedScale(ramp, low, high), 0.6, "rising elevation slows uphill and clamps slope");
        Near(MotionRules.SlopeSpeedScale(ramp, high, low), 0.85, "falling elevation uses downhill limit");
        Near(MotionRules.SlopeSpeedScale(ramp, new SimVector2(50, 10), new SimVector2(50, 90)), 1,
            "moving along contour does not slow down");
        var rise = Math.Tan(Radians(15)) * 100;
        var gentle = new TerrainHeights(2, 2, 100, default, new double[] { 0, rise, 0, rise });
        Near(MotionRules.SlopeSpeedScale(gentle, low, high), 0.8, "gentle uphill uses actual height over planar distance");
        Near(MotionRules.SlopeSpeedScale(gentle, high, low), 0.925, "gentle downhill interpolates independently");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, new SimVector2(101, 50)), "missing elevation is rejected explicitly");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, high, maximumDegrees: 0), "zero slope threshold rejected");
        Reject(() => MotionRules.SlopeSpeedScale(ramp, low, high, uphillScale: double.NaN), "nonfinite slope scale rejected");
    }
}
