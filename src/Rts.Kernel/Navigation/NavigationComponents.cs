namespace Rts.Kernel.Navigation;

/// <summary>Per-command derived reachability, including clearance and corner rules; never match authority.</summary>
internal sealed class NavigationComponents(NavigationState navigation)
{
    private readonly Dictionary<int, int[]> _byClearance = [];
    internal bool Connected(GridCell from, GridCell to, int clearance)
    {
        var grid = navigation.Grid;
        if (from.X < 0 || from.Y < 0 || from.X >= grid.Width || from.Y >= grid.Height
            || to.X < 0 || to.Y < 0 || to.X >= grid.Width || to.Y >= grid.Height) return false;
        if (!_byClearance.TryGetValue(clearance, out var labels))
        {
            labels = Build(clearance);
            _byClearance.Add(clearance, labels);
        }
        var component = labels[from.Y * grid.Width + from.X];
        return component > 0 && component == labels[to.Y * grid.Width + to.X];
    }
    private int[] Build(int clearance)
    {
        var grid = navigation.Grid;
        var labels = new int[checked(grid.Width * grid.Height)];
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
            if (!navigation.CanOccupy(new GridCell(x, y), clearance)) labels[y * grid.Width + x] = -1;
        var pending = new Queue<GridCell>();
        var component = 0;
        for (var y = 0; y < grid.Height; y++)
        for (var x = 0; x < grid.Width; x++)
        {
            var index = y * grid.Width + x;
            if (labels[index] != 0) continue;
            labels[index] = ++component;
            pending.Enqueue(new GridCell(x, y));
            while (pending.TryDequeue(out var cell))
            {
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cell.X + dx;
                    var ny = cell.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= grid.Width || ny >= grid.Height) continue;
                    var nextIndex = ny * grid.Width + nx;
                    if (labels[nextIndex] != 0 || (dx != 0 && dy != 0
                        && (labels[cell.Y * grid.Width + nx] < 0 || labels[ny * grid.Width + cell.X] < 0))) continue;
                    labels[nextIndex] = component;
                    pending.Enqueue(new GridCell(nx, ny));
                }
            }
        }
        return labels;
    }
}
