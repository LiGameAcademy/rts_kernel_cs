using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NumericPrototype;

internal static class PlanningLoadProbe
{
    private sealed record Sample(int Members, string Formation, int Repeat, int Frames, int Succeeded, int Failed,
        double CreateMs, double PeakTickMs, double PlacementMs, long AllocatedBytes, string StateHash,
        double MatchingMs, double CandidateMs, double ReachabilityMs,
        double MatchingPeakMs, double CandidatePeakMs, double MatchingInitializationMs, double PreparationPeakMs, string OutcomeHash, double PlacementPeakMs);
    internal static object Run(string assetsDirectory)
    {
        var samples = new List<Sample>();
        string? world = null;
        foreach (var count in new[] { 300, 500 })
        for (var formation = 0; formation < 4; formation++)
        {
            string? baseline = null;
            for (var repeat = 1; repeat <= 2; repeat++)
            {
                using var session = new PlanningPreviewSession(assetsDirectory, count);
                var map = session.ReadMap();
                world = session.WorldIdentity;
                var x = map.Origin.X + map.Width * map.CellSize / 2;
                var y = map.Origin.Y + map.Height * map.CellSize / 2;
                session.Plan(formation, InputQuantizerRaw(x), InputQuantizerRaw(y));
                var frames = 0;
                while (session.Planning && frames++ < 10000) session.Tick();
                if (session.Planning) throw new InvalidOperationException("Planning benchmark exceeded frame bound.");
                var metrics = session.Metrics();
                if (!session.MatchesPlacementReference())
                    throw new InvalidOperationException("Indexed/prepared placement differs from reference.");
                if (metrics.Succeeded + metrics.Failed != count) throw new InvalidOperationException("Planning outcomes lost members.");
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session.Capture()))).ToLowerInvariant();
                if (baseline is not null && hash != baseline) throw new InvalidOperationException("Planning repeats differ.");
                baseline = hash;
                samples.Add(new(count, ((Placement.FixedFormationKind)formation).ToString(), repeat, frames,
                    metrics.Succeeded, metrics.Failed, metrics.CreateMs, metrics.PeakTickMs, metrics.PlacementMs, metrics.AllocatedBytes, hash,
                    metrics.MatchingMs, metrics.CandidateMs, metrics.ReachabilityMs,
                    metrics.MatchingPeakMs, metrics.CandidatePeakMs, metrics.MatchingInitializationMs, metrics.PreparationPeakMs,
                    OutcomeIdentity(session.ReadUnits()), metrics.PlacementPeakMs));
            }
        }
        return new { WorldIdentity = world, Scope = "planning only; no movement/traffic performance conclusion", Samples = samples };
    }
    private static string OutcomeIdentity(PreviewUnit[] units) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(units))));

    private static string InputQuantizerRaw(long value) => (value / (decimal)PrototypeScalar.Scale)
        .ToString(System.Globalization.CultureInfo.InvariantCulture);
}
