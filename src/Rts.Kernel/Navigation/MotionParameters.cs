namespace Rts.Kernel.Navigation;

/// <summary>Diagnostic motion parameters; production hosts must obtain them from authoritative unit definitions.</summary>
public sealed record MotionParameters(
    double TurnRate = 0.5,
    bool ScaleTurnSpeed = true,
    double FullSpeedDegrees = 25,
    double MinimumSpeedDegrees = 140,
    double MinimumTurnScale = 0.12,
    bool ScaleSlopeSpeed = true,
    double MaximumSlopeDegrees = 30,
    double UphillScale = 0.6,
    double DownhillScale = 0.85)
{
    internal bool IsValid => double.IsFinite(TurnRate) && TurnRate >= 0.05 && TurnRate <= double.MaxValue / Math.Tau
        && double.IsFinite(FullSpeedDegrees) && FullSpeedDegrees >= 0
        && double.IsFinite(MinimumSpeedDegrees) && MinimumSpeedDegrees > FullSpeedDegrees && MinimumSpeedDegrees <= 180
        && Fraction(MinimumTurnScale) && double.IsFinite(MaximumSlopeDegrees)
        && MaximumSlopeDegrees > 0 && MaximumSlopeDegrees < 90 && Fraction(UphillScale) && Fraction(DownhillScale);

    private static bool Fraction(double value) => double.IsFinite(value) && value >= 0 && value <= 1;
}
