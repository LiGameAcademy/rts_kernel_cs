namespace Rts.Kernel.Navigation;

/// <summary>Frozen rule parameters. Turn rate uses revolutions/second; angles below are degrees.</summary>
public sealed record MotionParameters
{
    public SimScalar TurnRate { get; init; }
    public bool ScaleTurnSpeed { get; init; }
    public SimScalar FullSpeedDegrees { get; init; }
    public SimScalar MinimumSpeedDegrees { get; init; }
    public SimScalar MinimumTurnScale { get; init; }
    public bool ScaleSlopeSpeed { get; init; }
    public SimScalar MaximumSlopeDegrees { get; init; }
    public SimScalar UphillScale { get; init; }
    public SimScalar DownhillScale { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    public MotionParameters() : this(TurnRate: null) { }

    public MotionParameters(SimScalar? TurnRate = null, bool ScaleTurnSpeed = true,
        SimScalar? FullSpeedDegrees = null, SimScalar? MinimumSpeedDegrees = null,
        SimScalar? MinimumTurnScale = null, bool ScaleSlopeSpeed = true,
        SimScalar? MaximumSlopeDegrees = null, SimScalar? UphillScale = null, SimScalar? DownhillScale = null)
    {
        this.TurnRate = TurnRate ?? SimScalar.FromRatio(1, 2);
        this.ScaleTurnSpeed = ScaleTurnSpeed;
        this.FullSpeedDegrees = FullSpeedDegrees ?? (SimScalar)25;
        this.MinimumSpeedDegrees = MinimumSpeedDegrees ?? (SimScalar)140;
        this.MinimumTurnScale = MinimumTurnScale ?? SimScalar.FromRatio(3, 25);
        this.ScaleSlopeSpeed = ScaleSlopeSpeed;
        this.MaximumSlopeDegrees = MaximumSlopeDegrees ?? (SimScalar)30;
        this.UphillScale = UphillScale ?? SimScalar.FromRatio(3, 5);
        this.DownhillScale = DownhillScale ?? SimScalar.FromRatio(17, 20);
    }

    internal bool IsValid => TurnRate >= SimScalar.FromRatio(1, 20) && TurnRate <= SimulationDomain.CapabilityLimit
        && FullSpeedDegrees >= 0 && MinimumSpeedDegrees > FullSpeedDegrees && MinimumSpeedDegrees <= 180
        && Fraction(MinimumTurnScale) && MaximumSlopeDegrees > 0 && MaximumSlopeDegrees < 90
        && Fraction(UphillScale) && Fraction(DownhillScale);
    private static bool Fraction(SimScalar value) => value >= 0 && value <= 1;
}
