using System.Text.Json;
using Rts.Kernel;
using Rts.Kernel.Navigation;

internal static class PlacementOrderReadChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var orders = new UnitOrderBook();
        var id = new EntityId(1);
        var radius = SimScalar.FromRatio(5, 2);
        var prefix = new PlacementBody(new(-1, -1), radius);
        UnitOrderIntent Move(int x) => new(UnitOrderKind.Move, OrderSource.Player, new(new(x, 7), 30));
        MoveOrder Path(UnitOrderIntent intent) => new(intent.Move!, [new(0, 7), intent.Move!.Goal]);

        void Read(params int[] expected)
        {
            var beforeQueues = JsonSerializer.Serialize(orders.ReadQueues());
            var beforePaths = JsonSerializer.Serialize(orders.ReadPaths());
            var bodies = new List<PlacementBody> { prefix };
            orders.AppendRetainedBodies(id, radius, bodies);
            check(bodies.SequenceEqual(new[] { prefix }.Concat(expected.Select(x => new PlacementBody(new(x, 7), radius)))),
                "placement read appends current then pending goals, preserving duplicates and radius");
            check(beforeQueues == JsonSerializer.Serialize(orders.ReadQueues())
                && beforePaths == JsonSerializer.Serialize(orders.ReadPaths()), "placement read cannot consume or mutate orders");
        }

        Read();
        var first = Move(10);
        orders.ReplaceMove(id, first, Path(first));
        orders.TryAppend(id, Move(20));
        orders.TryAppend(id, Move(20));
        orders.TryAppend(id, Move(30));
        Read(10, 20, 20, 30);
        orders.Complete(id);
        Read(20, 20, 30); // A completed current order does not erase queued goals.
        var replacement = Move(40);
        orders.ReplaceMove(id, replacement, Path(replacement));
        Read(40);
        orders.Stop(id, OrderSource.Player);
        Read();
        orders.Clear(id);
        Read();
    }
}
