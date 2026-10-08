namespace Rts.Kernel.Navigation;

/// <summary>Dense Hungarian assignment; bounded to 499 followers by the public formation contract.</summary>
internal static class SlotMatching
{
    internal static int[] Assign(IReadOnlyList<SimVector2> positions, IReadOnlyList<SimVector2> slots, int anchor)
    {
        var members = Enumerable.Range(0, positions.Count).Where(i => i != anchor).ToArray();
        var n = members.Length;
        var costs = new double[n, n];
        foreach (var p in positions.Concat(slots))
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) throw new ArgumentException("Nonfinite match point.");
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            var dx = positions[members[i]].X - slots[j + 1].X;
            var dy = positions[members[i]].Y - slots[j + 1].Y;
            costs[i, j] = dx * dx + dy * dy;
            if (!double.IsFinite(costs[i, j])) throw new ArgumentException("Assignment cost overflow.");
        }
        var u = new double[n + 1];
        var v = new double[n + 1];
        var occupant = new int[n + 1];
        var previous = new int[n + 1];
        for (var row = 1; row <= n; row++)
        {
            occupant[0] = row;
            var column = 0;
            var minimum = new double[n + 1];
            Array.Fill(minimum, double.PositiveInfinity);
            var used = new bool[n + 1];
            do
            {
                used[column] = true;
                var nextColumn = 0;
                var delta = double.PositiveInfinity;
                var activeRow = occupant[column];
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var reduced = costs[activeRow - 1, j - 1] - u[activeRow] - v[j];
                    if (reduced < minimum[j]) { minimum[j] = reduced; previous[j] = column; }
                    if (minimum[j] < delta) { delta = minimum[j]; nextColumn = j; }
                }
                if (!double.IsFinite(delta)) throw new ArgumentException("Assignment arithmetic overflow.");
                for (var j = 0; j <= n; j++)
                {
                    if (used[j]) { u[occupant[j]] += delta; v[j] -= delta; }
                    else minimum[j] -= delta;
                }
                column = nextColumn;
            } while (occupant[column] != 0);
            do
            {
                var next = previous[column];
                occupant[column] = occupant[next];
                column = next;
            } while (column != 0);
        }
        var result = new int[positions.Count];
        for (var j = 1; j <= n; j++) result[members[occupant[j] - 1]] = j;
        return result;
    }
}
