using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace TemporalSiege.Storms;

/// <summary>
/// /tsstorm begin [intensity] | /tsstorm end | /tsstorm status
/// Server-side, controlserver privilege.
/// </summary>
public static class StormDebugCommands
{
    public static void Register(ICoreServerAPI sapi, StormCoordinator coordinator)
    {
        var root = sapi.ChatCommands
            .Create("tsstorm")
            .WithDescription("Temporal Siege storm debug controls")
            .RequiresPrivilege(Privilege.controlserver);

        root.BeginSubCommand("begin")
            .WithDescription("Force-begin a storm. Optional intensity: minor|moderate|major")
            .WithArgs(sapi.ChatCommands.Parsers.OptionalWord("intensity"))
            .HandleWith(args =>
            {
                var intensity = (string?)args[0];
                if (intensity != null && intensity != "minor" && intensity != "moderate" && intensity != "major")
                    return TextCommandResult.Error("intensity must be minor / moderate / major");
                coordinator.ForceBeginStorm(intensity);
                return TextCommandResult.Success($"Storm forced to begin (intensity={intensity ?? "auto"}).");
            })
            .EndSubCommand();

        root.BeginSubCommand("end")
            .WithDescription("Force the active storm into the subsiding path.")
            .HandleWith(args =>
            {
                if (coordinator.ActiveSession == null) return TextCommandResult.Error("No active storm.");
                coordinator.ForceEndStorm();
                return TextCommandResult.Success("Storm forced into subsiding.");
            })
            .EndSubCommand();

        root.BeginSubCommand("status")
            .WithDescription("Print active storm state.")
            .HandleWith(args =>
            {
                var s = coordinator.ActiveSession;
                if (s == null) return TextCommandResult.Success("No active storm.");
                return TextCommandResult.Success(
                    $"Storm: intensity={s.Intensity}  phase={s.CurrentPhase}  wave={s.CurrentWaveIndex + 1}/{s.Schedule?.Waves.Count ?? 0}  phaseTimeLeft={s.PhaseTimeRemaining:F1}s");
            })
            .EndSubCommand();
    }
}
