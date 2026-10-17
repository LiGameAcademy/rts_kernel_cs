using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Session;

/// <summary>Read-only Windows ETW registration and enable-state snapshot; never attaches a controller.</summary>
internal static class ProviderSnapshot
{
    internal static readonly Guid RuntimeProvider = new("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4");
    internal static readonly Guid RundownProvider = new("a669021c-c450-4609-a035-5af59af4df18");

    internal static bool Write(int pid, string sessionToken, string output)
    {
        if (File.Exists(output)) throw new IOException("Refusing to overwrite provider evidence.");
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("This diagnostic uses Windows x64 ETW layouts.");
        using var process = Process.GetProcessById(pid);
        var sessions = TraceEventSession.GetActiveSessionNames().Where(n => n.Contains(sessionToken, StringComparison.Ordinal))
            .Select(ReadSession).ToArray();
        if (sessions.Length == 0) throw new InvalidDataException("No live sessions match the owned capture token.");
        var runtime = ReadInstances(RuntimeProvider).Where(i => i.ProcessId == pid).ToArray();
        var rundown = ReadInstances(RundownProvider).Where(i => i.ProcessId == pid).ToArray();
        var registered = runtime.Any(i => (i.Flags & 2) == 0); // PRE_ENABLE is not a live registration.
        var enabled = IsEnabled(runtime, sessions);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            process_id = pid, process_image = process.MainModule?.FileName, captured_utc = DateTime.UtcNow,
            qpc = Stopwatch.GetTimestamp(), session_token = sessionToken, sessions,
            runtime_registered = registered, runtime_enabled_for_owned_session = enabled,
            runtime_instances = runtime, rundown_instances = rundown
        }, new JsonSerializerOptions { WriteIndented = true }));
        return registered && enabled;
    }

    internal static bool IsEnabled(ProviderInstance[] instances, ProviderSession[] sessions) =>
        instances.Where(i => (i.Flags & 2) == 0).SelectMany(i => i.Enables).Any(e =>
            e.IsEnabled && sessions.Any(s => s.LoggerId == e.LoggerId)
            && (e.Level == 0 || e.Level >= 5) && (e.MatchAnyKeyword & 0x18) == 0x18 && e.MatchAllKeyword == 0);

    private static ProviderInstance[] ReadInstances(Guid provider)
    {
        uint required = 0;
        var result = EnumerateTraceGuidsEx(1, ref provider, 16, IntPtr.Zero, 0, out required);
        if (result == 4201) return []; // ERROR_WMI_GUID_NOT_FOUND: no active registration/pre-enable.
        if (result != 122) throw new Win32Exception((int)result);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (required < 8 || required > 16 * 1024 * 1024) throw new InvalidDataException("Invalid ETW query size.");
            var buffer = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                result = EnumerateTraceGuidsEx(1, ref provider, 16, buffer, required, out var used);
                if (result == 122) { required = used; continue; }
                if (result != 0) throw new Win32Exception((int)result);
                var bytes = new byte[used];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                return Parse(bytes);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("ETW registration list kept changing during the bounded query.");
    }

    internal static ProviderInstance[] Parse(byte[] bytes)
    {
        uint Read32(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
        ushort Read16(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        ulong Read64(int offset) => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));
        if (bytes.Length < 8) throw new InvalidDataException("Missing TRACE_GUID_INFO.");
        var count = Read32(0);
        var instances = new List<ProviderInstance>();
        var offset = 8;
        for (uint instance = 0; instance < count; instance++)
        {
            if (offset > bytes.Length - 16) throw new InvalidDataException("Truncated provider instance.");
            var next = Read32(offset);
            var enables = Read32(offset + 4);
            var pid = Read32(offset + 8);
            var flags = Read32(offset + 12);
            if (enables > (bytes.Length - offset - 16) / 32) throw new InvalidDataException("Truncated enable info.");
            var entries = new List<ProviderEnable>();
            for (var index = 0; index < enables; index++)
            {
                var at = checked(offset + 16 + index * 32);
                entries.Add(new(Read32(at) != 0, bytes[at + 4], Read16(at + 6), Read32(at + 8), Read64(at + 16), Read64(at + 24)));
            }
            instances.Add(new(pid, flags, entries.ToArray()));
            if (instance + 1 == count)
            {
                if (next != 0) throw new InvalidDataException("Last provider instance has a successor.");
            }
            else
            {
                if (next < 16 + enables * 32 || next > bytes.Length - offset)
                    throw new InvalidDataException("Invalid provider successor offset.");
                offset = checked(offset + (int)next);
            }
        }
        return instances.ToArray();
    }

    private static ProviderSession ReadSession(string name)
    {
        // EVENT_TRACE_PROPERTIES is 120 bytes on Windows x64, followed by the two name buffers.
        var buffer = Marshal.AllocHGlobal(4216);
        try
        {
            Marshal.Copy(new byte[4216], 0, buffer, 4216);
            Marshal.WriteInt32(buffer, 0, 4216);
            Marshal.WriteInt32(buffer, 112, 120);
            Marshal.WriteInt32(buffer, 116, 2168);
            var result = ControlTraceW(0, name, buffer, 0); // QUERY only; never START/STOP/UPDATE.
            if (result != 0) throw new Win32Exception((int)result);
            return new(name, checked((ushort)Marshal.ReadInt64(buffer, 8)), Marshal.ReadInt32(buffer, 64),
                Marshal.ReadInt32(buffer, 48), Marshal.ReadInt32(buffer, 80), Marshal.ReadInt32(buffer, 88));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern uint EnumerateTraceGuidsEx(int infoClass, ref Guid input, uint inputSize,
        IntPtr output, uint outputSize, out uint returnLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint controlCode);
}

internal sealed record ProviderInstance(uint ProcessId, uint Flags, ProviderEnable[] Enables);
internal sealed record ProviderEnable(bool IsEnabled, byte Level, ushort LoggerId, uint EnableProperty,
    ulong MatchAnyKeyword, ulong MatchAllKeyword);
internal sealed record ProviderSession(string Name, ushort LoggerId, int LogFileMode, int BufferSizeKb,
    int NumberOfBuffers, int EventsLost);
