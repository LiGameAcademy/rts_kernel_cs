using FixPointCS;

namespace Rts.Kernel.Numerics;

/// <summary>Exact integer roots and a bounded pinned transcendental adapter; upstream types stay private.</summary>
public static class SimMath
{
    public static SimScalar Pi => SimScalar.FromRaw(Fixed64.Pi);
    // One authoritative period must be exactly twice the chosen half-period.
    public static SimScalar Tau => Pi + Pi;
    public static SimScalar Abs(SimScalar value) => value.Raw < 0 ? -value : value;
    public static SimScalar Min(SimScalar a, SimScalar b) => a < b ? a : b;
    public static SimScalar Max(SimScalar a, SimScalar b) => a > b ? a : b;
    public static SimScalar Clamp(SimScalar value, SimScalar minimum, SimScalar maximum)
    {
        if (minimum > maximum) throw new ArgumentException("Minimum exceeds maximum.");
        return Min(Max(value, minimum), maximum);
    }
    public static SimScalar Sqrt(SimScalar value) => value.Raw < 0
        ? throw new ArgumentOutOfRangeException(nameof(value)) : SimScalar.FromRaw(checked((long)WideIntegerMath.FloorRoot((UInt128)value.Raw << 32)));
    public static SimScalar NormalizeRadians(SimScalar radians)
    {
        var raw = radians.Raw % Tau.Raw;
        if (raw >= Pi.Raw) raw -= Tau.Raw;
        if (raw < -Pi.Raw) raw += Tau.Raw;
        return SimScalar.FromRaw(raw);
    }
    public static SimScalar Sin(SimScalar radians) => SimScalar.FromRaw(Fixed64.Sin(NormalizeRadians(radians).Raw));
    public static SimScalar Cos(SimScalar radians) => SimScalar.FromRaw(Fixed64.Cos(NormalizeRadians(radians).Raw));
    public static SimScalar Atan2(SimScalar y, SimScalar x)
    {
        if (x.Raw == 0 && y.Raw == 0) throw new ArgumentException("A zero vector has no heading.");
        if (x.Raw == 0) return y.Raw > 0 ? Pi / 2 : -Pi / 2;
        if (y.Raw == 0) return x.Raw > 0 ? SimScalar.Zero : -Pi;
        // The pinned approximation complements negative inputs. Normalize first so a
        // one-raw-unit negative direction cannot collapse to zero inside the library.
        var magnitude = Int128.Max(Int128.Abs((Int128)x.Raw), Int128.Abs((Int128)y.Raw));
        const long normalizedScale = 1L << 60;
        var nx = checked((long)((Int128)x.Raw * normalizedScale / magnitude));
        var ny = checked((long)((Int128)y.Raw * normalizedScale / magnitude));
        return NormalizeRadians(SimScalar.FromRaw(Fixed64.Atan2(ny, nx)));
    }
}
