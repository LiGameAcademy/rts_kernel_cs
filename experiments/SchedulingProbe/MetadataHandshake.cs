using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>Bounded stdin handshake, confined to the disposable metadata calibration process.</summary>
internal static class MetadataHandshake
{
    internal static void Ready()
    {
        using var process = Process.GetCurrentProcess();
        var module = process.Modules.Cast<ProcessModule>().Single(m => m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
        var settings = new[] { "DOTNET_EnableDiagnostics", "COMPlus_EnableDiagnostics", "DOTNET_EnableEventLog",
            "COMPlus_EnableEventLog", "DOTNET_ETWEnabled", "COMPlus_ETWEnabled", "DOTNET_TieredCompilation", "DOTNET_TieredPGO" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        Console.WriteLine("metadata_ready=" + JsonSerializer.Serialize(new {
            process_id = Environment.ProcessId, native_thread_id = GetCurrentThreadId(),
            runtime = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            coreclr_path = module.FileName, coreclr_version = module.FileVersionInfo.FileVersion, settings
        }));
        Console.Out.Flush();
        WaitFor("run");
    }

    internal static void ReleaseAfterSave()
    {
        Console.Out.Flush();
        WaitFor("release");
        Console.WriteLine("metadata_released=true");
    }

    private static void WaitFor(string expected)
    {
        var command = Task.Run(Console.ReadLine).WaitAsync(TimeSpan.FromSeconds(120)).GetAwaiter().GetResult();
        if (!string.Equals(command, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"Expected calibration handshake '{expected}', received '{command}'.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
}
