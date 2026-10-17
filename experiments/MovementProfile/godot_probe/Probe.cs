using Godot;
using System.Runtime.InteropServices;

public partial class Probe : Node
{
    public override void _Ready()
    {
        var checks = 0;
        try
        {
            Console.WriteLine($"godot_movement_runtime engine={Engine.GetVersionInfo()["string"]} runtime={RuntimeInformation.FrameworkDescription}");
            MultiPartyCloseoutChecks.Run((condition, message) =>
            {
                checks++;
                if (!condition) throw new InvalidOperationException(message);
            });
            Console.WriteLine($"Godot multi-party PASS ({checks} checks)");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
