using Godot;

public partial class CombatProbe : Node
{
    public override void _Ready()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        try
        {
            var args = OS.GetCmdlineUserArgs();
            var profile = Array.IndexOf(args, "--combat-profile");
            if (profile >= 0) CombatProfileChecks.Run(Check, int.Parse(args[profile + 1]));
            else
            {
                CombatRulesChecks.Run(Check);
                CombatExecutionChecks.Run(Check);
            }
            GD.Print($"COMBAT_PROBE_DONE checks={checks}");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GD.Print("COMBAT_PROBE_FAILED");
            GetTree().Quit(1);
        }
    }
}
