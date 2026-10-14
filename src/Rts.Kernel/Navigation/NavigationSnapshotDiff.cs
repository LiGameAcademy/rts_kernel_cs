namespace Rts.Kernel.Navigation;

internal static class NavigationSnapshotDiff
{
    internal static SnapshotDifference? FindFirst(MatchSnapshot expected, MatchSnapshot actual)
    {
        var left = expected.Navigation;
        var right = actual.Navigation;
        if ((left is null) != (right is null)) return Difference("navigation.present", left is not null, right is not null);
        if (left is not null && right is not null)
        {
            if (left.MapHash != right.MapHash) return Difference("navigation.mapHash", left.MapHash, right.MapHash);
            if (left.HeightHash != right.HeightHash) return Difference("navigation.heightHash", left.HeightHash, right.HeightHash);
            if (left.Obstacles.Count != right.Obstacles.Count)
                return Difference("navigation.obstacles.count", left.Obstacles.Count, right.Obstacles.Count);
            for (var i = 0; i < left.Obstacles.Count; i++)
            {
                var a = left.Obstacles[i];
                var b = right.Obstacles[i];
                var prefix = $"navigation.obstacles[{i}]";
                if (a.Id != b.Id) return Difference(prefix + ".id", a.Id, b.Id);
                if (a.Area.X != b.Area.X) return Difference(prefix + ".area.x", a.Area.X, b.Area.X);
                if (a.Area.Y != b.Area.Y) return Difference(prefix + ".area.y", a.Area.Y, b.Area.Y);
                if (a.Area.Width != b.Area.Width) return Difference(prefix + ".area.width", a.Area.Width, b.Area.Width);
                if (a.Area.Height != b.Area.Height) return Difference(prefix + ".area.height", a.Area.Height, b.Area.Height);
            }
        }
        return null;
    }

    private static SnapshotDifference Difference<T>(string path, T left, T right) =>
        new(path, Convert.ToString(left, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
            Convert.ToString(right, System.Globalization.CultureInfo.InvariantCulture) ?? "null");
}
