namespace Rts.Kernel.Navigation;

/// <summary>Pure fixed-point motion. Angles are radians; turn rates are revolutions per second.</summary>
public static class MotionRules
{
    public static SimScalar TurnTowards(SimScalar facing, SimScalar desiredFacing, SimScalar turnRate, SimScalar seconds)
    {
        if (turnRate < 0 || turnRate > SimulationDomain.CapabilityLimit || seconds < 0 || seconds > 1)
            throw new ArgumentOutOfRangeException(nameof(turnRate));
        var maximum = turnRate * seconds * SimMath.Tau;
        var difference = AngleDifference(facing, desiredFacing);
        return Normalize(Normalize(facing) + SimMath.Clamp(difference, -maximum, maximum));
    }

    public static SimScalar TurnSpeedScale(SimScalar facing, SimScalar desiredFacing,
        SimScalar? fullSpeedDegrees = null, SimScalar? minimumSpeedDegrees = null, SimScalar? minimumScale = null)
    {
        var full = fullSpeedDegrees ?? (SimScalar)25;
        var minimum = minimumSpeedDegrees ?? (SimScalar)140;
        var limit = minimumScale ?? SimScalar.FromRatio(3, 25);
        if (full < 0 || minimum <= full || minimum > 180 || limit < 0 || limit > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumSpeedDegrees));
        var degrees = SimMath.Abs(AngleDifference(facing, desiredFacing)) * 180 / SimMath.Pi;
        if (degrees <= full) return 1;
        if (degrees >= minimum) return limit;
        var progress = (degrees - full) / (minimum - full);
        progress = progress * progress * (3 - 2 * progress);
        return 1 + (limit - 1) * progress;
    }

    /// <summary>Sampled elevation over planar distance; missing terrain remains an explicit failure.</summary>
    public static SimScalar SlopeSpeedScale(TerrainHeights terrain, FixedVector2 from, FixedVector2 to,
        SimScalar? maximumDegrees = null, SimScalar? uphillScale = null, SimScalar? downhillScale = null)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        var maximum = maximumDegrees ?? (SimScalar)30;
        var uphill = uphillScale ?? SimScalar.FromRatio(3, 5);
        var downhill = downhillScale ?? SimScalar.FromRatio(17, 20);
        if (maximum <= 0 || maximum >= 90 || uphill < 0 || uphill > 1 || downhill < 0 || downhill > 1)
            throw new ArgumentOutOfRangeException(nameof(maximumDegrees));
        if (!terrain.TrySample(from, out var startHeight) || !terrain.TrySample(to, out var endHeight))
            throw new ArgumentException("Both motion endpoints must be inside the height field.");
        var distance = SquaredDistance.Between(from, to).Length;
        var rise = endHeight - startHeight;
        if (distance == 0) return 1;
        var degrees = SimMath.Atan2(SimMath.Abs(rise), distance) * 180 / SimMath.Pi;
        var progress = degrees >= maximum ? SimScalar.One : degrees / maximum;
        return 1 + ((rise > 0 ? uphill : downhill) - 1) * progress;
    }

    private static SimScalar AngleDifference(SimScalar from, SimScalar to) => Normalize(Normalize(to) - Normalize(from));
    internal static SimScalar Normalize(SimScalar angle) => SimMath.NormalizeRadians(angle);
}
