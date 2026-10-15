using Rts.Kernel;

if (args.Contains("--crowd")) { CrowdSample.Run(); return; }
if (args.Contains("--formation")) { FormationSample.Run(); return; }
if (args.Contains("--orders")) { OrdersSample.Run(); return; }
if (args.Contains("--motion")) { MotionSample.Run(); return; }
if (args.Contains("--navigation")) { NavigationSample.Run(); return; }

var match = new RtsMatch(MatchConfig.Default, seed: 0xC0FFEEUL);
match.SubmitCommand(CommandEnvelope.Spawn(1, playerId: 0, sequence: 0, new FixedVector2(128, 256)));
match.Step();

var entityId = match.Entities.Single().Id;
match.SubmitCommand(CommandEnvelope.SetVelocity(2, playerId: 0, sequence: 1, entityId, new FixedVector2(30, 0)));
for (var i = 0; i < 30; i++)
{
    match.Step();
}

var entity = match.Entities.Single();
Console.WriteLine($"frame={match.Frame} entity={entity.Id} position=({entity.Position.X.ToPresentationDouble():F3},{entity.Position.Y.ToPresentationDouble():F3}) hash={match.ComputeStateHash()}");
