using System.Runtime.InteropServices;

/// <summary>Optional measurement only; never consulted by simulation rules.</summary>
internal static class RecoveryClock
{
    internal static double? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return (kernel + user) / 10000.0;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(nint thread, out long creation, out long exit, out long kernel, out long user);
}
