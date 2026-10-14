using System.Security.Cryptography;
using System.Text.Json;

namespace NumericPrototype.Movement;

internal sealed class MovementWorld
{
    private readonly byte[] _flags;
    private readonly long[] _heights;
    internal int Width { get; }
    internal int Height { get; }
    internal long CellSize { get; }
    internal FixedPoint Origin { get; }
    internal int HeightWidth { get; }
    internal int HeightHeight { get; }
    internal long TileSize { get; }
    internal FixedPoint HeightOrigin { get; }
    internal string Identity { get; }

    internal MovementWorld(string pathingJson, string heightJson)
    {
        using var pathing = JsonDocument.Parse(pathingJson);
        using var terrain = JsonDocument.Parse(heightJson);
        var p = pathing.RootElement;
        var h = terrain.RootElement;
        Width = p.GetProperty("width").GetInt32();
        Height = p.GetProperty("height").GetInt32();
        CellSize = Number(p, "cellSize");
        Origin = Point(p.GetProperty("origin"));
        _flags = Convert.FromBase64String(p.GetProperty("cellsBase64").GetString()!);
        HeightWidth = h.GetProperty("tilepointWidth").GetInt32();
        HeightHeight = h.GetProperty("tilepointHeight").GetInt32();
        TileSize = Number(h, "tileSize");
        HeightOrigin = Point(h.GetProperty("centerOffset"));
        _heights = h.GetProperty("heights").EnumerateArray().Select(v => InputQuantizer.Parse(v.GetRawText()).Raw).ToArray();
        if (Width <= 0 || Height <= 0 || CellSize <= 0 || TileSize <= 0
            || _flags.Length != checked(Width * Height) || HeightWidth < 2 || HeightHeight < 2
            || _heights.Length != checked(HeightWidth * HeightHeight))
            throw new ArgumentException("Invalid prototype terrain dimensions/buffers.");
        // Experiment bound, not an approved engine limit. Keeps scalar/time calculations in the audited sample domain.
        if ((Int128)Width * CellSize > 16384L * FixedMath.One || (Int128)Height * CellSize > 16384L * FixedMath.One
            || Origin.X != HeightOrigin.X || Origin.Y != HeightOrigin.Y
            || (Int128)Width * CellSize != (Int128)(HeightWidth - 1) * TileSize
            || (Int128)Height * CellSize != (Int128)(HeightHeight - 1) * TileSize)
            throw new ArgumentException("Prototype requires aligned terrain spanning at most 16384 units per axis.");
        Identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { pathingJson, heightJson })))).ToLowerInvariant();
    }

    internal static long Number(JsonElement value, string property) => InputQuantizer.Parse(value.GetProperty(property).GetRawText()).Raw;
    private static FixedPoint Point(JsonElement value) => new(Number(value, "x"), Number(value, "y"));
    internal bool Walkable(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && (_flags[y * Width + x] & 2) == 0;
    internal (int X, int Y) Cell(FixedPoint point) => (Floor(point.X - Origin.X, CellSize), Floor(point.Y - Origin.Y, CellSize));
    internal FixedPoint Center(int x, int y) => new(Origin.X + x * CellSize + CellSize / 2, Origin.Y + y * CellSize + CellSize / 2);
    internal static int Floor(long numerator, long denominator)
    {
        var quotient = numerator / denominator;
        if (numerator < 0 && numerator % denominator != 0) quotient--;
        return checked((int)quotient);
    }
    internal long Sample(FixedPoint point)
    {
        var x = point.X - HeightOrigin.X;
        var y = point.Y - HeightOrigin.Y;
        if (x < 0 || y < 0 || (Int128)x > (Int128)(HeightWidth - 1) * TileSize
            || (Int128)y > (Int128)(HeightHeight - 1) * TileSize)
            throw new ArgumentOutOfRangeException(nameof(point), "Missing terrain sample is not flat ground.");
        var ix = Math.Min(Floor(x, TileSize), HeightWidth - 2);
        var iy = Math.Min(Floor(y, TileSize), HeightHeight - 2);
        var fx = FixedMath.Div(x - ix * TileSize, TileSize);
        var fy = FixedMath.Div(y - iy * TileSize, TileSize);
        var index = iy * HeightWidth + ix;
        long Lerp(long a, long b, long t) => a + FixedMath.Mul(b - a, t);
        return Lerp(Lerp(_heights[index], _heights[index + 1], fx),
            Lerp(_heights[index + HeightWidth], _heights[index + HeightWidth + 1], fx), fy);
    }
    internal IEnumerable<(long Left, long Top, long Right, long Bottom)> BlockedBoxes(FixedPoint a, FixedPoint b, long radius)
    {
        var low = Cell(new(Math.Min(a.X, b.X) - radius, Math.Min(a.Y, b.Y) - radius));
        var high = Cell(new(Math.Max(a.X, b.X) + radius, Math.Max(a.Y, b.Y) + radius));
        for (var y = Math.Max(-1, low.Y); y <= Math.Min(Height, high.Y); y++)
        for (var x = Math.Max(-1, low.X); x <= Math.Min(Width, high.X); x++)
            if (!Walkable(x, y))
                yield return (Origin.X + x * CellSize, Origin.Y + y * CellSize,
                    Origin.X + (x + 1) * CellSize, Origin.Y + (y + 1) * CellSize);
    }
}
