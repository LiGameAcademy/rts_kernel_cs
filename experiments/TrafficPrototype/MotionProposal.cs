using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

/// <summary>One straight, constant-speed motion over the same normalized frame interval [0,1].</summary>
internal sealed record MotionProposal(ulong Id, FixedVector2 Start, FixedVector2 DesiredEnd,
    SimScalar Radius, int WaitAge = 0, bool Stopped = false)
{
    internal void Validate()
    {
        if (Id == 0 || !SimulationDomain.Contains(Start) || !SimulationDomain.Contains(DesiredEnd)
            || !SimulationDomain.IsCapability(Radius) || WaitAge < 0 || (Stopped && Start != DesiredEnd))
            throw new ArgumentException("Invalid frame proposal or Stop displacement.");
    }
}

internal sealed record ResolvedMotion(ulong Id, FixedVector2 Start, FixedVector2 End,
    SimScalar Radius, bool HeldForConflict);

internal sealed record MotionResolution(IReadOnlyList<ResolvedMotion> Motions, int PairChecks, int Passes);
