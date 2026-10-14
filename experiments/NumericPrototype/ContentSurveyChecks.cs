using System.Security.Cryptography;
using System.Text.Json;

namespace NumericPrototype;

internal static class ContentSurveyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("rts-numeric-survey-");
        try
        {
            void Write(string relative, string content)
            {
                var path = Path.Combine(directory.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            Write("slk-exported/Units/UnitData.json", """
                {"records":[{"turnRate":0.1,"moveFloor":0,"moveHeight":325},{"turnRate":"-","moveHeight":0}]}
                """);
            Write("slk-exported/Units/UnitBalance.json", """
                {"records":[{"spd":405,"collision":196},{"spd":"-","collision":8}]}
                """);
            Write("map-parsed/echoisles/units.json", """
                {"units":[{"position":{"x":-6816,"y":0.1,"z":574.25}}]}
                """);
            Write("map-parsed/echoisles/pathing.json", """
                {"width":512,"height":384,"cellSize":32,"origin":{"x":-8192,"y":-6144},"cellsBase64":"ignored"}
                """);
            Write("map-parsed/echoisles/terrain-heightfield.json", """
                {"heights":[226.25,722.5]}
                """);
            var reports = ContentRangeSurvey.Run(directory.FullName);
            var rate = reports[0].Fields.Single(f => f.Field == "turnRate");
            check(rate.Count == 1 && rate.Skipped == 1, "survey distinguishes missing/non-numeric from zero");
            var height = reports[4].Fields.Single();
            check(height.Minimum == "226.25" && height.Maximum == "722.5", "survey range is based on selected values");
            check(height.MaximumAbsoluteError == 0, "binary-exact exported heights survive quantization");
            var input = File.ReadAllBytes(Path.Combine(directory.FullName, reports[0].Source));
            check(reports[0].Sha256 == Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), "survey pins actual input bytes");
            Write("slk-exported/Units/UnitBalance.json", """{"records":[{"spd":2147483648,"collision":8}]}""");
            try
            {
                ContentRangeSurvey.Run(directory.FullName);
                check(false, "out-of-range content must abort the survey");
            }
            catch (OverflowException) { check(true, "numeric overflow is not silently skipped"); }
        }
        finally { directory.Delete(recursive: true); } // Exclusively owned directory created above.
    }
}
