using System.Security.Cryptography;
using System.Text.Json;

namespace Rts.Kernel.Navigation;

/// <summary>Frozen movement capabilities supplied by content at match creation.</summary>
public sealed record MovementDefinition(ulong Id, SimScalar Speed, SimScalar Radius, MotionParameters? Motion = null)
{
    internal bool IsValid => Id > 0 && SimulationDomain.IsCapability(Speed)
        && SimulationDomain.IsCapability(Radius) && (Motion is null || Motion.IsValid);
}

internal sealed class MovementDefinitions
{
    private readonly SortedDictionary<ulong, MovementDefinition> _items = [];
    internal string? ContentHash { get; }
    internal MovementDefinitions(IReadOnlyList<MovementDefinition>? definitions)
    {
        foreach (var definition in definitions ?? [])
            if (definition is null || !definition.IsValid || !_items.TryAdd(definition.Id, definition))
                throw new ArgumentException("Movement definitions must have unique IDs and finite positive capabilities.");
        if (_items.Count > 0)
            ContentHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(_items.Values)))
                .ToLowerInvariant();
    }
    internal bool TryGet(ulong id, out MovementDefinition? definition) => _items.TryGetValue(id, out definition);

    /// <summary>Chebyshev clearance cells from body radius and grid cell size (same rule as host PathQuery).</summary>
    internal static int ClearanceCells(MovementDefinition definition, SimScalar cellSize)
    {
        var numerator = (Int128)definition.Radius.Raw * 2 - cellSize.Raw;
        if (numerator <= 0) return 0;
        var denominator = (Int128)cellSize.Raw * 2;
        return (int)Int128.Min(int.MaxValue, (numerator + denominator - 1) / denominator);
    }
}
