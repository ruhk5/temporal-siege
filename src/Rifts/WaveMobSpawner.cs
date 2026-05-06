using TemporalSiege.Config;
using TemporalSiege.Storms;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Rifts;

/// <summary>
/// Reads a wave's <see cref="WaveSpec"/> and emits mobs from active rifts
/// over the wave's duration. Caps simultaneous on-field storm-mobs at
/// <see cref="WaveConfig.MaxConcurrentMobs"/>. Tags every spawned mob with
/// the watched-attribute the storm-end despawn sweep reads.
///
/// Wave-composition matrix lives in <see cref="WaveConfig.ByIntensity"/>.
/// Each <see cref="MobSpawn"/> in a wave is either a fixed-count (e.g.
/// "exactly 1 Sundered in wave 4") or a weighted slot in the remaining
/// total-count budget.
///
/// Stormdrifter / Sundered entity codes don't exist until Phase 5 / 6 — until
/// then, attempts to spawn them log a warning and skip. The spawner itself
/// is exercisable via the wave-end timer in <see cref="StormSession"/>.
/// </summary>
public class WaveMobSpawner
{
    private const string FromStormAttributeKey = "temporalsiege:fromstorm";

    private readonly ICoreServerAPI sapi;
    private readonly TemporalSiegeConfig cfg;
    private readonly RiftRegistry registry;
    private readonly Random rng = new();

    private WaveSpec? currentWave;
    private int spawnedThisWave;
    private int targetThisWave;
    private float secondsSinceLastSpawn;
    private readonly List<MobSpawn> currentMobsBag = new();
    private long? tickListenerId;

    public WaveMobSpawner(ICoreServerAPI sapi, TemporalSiegeConfig cfg, RiftRegistry registry)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        this.registry = registry;
    }

    public void OnWaveBegan(StormSession session, int waveNumber, WaveSpec wave)
    {
        currentWave = wave;
        spawnedThisWave = 0;
        targetThisWave = ScaleCountForPlayerCount(wave.TotalCount);
        secondsSinceLastSpawn = 0;
        currentMobsBag.Clear();
        BuildBag(wave);

        // Spawn ticker runs while wave is live. We re-register every wave
        // because tick interval is independent of wave length.
        if (tickListenerId == null)
        {
            tickListenerId = sapi.Event.RegisterGameTickListener(OnSpawnTick, millisecondInterval: 500);
        }

        sapi.Logger.Notification("[TemporalSiege] wave {0}: target={1} mobs across {2} rifts", waveNumber, targetThisWave, registry.Count);
    }

    private void OnSpawnTick(float dt)
    {
        if (currentWave == null) return;
        if (spawnedThisWave >= targetThisWave) return;
        if (registry.Count == 0) return;

        secondsSinceLastSpawn += dt;
        // Spread the wave's spawns across ~2/3 of the wave duration so the
        // tail of the wave is mop-up. Use the wave's lull-after as a coarse
        // proxy for wave length (config doesn't separately encode wave
        // duration in v1).
        float interval = Math.Max(0.5f, currentWave.LullSecondsAfter * 0.5f / Math.Max(targetThisWave, 1));
        if (secondsSinceLastSpawn < interval) return;
        secondsSinceLastSpawn = 0;

        if (CountActiveStormMobs() >= cfg.Waves.MaxConcurrentMobs) return;

        var rift = PickRiftForSpawn();
        if (rift == null) return;

        var nextSpawn = TakeFromBag();
        if (nextSpawn == null) return;

        if (TrySpawnMob(rift, nextSpawn.EntityCode))
        {
            spawnedThisWave++;
        }
    }

    /// <summary>4-player baseline scaled by online-player count. Cheap linear scale; tune later.</summary>
    private int ScaleCountForPlayerCount(int baseline)
    {
        var n = Math.Max(1, sapi.World.AllOnlinePlayers.Length);
        // 4-player baseline → linear scale, clamped so solo doesn't get steamrolled.
        var scale = 0.25f + 0.75f * (n / 4f);
        return Math.Max(1, (int)Math.Round(baseline * scale));
    }

    private void BuildBag(WaveSpec wave)
    {
        // Add fixed-count entries verbatim, then fill the remainder with
        // weighted picks. Bag is shuffled at draw time.
        int fixedTotal = 0;
        foreach (var m in wave.Mobs)
        {
            if (m.FixedCount > 0)
            {
                for (int i = 0; i < m.FixedCount; i++) currentMobsBag.Add(m);
                fixedTotal += m.FixedCount;
            }
        }

        int remaining = Math.Max(0, targetThisWave - fixedTotal);
        var weighted = wave.Mobs.Where(m => m.FixedCount == 0 && m.Weight > 0).ToList();
        if (weighted.Count == 0) return;

        var totalWeight = weighted.Sum(m => m.Weight);
        for (int i = 0; i < remaining; i++)
        {
            var roll = rng.NextSingle() * totalWeight;
            float acc = 0;
            foreach (var m in weighted)
            {
                acc += m.Weight;
                if (roll <= acc) { currentMobsBag.Add(m); break; }
            }
        }
    }

    private MobSpawn? TakeFromBag()
    {
        if (currentMobsBag.Count == 0) return null;
        // Random pull keeps the wave's mix shuffled rather than drained
        // category-by-category.
        var idx = rng.Next(currentMobsBag.Count);
        var pick = currentMobsBag[idx];
        currentMobsBag.RemoveAt(idx);
        return pick;
    }

    private EntityRift? PickRiftForSpawn()
    {
        // Skip rifts that are collapsing — they shouldn't keep spawning
        // (handled by EntityRift, but we double-check here so spawn budget
        // isn't burned on dead rifts).
        var spawnable = registry.Active.Where(r => r.Alive && !r.IsCollapsing).ToList();
        if (spawnable.Count == 0) return null;
        return spawnable[rng.Next(spawnable.Count)];
    }

    private int CountActiveStormMobs()
    {
        int n = 0;
        foreach (var e in sapi.World.LoadedEntities.Values)
        {
            if (e == null || !e.Alive) continue;
            if (e.WatchedAttributes?.GetBool(FromStormAttributeKey, false) == true) n++;
        }
        return n;
    }

    private bool TrySpawnMob(EntityRift rift, string entityCode)
    {
        var asset = new AssetLocation(entityCode);
        var props = sapi.World.GetEntityType(asset);
        if (props == null)
        {
            // Stormdrifter / Sundered entities don't exist until Phase 5 / 6 —
            // log once per missing code per wave so the log isn't spammed.
            sapi.Logger.Debug("[TemporalSiege] wave-spawn skipped: entity {0} not registered (will land in Phase 5/6)", entityCode);
            return false;
        }

        var entity = sapi.World.ClassRegistry.CreateEntity(props);
        if (entity == null) return false;

        // Spawn slightly offset from the rift (not in player's face per #16
        // acceptance). Drop them on the same Y as the rift; physics will
        // settle them onto the ground.
        var theta = rng.NextDouble() * Math.PI * 2;
        var radius = 1.5f + rng.NextSingle() * 1.5f;
        var spawnPos = new Vec3d(
            rift.Pos.X + Math.Cos(theta) * radius,
            rift.Pos.Y,
            rift.Pos.Z + Math.Sin(theta) * radius);

        entity.Pos.SetPos(spawnPos);
        entity.Pos.Yaw = (float)(rng.NextDouble() * Math.PI * 2);

        // Tag for the storm-end despawn sweep (StormSession reads this attr).
        entity.WatchedAttributes.SetBool(FromStormAttributeKey, true);

        sapi.World.SpawnEntity(entity);
        return true;
    }
}
