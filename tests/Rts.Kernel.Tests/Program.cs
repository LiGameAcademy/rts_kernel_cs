using Rts.Kernel;
using Rts.Kernel.Navigation;

var checks = 0;

void Check(bool condition, string message)
{
    checks++;
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

void CheckThrows<TException>(Action action, string message) where TException : Exception
{
    checks++;
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

if (args.Contains("--perf") || args.Contains("--perf-long"))
{
    MovementPerformance.Run(args.Contains("--perf-long"));
    return;
}

if (args.Contains("--crowd-only"))
{
    CrowdMovementChecks.Run(Check);
    Console.WriteLine($"Rts.Kernel crowd checks PASS ({checks} checks)");
    return;
}

RtsMatch CreateMovingMatch()
{
    var match = new RtsMatch(MatchConfig.Default, 1234);
    Check(match.SubmitCommand(CommandEnvelope.Spawn(1, 1, 0, new SimVector2(10, 20))).Accepted, "spawn accepted");
    match.Step();
    var id = match.Entities.Single().Id;
    Check(match.SubmitCommand(CommandEnvelope.SetVelocity(2, 1, 1, id, new SimVector2(30, -15))).Accepted, "velocity accepted");
    match.Step();
    return match;
}

var original = CreateMovingMatch();
var moving = original.Entities.Single();
Check(moving.Id.Value == 1, "entity ids start at one");
Check(moving.Position == new SimVector2(11, 19.5), "fixed tick movement");
Check(!original.SubmitCommand(CommandEnvelope.Spawn(original.Frame, 1, 2, SimVector2.Zero)).Accepted, "past command rejected");
Check(original.SubmitCommand(CommandEnvelope.Stop(10, 1, 3, moving.Id)).Accepted, "future command accepted");

var snapshotJson = SnapshotJson.Serialize(original.CaptureSnapshot());
var restored = RtsMatch.Restore(SnapshotJson.Deserialize(snapshotJson));
Check(original.ComputeStateHash() == restored.ComputeStateHash(), "snapshot round trip hash");
var originalHash = original.ComputeStateHash();
var restoredHash = restored.ComputeStateHash();
var originalRandomCopy = new DeterministicRng(original.CaptureSnapshot().RngState);
var restoredRandomCopy = new DeterministicRng(restored.CaptureSnapshot().RngState);
for (var draw = 0; draw < 20; draw++)
{
    Check(originalRandomCopy.NextUInt64() == restoredRandomCopy.NextUInt64(), "snapshot random sequence restored");
}
Check(original.ComputeStateHash() == originalHash && restored.ComputeStateHash() == restoredHash,
    "consuming detached random streams cannot alter either match");
var canonicalSnapshot = original.CaptureSnapshot();
Check(SnapshotDiff.FindFirst(canonicalSnapshot, canonicalSnapshot) is null, "identical snapshots have no diff");
Check(SnapshotDiff.FindFirst(canonicalSnapshot, canonicalSnapshot with { Frame = canonicalSnapshot.Frame + 1 })?.Path == "frame", "frame diff path");
var changedEntities = canonicalSnapshot.Entities.ToArray();
changedEntities[0] = changedEntities[0] with { PositionX = changedEntities[0].PositionX + 1 };
Check(SnapshotDiff.FindFirst(canonicalSnapshot, canonicalSnapshot with { Entities = changedEntities })?.Path == "entities[0].positionX", "entity field diff path");
var changedPending = canonicalSnapshot.PendingCommands.ToArray();
changedPending[0] = changedPending[0] with
{
    Command = changedPending[0].Command with { ExecuteFrame = changedPending[0].Command.ExecuteFrame + 1 },
};
Check(SnapshotDiff.FindFirst(canonicalSnapshot, canonicalSnapshot with { PendingCommands = changedPending })?.Path == "pendingCommands[0].executeFrame", "command field diff path");
CheckThrows<InvalidDataException>(
    () => RtsMatch.Restore(canonicalSnapshot with { NextEntityId = 1 }),
    "non-canonical next entity id rejected");

for (var i = 0; i < 20; i++)
{
    original.Step();
    restored.Step();
    Check(original.ComputeStateHash() == restored.ComputeStateHash(), $"restored state at step {i}");
}

var left = CreateMovingMatch();
var right = CreateMovingMatch();
left.Step();
left.Step();
right.Step();
Check(left.Frame == right.Frame + 1, "matches keep independent clocks");
Check(left.ComputeStateHash() != right.ComputeStateHash(), "matches keep independent state");
right.Step();
Check(left.ComputeStateHash() == right.ComputeStateHash(), "same inputs converge after interleaved stepping");

var rejected = new RtsMatch(MatchConfig.Default, 99);
rejected.SubmitCommand(CommandEnvelope.SetVelocity(1, 4, 0, new EntityId(999), new SimVector2(1, 1)));
rejected.Step();
var rejectionEvent = rejected.DrainEvents().Single();
Check(rejectionEvent.Kind == MatchEventKind.CommandRejected, "execution rejection is an event");
Check(rejectionEvent.Frame == 1, "event carries simulation frame");

var rawFlags = new byte[] { 0, 2, 4, 8 };
var grid = new PathingGrid(2, 2, 32, new SimVector2(-32, -32), rawFlags, new byte[] { 2, 0, 0, 0 });
rawFlags[2] = 2;
Check(!grid.IsWalkable(new GridCell(0, 0)), "obstacle flags combined");
Check(grid.IsWalkable(new GridCell(0, 1)), "no-fly flag permits walking and input buffers are copied");
Check(!grid.IsWalkable(new GridCell(2, 0)), "out of bounds blocked");
Check(grid.TryWorldToCell(new SimVector2(-0.01, -0.01), out var cell) && cell == new GridCell(0, 0), "negative origin and floor semantics");
Check(!grid.TryWorldToCell(new SimVector2(32, 0), out _), "upper edge excluded");
Check(!grid.TryWorldToCell(new SimVector2(double.NaN, 0), out _), "nonfinite coordinate rejected");
CheckThrows<ArgumentException>(() => new PathingGrid(2, 2, 32, default, new byte[3]), "truncated map rejected");
var terrain = new TerrainHeights(2, 2, 128, new SimVector2(-128, -128), new double[] { 0, 10, 20, 30 });
Check(terrain.TrySample(new SimVector2(-64, -64), out var altitude) && altitude == 15, "bilinear height sample");
Check(terrain.TrySample(SimVector2.Zero, out altitude) && altitude == 30, "last corner samples final quad");
Check(!terrain.TrySample(new SimVector2(1, 0), out _), "height outside map explicitly missing");
CheckThrows<ArgumentException>(() => new TerrainHeights(2, 2, 128, default, new double[] {0, 0, 0, double.NaN}), "nonfinite height rejected");

CommandContractChecks.Run(Check);
PathfindingChecks.Run(Check);
NavigationChecks.Run(Check);
MovementChecks.Run(Check);
MotionRuleChecks.Run(Check);
MotionStateChecks.Run(Check);
MotionIntegrationChecks.Run(Check);
FormationGeometryChecks.Run(Check);
CrowdMovementChecks.Run(Check);
GroupMovementChecks.Run(Check);
GroupArrivalChecks.Run(Check);
GroupEdgeChecks.Run(Check);
GroupPlanningChecks.Run(Check);
GroupSupersessionChecks.Run(Check);
OrderQueueChecks.Run(Check);
OrderSnapshotChecks.Run(Check);

Console.WriteLine($"Rts.Kernel.Tests PASS ({checks} checks)");
