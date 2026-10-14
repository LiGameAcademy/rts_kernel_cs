using System.Text.Json;

namespace NumericPrototype;

internal static class PlanningPreviewChecks
{
    internal static object Run(string assetsDirectory)
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        foreach (var count in new[] { 65, 300, 500 })
        {
            var session = new PlanningPreviewSession(assetsDirectory, count);
            var map = session.ReadMap();
            string Number(long raw) => (raw / (decimal)PrototypeScalar.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
            session.Plan(0, Number(map.Origin.X + map.Width * map.CellSize / 2), Number(map.Origin.Y + map.Height * map.CellSize / 2));
            session.Tick();
            Check(session.Cancel(1), "pending member cancellation accepted");
            var snapshot = session.Capture();
            var clone = new PlanningPreviewSession(assetsDirectory, count);
            clone.Restore(snapshot);
            while (session.Planning)
            {
                session.Tick();
                clone.Tick();
                Check(session.Capture() == clone.Capture(), "preview restore preserves every subsequent frame");
            }
            Check(session.Metrics().Succeeded + session.Metrics().Failed == count - 1,
                "canceled member excluded from new placement");
            Check(session.ReadUnits().Single(unit => unit.Id == 1).Canceled, "cancellation remains visible after commit");
            var completed = session.Capture();
            clone.Restore(completed);
            Check(clone.Capture() == completed, "completed preview snapshot restores");
            var corrupt = completed.Replace("\"Format\":1", "\"Format\":2");
            try { session.Restore(corrupt); Check(false, "invalid preview format accepted"); }
            catch (InvalidDataException) { Check(true, "invalid preview rejected"); }
            Check(session.Capture() == completed, "failed restore is atomic");
        }
        return new { Checks = checks, Result = "planning_preview_restore_pass" };
    }
}
