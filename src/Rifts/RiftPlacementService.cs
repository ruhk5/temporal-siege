using TemporalSiege.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Rifts;

/// <summary>
/// Picks valid spawn positions for rifts and spawns the entities.
///
/// Validity rules (per CONTEXT.md § Spawn (rifts)):
///   - In a configurable ring radius around an anchor point (player or beacon).
///   - On natural terrain — block at chosen Y is solid, block above is air.
///   - At least <c>RiftConfig.MinAirBlocksAbove</c> air blocks clear above
///     so the pillar isn't buried (rejects underground/underwater spawns).
///
/// Two entry points:
///   - <see cref="SpawnInitialRifts"/>: storm-start, places 3–5 rifts around
///     each online player (no-beacon fallback per ADR-0004).
///   - <see cref="SpawnFreshRifts"/>: between waves, places 1–2 fresh rifts
///     at a different compass arc to keep pressure rotating.
///
/// Beacon-anchored placement (ADR-0004) is wired in Phase 7.5 (#29).
/// </summary>
public class RiftPlacementService
{
    private readonly ICoreServerAPI sapi;
    private readonly RiftConfig cfg;
    private readonly RiftRegistry registry;
    private readonly Random rng = new();

    public RiftPlacementService(ICoreServerAPI sapi, RiftConfig cfg, RiftRegistry registry)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        this.registry = registry;
    }

    /// <summary>
    /// Place 3–5 rifts (per <see cref="RiftConfig"/>) in a ring around each
    /// online player. Rifts that fail validation are silently skipped — we
    /// take the best-effort placement and log a warning if we end up with zero.
    /// </summary>
    public void SpawnInitialRifts()
    {
        var players = sapi.World.AllOnlinePlayers;
        if (players.Length == 0)
        {
            sapi.Logger.Notification("[TemporalSiege] no players online, skipping rift placement");
            return;
        }

        int placed = 0;
        foreach (var p in players)
        {
            if (p?.Entity == null) continue;
            int target = rng.Next(cfg.MinRiftsPerStorm, cfg.MaxRiftsPerStorm + 1);
            for (int i = 0; i < target; i++)
            {
                if (TrySpawnRiftNear(p.Entity.Pos.AsBlockPos)) placed++;
            }
        }

        sapi.Logger.Notification("[TemporalSiege] initial rifts placed: {0} (across {1} players)", placed, players.Length);
    }

    /// <summary>
    /// Place 1–2 fresh rifts (per <see cref="RiftConfig"/>) per online player.
    /// Called at the start of each between-wave lull.
    /// </summary>
    public void SpawnFreshRifts()
    {
        var players = sapi.World.AllOnlinePlayers;
        if (players.Length == 0) return;

        int placed = 0;
        foreach (var p in players)
        {
            if (p?.Entity == null) continue;
            int target = rng.Next(cfg.FreshRiftsPerLullMin, cfg.FreshRiftsPerLullMax + 1);
            for (int i = 0; i < target; i++)
            {
                if (TrySpawnRiftNear(p.Entity.Pos.AsBlockPos)) placed++;
            }
        }
        sapi.Logger.Notification("[TemporalSiege] fresh rifts placed: {0}", placed);
    }

    /// <summary>
    /// Spawn a rift at <paramref name="exactPos"/> unconditionally. Used by the
    /// /tsrift spawn debug command — validity checks belong to ring placement,
    /// not "spawn here for testing".
    /// </summary>
    public EntityRift? SpawnRiftAtUnchecked(BlockPos exactPos) => SpawnRiftAt(exactPos);

    /// <summary>
    /// Spawn a rift at <paramref name="exactPos"/> only if the position passes
    /// the air-above / natural-terrain validity check.
    /// </summary>
    public EntityRift? TrySpawnRiftAt(BlockPos exactPos)
    {
        if (!IsValidPosition(exactPos)) return null;
        return SpawnRiftAt(exactPos);
    }

    /// <summary>
    /// Try to place a single rift in the ring around <paramref name="anchor"/>.
    /// </summary>
    public bool TrySpawnRiftNear(BlockPos anchor, int maxAttempts = 24)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = SampleRingPosition(anchor);
            if (candidate == null) continue;
            if (!IsValidPosition(candidate)) continue;
            SpawnRiftAt(candidate);
            return true;
        }
        return false;
    }

    private BlockPos? SampleRingPosition(BlockPos anchor)
    {
        // Sample a point in the configured ring annulus, then walk down/up to
        // find the surface. The ring is XZ — we ignore Y when sampling.
        var theta = rng.NextDouble() * Math.PI * 2;
        var radius = cfg.MinRingRadius + rng.NextSingle() * (cfg.MaxRingRadius - cfg.MinRingRadius);
        int dx = (int)Math.Round(Math.Cos(theta) * radius);
        int dz = (int)Math.Round(Math.Sin(theta) * radius);

        int x = anchor.X + dx;
        int z = anchor.Z + dz;

        // Find the topmost solid block in this column (server only — fast on
        // loaded chunks). The world's RainMapHeightAt reads the precomputed
        // surface height index.
        int surfaceY = sapi.World.BlockAccessor.GetRainMapHeightAt(x, z);
        if (surfaceY <= 0) return null;

        // Stand the rift on the block above the surface.
        return new BlockPos(x, surfaceY + 1, z, 0);
    }

    private bool IsValidPosition(BlockPos pos)
    {
        var ba = sapi.World.BlockAccessor;

        // Block below must be solid (natural terrain). Block at pos must be air.
        var belowPos = new BlockPos(pos.X, pos.Y - 1, pos.Z, 0);
        var below = ba.GetBlock(belowPos);
        if (below == null || below.Id == 0) return false;
        // Reject liquids (lava/water) under the rift to keep "natural terrain".
        if (below.IsLiquid()) return false;

        var here = ba.GetBlock(pos);
        if (here != null && here.Id != 0)
        {
            // Allow placement if the block is replaceable (tall grass, snow layer etc).
            if (here.Replaceable < 6000) return false;
        }

        // Require N air blocks above to keep the pillar visible.
        var scan = new BlockPos(pos.X, pos.Y, pos.Z, 0);
        for (int dy = 0; dy < cfg.MinAirBlocksAbove; dy++)
        {
            scan.Y = pos.Y + dy;
            var b = ba.GetBlock(scan);
            if (b != null && b.Id != 0 && b.Replaceable < 6000) return false;
        }
        return true;
    }

    private EntityRift SpawnRiftAt(BlockPos pos)
    {
        var props = sapi.World.GetEntityType(new AssetLocation("temporalsiege", "rift"));
        if (props == null)
        {
            sapi.Logger.Error("[TemporalSiege] entity temporalsiege:rift not registered — rift placement aborted");
            return null!;
        }

        var entity = sapi.World.ClassRegistry.CreateEntity(props);
        if (entity is not EntityRift rift)
        {
            sapi.Logger.Error("[TemporalSiege] entity temporalsiege:rift resolved to {0}, expected EntityRift", entity?.GetType().FullName ?? "null");
            return null!;
        }

        var spawnPos = new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5);
        rift.Pos.SetPos(spawnPos);
        rift.Pos.Yaw = (float)(rng.NextDouble() * Math.PI * 2);
        rift.PositionBeforeFalling.Set(spawnPos);

        sapi.World.SpawnEntity(rift);
        registry.Add(rift);
        sapi.Logger.Notification("[TemporalSiege]   rift spawned at ({0}, {1}, {2})", pos.X, pos.Y, pos.Z);
        return rift;
    }
}
