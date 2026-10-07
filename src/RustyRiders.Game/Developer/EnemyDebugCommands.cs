using Rusty.Engine.Debugging;
using RustyRiders.Game.Enemies;
using RustyRiders.Game.Player;

namespace RustyRiders.Game.Developer;

/// <summary>Live-debug commands for the enemies and the chase.</summary>
internal sealed class EnemyDebugCommands(EnemyDirector enemies, Walker walker) : IDebugCommandModule
{
    [DebugCommand("riders.enemies.inspect", Description = "Read the chase line and each enemy's kind, role, awareness, health, place and distance from the player.")]
    public DebugCommandResult Inspect()
    {
        (string chase, string hostiles) = enemies.Hud();
        return DebugCommandResult.Success(string.Join("\n", [$"{chase} {hostiles}".Trim(), .. enemies.Describe(walker.Feet)]));
    }

    [DebugCommand("riders.dev.chase", Description = "Developer override: set the world seconds left before the chase begins (0 brings the first wave now).")]
    public DebugCommandResult Chase(float seconds)
    {
        if (!enemies.Active) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Not in a level.");
        if (!float.IsFinite(seconds) || seconds < 0) return DebugCommandResult.Failure(DebugCommandStatus.InvalidArguments, "Seconds must be 0 or more.");
        enemies.SetChase(seconds);
        return DebugCommandResult.Success($"Chase in {seconds} s.");
    }
}
