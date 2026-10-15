namespace Rts.Kernel;

public sealed partial class RtsMatch
{
    public const int CrowdBlockedSeconds = CrowdStepper.CrowdBlockedSeconds;
    public const int LocalReplansPerFrame = CrowdStepper.LocalReplansPerFrame;
    internal const int LocalRetryFrames = CrowdStepper.LocalRetryFrames;

    private void ValidateGroundBodies() => _crowd.ValidateGroundBodies();

    private void AdvanceCrowd(int tickRate) =>
        _crowd.Advance(Frame, tickRate, _navigation!, ref _pathsDirty);
}
