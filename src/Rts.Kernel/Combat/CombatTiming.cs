namespace Rts.Kernel.Combat;

/// <summary>Converts content-boundary seconds; the runtime stores integer frame deadlines.</summary>
public static class CombatTiming
{
    public const int MaxSeconds = 3600;
    public const int MaxFrames = MaxSeconds * 240;

    public static int FramesFromSeconds(SimScalar seconds, int tickRate)
    {
        if (tickRate is < 1 or > 240) throw new ArgumentOutOfRangeException(nameof(tickRate));
        CombatLimits.RequireRange(seconds, 0, MaxSeconds, nameof(seconds));
        Int128 scaled = (Int128)seconds.Raw * tickRate;
        return checked((int)((scaled + SimScalar.Scale - 1) / SimScalar.Scale));
    }
}
