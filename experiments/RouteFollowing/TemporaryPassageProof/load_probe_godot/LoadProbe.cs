using Godot;

/// <summary>Headless acceptance entry kept separate from the user's interactive lab and its build output.</summary>
public partial class LoadProbe : Node
{
    public override void _Ready()
    {
        SetProcess(false);
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args is not ["--multi-group", var count, var folder])
                throw new ArgumentException("Expected --multi-group 300|500 <new evidence folder>");
            GetTree().Quit(MultiGroupChecks.Run(int.Parse(count), folder, line => GD.Print(line)) ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }
}
