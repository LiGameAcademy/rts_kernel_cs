using System.Buffers.Binary;

internal static class ProviderSnapshotChecks
{
    internal static void Run()
    {
        // A provider enabled by another session cannot establish our recording's state.
        var instances = new[] { new ProviderInstance(42, 0, [new(true, 5, 7, 0, 0x20098, 0)]) };
        var sessions = new[] { new ProviderSession("owned", 8, 1, 1024, 20, 0) };
        if (ProviderSnapshot.IsEnabled(instances, sessions)) throw new InvalidOperationException("Foreign session accepted.");
        sessions = [sessions[0] with { LoggerId = 7 }];
        if (!ProviderSnapshot.IsEnabled(instances, sessions)) throw new InvalidOperationException("Owned CLR enable rejected.");
        if (ProviderSnapshot.IsEnabled([instances[0] with { Flags = 2 }], sessions))
            throw new InvalidOperationException("Pre-enabled GUID treated as a live registration.");
        if (ProviderSnapshot.IsEnabled([new(42, 0, [new(true, 4, 7, 0, 0x20098, 0)])], sessions)
            || ProviderSnapshot.IsEnabled([new(42, 0, [new(true, 5, 7, 0, 0x8, 0)])], sessions))
            throw new InvalidOperationException("Insufficient level/keywords accepted.");
        var bytes = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), 1);
        bytes[28] = 5;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30), 7);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), 0x20098);
        var parsed = ProviderSnapshot.Parse(bytes);
        if (parsed.Length != 1 || parsed[0].ProcessId != 42 || !ProviderSnapshot.IsEnabled(parsed, sessions))
            throw new InvalidOperationException("Native registration layout decoded incorrectly.");
        foreach (var broken in new[] { bytes[..7], bytes[..55], Mutate(bytes, 8, 4), Mutate(bytes, 12, 2) })
        {
            try { ProviderSnapshot.Parse(broken); }
            catch (InvalidDataException) { continue; }
            throw new InvalidOperationException("Malformed native provider buffer accepted.");
        }
        Console.WriteLine("Provider snapshot self-checks PASS: native layout, truncated data, foreign session, PRE_ENABLE and keyword/level gates.");
    }

    private static byte[] Mutate(byte[] source, int offset, uint value)
    {
        var copy = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }
}
