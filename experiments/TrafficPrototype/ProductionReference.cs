using Rts.Kernel;
using Rts.Kernel.Navigation;
using Rts.Kernel.Numerics;

namespace Rts.TrafficPrototype;

internal static class ProductionReference
{
    internal static IReadOnlyList<FixedVector2> ConvoyFirstFrame()
    {
        var grid = new PathingGrid(32, 8, 10, FixedVector2.Zero, new byte[256]);
        var match = new RtsMatch(MatchConfig.Default, 1, grid,
            movementDefinitions: [new MovementDefinition(1, 180, 4)]);
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 0, new FixedVector2(15, 15), 1));
        match.SubmitCommand(CommandEnvelope.Spawn(1, 0, 1, new FixedVector2(25, 15), 1));
        match.Step();
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 2, new EntityId(1), new FixedVector2(195, 15), 180));
        match.SubmitCommand(CommandEnvelope.MoveTo(2, 0, 3, new EntityId(2), new FixedVector2(205, 15), 180));
        match.Step();
        return match.Entities.Select(item => item.Position).ToArray();
    }
}
