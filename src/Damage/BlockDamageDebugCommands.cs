using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Damage;

/// <summary>
/// /tsdamage commands for poking the <see cref="BlockDamageStore"/> from chat.
/// Server-side only; require controlserver privilege.
///
/// /tsdamage stress [count]   - apply 1 dmg to <c>count</c> nearby blocks and time it.
/// /tsdamage count            - report current entry count.
/// /tsdamage clear            - clear entries inside a 32-block sphere around the caller.
/// </summary>
public static class BlockDamageDebugCommands
{
    public static void Register(ICoreServerAPI sapi, BlockDamageStore store)
    {
        var root = sapi.ChatCommands
            .Create("tsdamage")
            .WithDescription("Temporal Siege block-damage debug commands")
            .RequiresPrivilege(Privilege.controlserver);

        root.BeginSubCommand("stress")
            .WithDescription("Apply 1 damage to <count> blocks centered on the caller and report timings.")
            .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("count", 1000))
            .HandleWith(args => Stress(args, sapi, store))
            .EndSubCommand();

        root.BeginSubCommand("count")
            .WithDescription("Report damaged-block count across loaded chunks.")
            .HandleWith(args => TextCommandResult.Success($"Damaged blocks: {store.DamagedBlockCount}"))
            .EndSubCommand();

        root.BeginSubCommand("clear")
            .WithDescription("Clear all damage entries in a 32-block radius around the caller.")
            .HandleWith(args => Clear(args, store))
            .EndSubCommand();
    }

    private static TextCommandResult Stress(TextCommandCallingArgs args, ICoreServerAPI sapi, BlockDamageStore store)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be run by a player.");

        int count = (int)args[0];
        if (count <= 0) return TextCommandResult.Error("count must be > 0");

        var center = player.Entity.Pos.AsBlockPos;
        var positions = new List<BlockPos>(count);

        // Scan an outward-spiralling box around the player and pick the first <count>
        // damageable blocks we find.
        int radius = (int)Math.Cbrt(count) + 4;
        for (int dy = -radius; dy <= radius && positions.Count < count; dy++)
        for (int dx = -radius; dx <= radius && positions.Count < count; dx++)
        for (int dz = -radius; dz <= radius && positions.Count < count; dz++)
        {
            var pos = new BlockPos(center.X + dx, center.Y + dy, center.Z + dz, 0);
            var block = sapi.World.BlockAccessor.GetBlock(pos);
            if (block != null && block.Id != 0 && block.Resistance > 0) positions.Add(pos);
        }

        if (positions.Count == 0)
            return TextCommandResult.Error("No damageable blocks found near caller.");

        var sw = Stopwatch.StartNew();
        int destroyed = 0;
        foreach (var pos in positions)
        {
            if (store.ApplyDamage(pos, 1f)) destroyed++;
        }
        sw.Stop();

        var msg = $"Stress test: {positions.Count} ApplyDamage calls in {sw.Elapsed.TotalMilliseconds:F2} ms ({sw.Elapsed.TotalMilliseconds / Math.Max(positions.Count, 1):F4} ms/call). {destroyed} blocks destroyed. {store.DamagedBlockCount} entries live.";
        sapi.Logger.Notification("[TemporalSiege] {0}", msg);
        return TextCommandResult.Success(msg);
    }

    private static TextCommandResult Clear(TextCommandCallingArgs args, BlockDamageStore store)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be run by a player.");

        var center = player.Entity.Pos.AsBlockPos;
        const int r = 32;
        const int rSquared = r * r;
        int cleared = 0;

        var snapshot = new List<BlockPos>();
        foreach (var kv in store.EnumerateDamaged())
        {
            var dx = kv.Key.X - center.X;
            var dy = kv.Key.Y - center.Y;
            var dz = kv.Key.Z - center.Z;
            if (dx * dx + dy * dy + dz * dz <= rSquared) snapshot.Add(kv.Key);
        }
        foreach (var pos in snapshot)
        {
            store.Clear(pos);
            cleared++;
        }

        return TextCommandResult.Success($"Cleared {cleared} damage entries within {r} blocks.");
    }
}
