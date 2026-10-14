using Godot;
using NumericPrototype;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Globalization;

public partial class NumericProbe : Node
{
    public override void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            if (arguments.Length < 2 || arguments[0] != "--output" || !Path.IsPathFullyQualified(arguments[1]))
                throw new ArgumentException("Expected --output with an absolute report filename, then optional probe arguments.");
            var culture = CultureInfo.CurrentCulture.Name;
            var uiCulture = CultureInfo.CurrentUICulture.Name;
            var report = PrototypeRunner.Run(arguments.Skip(2).ToArray());
            if (CultureInfo.CurrentCulture.Name != culture || CultureInfo.CurrentUICulture.Name != uiCulture)
                throw new InvalidOperationException("Probe changed host culture.");
            File.WriteAllText(arguments[1], report);
            // Godot dynamically loads managed bytes, so Assembly.Location may be empty.
            var corePath = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/NumericPrototype.Core.dll");
            var coreHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corePath))).ToLowerInvariant();
            var moduleId = typeof(PrototypeRunner).Assembly.ManifestModule.ModuleVersionId;
            GD.Print("NUMERIC_PROBE_PASS runtime=" + RuntimeInformation.FrameworkDescription
                + " core_sha256=" + coreHash + " core_mvid=" + moduleId.ToString("D"));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
