namespace Rts.Kernel.Numerics;

/// <summary>Admitted world range; keeps 500-member squared matching sums safely within signed Int128.</summary>
public static class SimulationDomain
{
    public static SimScalar CoordinateLimit => SimScalar.FromInteger(1 << 20);
    public static SimScalar CapabilityLimit => SimScalar.FromInteger(1 << 16);
    public static bool Contains(SimScalar value) => value >= -CoordinateLimit && value <= CoordinateLimit;
    public static bool Contains(FixedVector2 point) => Contains(point.X) && Contains(point.Y);
    public static bool IsVelocity(FixedVector2 value) => value.X >= -CapabilityLimit && value.X <= CapabilityLimit
        && value.Y >= -CapabilityLimit && value.Y <= CapabilityLimit;
    public static bool IsCapability(SimScalar value) => value > 0 && value <= CapabilityLimit;
}
