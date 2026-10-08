namespace Rts.Kernel.Navigation;

/// <summary>Pure motion calculations. Angles are radians; turn rates are revolutions per second.</summary>
public static class MotionRules
{
    public static double TurnTowards(double facing, double desiredFacing, double turnRate, double seconds)
    {
        Finite(facing, nameof(facing));
        Finite(desiredFacing, nameof(desiredFacing));
        Nonnegative(turnRate, nameof(turnRate));
        Nonnegative(seconds, nameof(seconds));
        var maximum = turnRate * Math.Tau * seconds;
        Finite(maximum, nameof(turnRate));
        var difference = AngleDifference(facing, desiredFacing);
        return Normalize(Normalize(facing) + Math.Clamp(difference, -maximum, maximum));
    }

    public static double TurnSpeedScale(double facing, double desiredFacing,
        double fullSpeedDegrees = 25, double minimumSpeedDegrees = 140, double minimumScale = 0.12)
    {
        Finite(facing, nameof(facing));
        Finite(desiredFacing, nameof(desiredFacing));
        Nonnegative(fullSpeedDegrees, nameof(fullSpeedDegrees));
        Finite(minimumSpeedDegrees, nameof(minimumSpeedDegrees));
        Fraction(minimumScale, nameof(minimumScale));
        if (minimumSpeedDegrees <= fullSpeedDegrees || minimumSpeedDegrees > 180)
            throw new ArgumentOutOfRangeException(nameof(minimumSpeedDegrees));
        var degrees = Math.Abs(AngleDifference(facing, desiredFacing)) * 180 / Math.PI;
        var progress = Math.Clamp((degrees - fullSpeedDegrees) / (minimumSpeedDegrees - fullSpeedDegrees), 0, 1);
        progress = progress * progress * (3 - 2 * progress);
        return 1 + (minimumScale - 1) * progress;
    }

    /// <summary>Uses sampled elevation change over planar distance; missing terrain is an explicit failure.</summary>
    public static double SlopeSpeedScale(TerrainHeights terrain, SimVector2 from, SimVector2 to,
        double maximumDegrees = 30, double uphillScale = 0.6, double downhillScale = 0.85)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        Finite(maximumDegrees, nameof(maximumDegrees));
        if (maximumDegrees <= 0 || maximumDegrees >= 90)
            throw new ArgumentOutOfRangeException(nameof(maximumDegrees));
        Fraction(uphillScale, nameof(uphillScale));
        Fraction(downhillScale, nameof(downhillScale));
        if (!terrain.TrySample(from, out var startHeight) || !terrain.TrySample(to, out var endHeight))
            throw new ArgumentException("Both motion endpoints must be inside the height field.");
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var rise = endHeight - startHeight;
        Finite(distance, nameof(to));
        Finite(rise, nameof(terrain));
        if (distance == 0) return 1;
        var degrees = Math.Atan2(Math.Abs(rise), distance) * 180 / Math.PI;
        var progress = Math.Min(degrees / maximumDegrees, 1);
        var limit = rise > 0 ? uphillScale : downhillScale;
        return 1 + (limit - 1) * progress;
    }

    // Normalize operands first to avoid overflowing subtraction of finite angles.
    private static double AngleDifference(double from, double to) => Normalize(Normalize(to) - Normalize(from));

    private static double Normalize(double angle)
    {
        var wrapped = angle % Math.Tau;
        if (wrapped >= Math.PI) wrapped -= Math.Tau;
        if (wrapped < -Math.PI) wrapped += Math.Tau;
        return wrapped;
    }

    private static void Finite(double value, string name)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }

    private static void Nonnegative(double value, string name)
    {
        Finite(value, name);
        if (value < 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void Fraction(double value, string name)
    {
        Finite(value, name);
        if (value < 0 || value > 1) throw new ArgumentOutOfRangeException(name);
    }
}
