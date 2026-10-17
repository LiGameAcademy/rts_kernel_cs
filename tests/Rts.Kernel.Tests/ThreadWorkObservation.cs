using System.ComponentModel;
using System.Runtime.InteropServices;

/// <summary>Test-only Windows thread execution counters; never used to schedule simulation work.</summary>
internal readonly record struct ThreadWorkObservation(long CpuTicks, ulong Cycles, uint ThreadId)
{
    internal static ThreadWorkObservation? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var thread = GetCurrentThread(); // Pseudo-handle owned by Windows; must not be closed.
        if (!GetThreadTimes(thread, out _, out _, out var kernelTicks, out var userTicks))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetThreadTimes failed in frame observer.");
        if (!QueryThreadCycleTime(thread, out var cycles))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "QueryThreadCycleTime failed in frame observer.");
        return new(checked(kernelTicks + userTicks), cycles, GetCurrentThreadId());
    }

    internal static ThreadWorkObservation? Between(ThreadWorkObservation? before, ThreadWorkObservation? after)
    {
        if (before is null || after is null) return null;
        if (before.Value.ThreadId != after.Value.ThreadId)
            throw new InvalidOperationException("Frame observer crossed native threads.");
        return new(checked(after.Value.CpuTicks - before.Value.CpuTicks),
            checked(after.Value.Cycles - before.Value.Cycles), before.Value.ThreadId);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit,
        out long kernel, out long user);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
}
