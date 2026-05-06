using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Rifts;

/// <summary>
/// /tsrift spawn|spawnring|list|close|wipe — controlserver-only.
///
/// /tsrift spawn      — spawn a rift at the caller's feet (skips ring sampling so the rift lands somewhere visible).
/// /tsrift spawnring  — sample a valid spot in the ring around the caller and spawn there.
/// /tsrift list       — print active rift positions and HP.
/// /tsrift close [n]  — kill the n-th active rift (default 1) — exercises the close-by-player path.
/// /tsrift collapse   — begin storm-end-style collapse on every active rift (exercises the persistent loot path).
/// /tsrift wipe       — force-despawn every active rift without running close/collapse handlers.
/// </summary>
public static class RiftDebugCommands
{
    public static void Register(ICoreServerAPI sapi, RiftSystem rifts)
    {
        var root = sapi.ChatCommands
            .Create("tsrift")
            .WithDescription("Temporal Siege rift debug commands")
            .RequiresPrivilege(Privilege.controlserver);

        root.BeginSubCommand("spawn")
            .WithDescription("Spawn a rift at the caller's feet (no ring sampling).")
            .HandleWith(args =>
            {
                if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Must be run by a player.");
                var pos = player.Entity.Pos.AsBlockPos.UpCopy(1);
                var rift = rifts.Placement.TrySpawnRiftAt(pos);
                return rift != null
                    ? TextCommandResult.Success($"Spawned rift at {pos}")
                    : TextCommandResult.Error($"Position {pos} failed validity check (need ≥{sapi.ModLoader.GetModSystem<TemporalSiegeModSystem>().Config.Rifts.MinAirBlocksAbove} air above natural terrain).");
            })
            .EndSubCommand();

        root.BeginSubCommand("spawnring")
            .WithDescription("Sample the configured ring around the caller and spawn a rift at a valid spot.")
            .HandleWith(args =>
            {
                if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Must be run by a player.");
                var anchor = player.Entity.Pos.AsBlockPos;
                return rifts.Placement.TrySpawnRiftNear(anchor)
                    ? TextCommandResult.Success("Spawned rift in ring around caller.")
                    : TextCommandResult.Error("No valid ring position found after 24 attempts.");
            })
            .EndSubCommand();

        root.BeginSubCommand("list")
            .WithDescription("List active rifts.")
            .HandleWith(args =>
            {
                if (rifts.Registry.Count == 0) return TextCommandResult.Success("No active rifts.");
                var lines = rifts.Registry.Active.Select((r, i) =>
                {
                    var hp = r.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>();
                    var hpStr = hp != null ? $"{hp.Health:F0}/{hp.MaxHealth:F0}" : "?";
                    return $"  [{i + 1}] pos={r.Pos.AsBlockPos} hp={hpStr} collapsing={r.IsCollapsing}";
                });
                return TextCommandResult.Success($"Active rifts ({rifts.Registry.Count}):\n{string.Join('\n', lines)}");
            })
            .EndSubCommand();

        root.BeginSubCommand("close")
            .WithDescription("Damage the n-th active rift to zero HP (exercises close-by-player path).")
            .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("index", 1))
            .HandleWith(args =>
            {
                if (rifts.Registry.Count == 0) return TextCommandResult.Error("No active rifts.");
                int idx = (int)args[0] - 1;
                var list = rifts.Registry.Active.ToList();
                if (idx < 0 || idx >= list.Count) return TextCommandResult.Error($"Index out of range — have {list.Count} rifts.");
                var rift = list[idx];
                var hp = rift.GetBehavior<Vintagestory.GameContent.EntityBehaviorHealth>();
                if (hp == null) return TextCommandResult.Error("Rift has no health behavior.");
                rift.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Player, Type = EnumDamageType.BluntAttack }, hp.Health + 1);
                return TextCommandResult.Success($"Closed rift {idx + 1}.");
            })
            .EndSubCommand();

        root.BeginSubCommand("collapse")
            .WithDescription("Begin storm-end collapse on every active rift.")
            .HandleWith(args =>
            {
                if (rifts.Registry.Count == 0) return TextCommandResult.Error("No active rifts.");
                int n = 0;
                foreach (var r in rifts.Registry.Active.ToArray())
                {
                    r.BeginStormEndCollapse(30f);
                    n++;
                }
                return TextCommandResult.Success($"Collapsing {n} rifts over 30s.");
            })
            .EndSubCommand();

        root.BeginSubCommand("wipe")
            .WithDescription("Force-despawn every active rift without running close/collapse handlers.")
            .HandleWith(args =>
            {
                int n = 0;
                foreach (var r in rifts.Registry.Active.ToArray())
                {
                    r.Die(EnumDespawnReason.Removed, null);
                    n++;
                }
                rifts.Registry.Clear();
                return TextCommandResult.Success($"Wiped {n} rifts.");
            })
            .EndSubCommand();
    }
}
