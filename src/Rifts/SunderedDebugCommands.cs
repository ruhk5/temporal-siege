using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Rifts;

/// <summary>
/// /tssundered spawn — drops a Sundered at the caller's feet, tagged
/// <c>temporalsiege:fromstorm=true</c> so storm-AI gating treats it like a
/// storm-spawned mob (and the storm-end despawn sweep can clean it up later).
/// </summary>
public static class SunderedDebugCommands
{
    public static void Register(ICoreServerAPI sapi)
    {
        var root = sapi.ChatCommands
            .Create("tssundered")
            .WithDescription("Temporal Siege Sundered debug commands")
            .RequiresPrivilege(Privilege.controlserver);

        root.BeginSubCommand("spawn")
            .WithDescription("Spawn a Sundered at the caller's feet.")
            .HandleWith(args =>
            {
                if (args.Caller.Player is not IServerPlayer player)
                    return TextCommandResult.Error("Must be run by a player.");

                var props = sapi.World.GetEntityType(new AssetLocation("temporalsiege", "sundered"));
                if (props == null) return TextCommandResult.Error("Sundered entity type not registered.");

                var entity = sapi.World.ClassRegistry.CreateEntity(props);
                if (entity == null) return TextCommandResult.Error("Failed to instantiate Sundered.");

                var pos = player.Entity.Pos.AsBlockPos.UpCopy(1);
                entity.Pos.SetPos(new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5));
                entity.WatchedAttributes.SetBool("temporalsiege:fromstorm", true);
                sapi.World.SpawnEntity(entity);

                return TextCommandResult.Success($"Spawned Sundered at {pos} (eid {entity.EntityId}).");
            })
            .EndSubCommand();
    }
}
