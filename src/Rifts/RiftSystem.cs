using TemporalSiege.Config;
using TemporalSiege.Loot;
using TemporalSiege.Storms;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Rifts;

/// <summary>
/// Top-level rift coordinator: subscribes to <see cref="StormCoordinator"/>
/// events and drives the rift lifecycle (placement on storm-start and inter-wave,
/// mob spawning during waves, shard drops on close, storm-end collapse with
/// persistent loot pile).
///
/// Phase 4 owns this. Sub-systems:
///   - <see cref="RiftRegistry"/>     — live set of active rifts
///   - <see cref="RiftPlacementService"/> — picks valid spots and spawns
///   - <see cref="WaveMobSpawner"/>   — wave-comp matrix → per-rift spawns
///   - <see cref="PersistentLootKeeper"/> — long-lifetime loot piles at storm-end
/// </summary>
public class RiftSystem
{
    private const float StormEndCollapseSeconds = 30f;

    private readonly ICoreServerAPI sapi;
    private readonly TemporalSiegeConfig cfg;

    public RiftRegistry Registry { get; }
    public RiftPlacementService Placement { get; }
    public WaveMobSpawner WaveSpawner { get; }
    public PersistentLootKeeper LootKeeper { get; }

    public RiftSystem(ICoreServerAPI sapi, TemporalSiegeConfig cfg, StormCoordinator storms)
    {
        this.sapi = sapi;
        this.cfg = cfg;

        Registry = new RiftRegistry();
        LootKeeper = new PersistentLootKeeper(sapi);
        Placement = new RiftPlacementService(sapi, cfg.Rifts, Registry);
        WaveSpawner = new WaveMobSpawner(sapi, cfg, Registry);

        Registry.OnRiftAdded += AttachRiftHandlers;

        storms.OnStormBegan      += OnStormBegan;
        storms.OnWaveBegan       += (s, n, w) => WaveSpawner.OnWaveBegan(s, n, w);
        storms.OnWaveEnded       += OnWaveEnded;
        storms.OnStormSubsiding  += OnStormSubsiding;
        storms.OnStormFullyEnded += OnStormFullyEnded;

        // Sweep dead rifts that despawned without going through our close paths
        // (e.g. forced removal, chunk unload). Cheap — only iterates the active
        // set, which is small.
        sapi.Event.RegisterGameTickListener(_ => SweepDeadRifts(), millisecondInterval: 1000);
    }

    private void AttachRiftHandlers(EntityRift rift)
    {
        rift.OnPlayerClosed      += OnRiftClosedByPlayer;
        rift.OnStormEndCollapsed += OnRiftCollapsedAtStormEnd;
    }

    private void OnStormBegan(StormSession session)
    {
        Registry.Clear();
        // No-beacon fallback per ADR-0004: place around each player.
        // Beacon-anchored placement lands in #29 (Phase 7.5).
        Placement.SpawnInitialRifts();
    }

    private void OnWaveEnded(StormSession session, int waveNumber)
    {
        // Fresh rifts open during the lull after each non-final wave. After the
        // final wave the storm enters subsiding, so we don't add more pressure.
        if (session.Schedule == null || waveNumber >= session.Schedule.Waves.Count) return;
        Placement.SpawnFreshRifts();
    }

    private void OnStormSubsiding(StormSession session)
    {
        // Begin the 30s collapse on every still-living rift. BeginStormEndCollapse
        // is idempotent — rifts already in collapse ignore the call.
        foreach (var r in Registry.Active.ToArray())
        {
            r.BeginStormEndCollapse(StormEndCollapseSeconds);
        }
    }

    private void OnStormFullyEnded(StormSession session)
    {
        // Belt-and-braces: if any rift survived the collapse window, force it
        // off. (Should not happen — collapse is 30s, straggler phase is 5min.)
        foreach (var r in Registry.Active.ToArray())
        {
            r.Die(EnumDespawnReason.Removed, null);
        }
        Registry.Clear();
    }

    private void OnRiftClosedByPlayer(EntityRift rift)
    {
        DropClosedRiftLoot(rift);
        Registry.Remove(rift);
    }

    private void OnRiftCollapsedAtStormEnd(EntityRift rift)
    {
        DropStormEndLoot(rift);
        Registry.Remove(rift);
    }

    private void DropClosedRiftLoot(EntityRift rift)
    {
        var rng = new Random();
        int shardCount = rng.Next(3, 6); // [3, 5] inclusive
        // 10% chance of a single bonus shard (placeholder until Phase 8 introduces a real bonus item table).
        if (rng.NextSingle() < 0.10f) shardCount++;
        DropShards(rift.Pos.XYZ, shardCount, persistent: false);
        sapi.Logger.Notification("[TemporalSiege] rift closed by player at {0}, {1} shards dropped", rift.Pos.AsBlockPos, shardCount);
    }

    private void DropStormEndLoot(EntityRift rift)
    {
        var rng = new Random();
        int shardCount = rng.Next(1, 3); // [1, 2] inclusive
        DropShards(rift.Pos.XYZ, shardCount, persistent: true);
        sapi.Logger.Notification("[TemporalSiege] rift collapsed at {0}, {1} persistent shards dropped", rift.Pos.AsBlockPos, shardCount);
    }

    private void DropShards(Vec3d pos, int count, bool persistent)
    {
        var item = sapi.World.GetItem(new AssetLocation("temporalsiege", "temporalshard"));
        if (item == null)
        {
            sapi.Logger.Warning("[TemporalSiege] cannot drop shards — temporalsiege:temporalshard item not registered");
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var stack = new ItemStack(item, 1);
            var dropPos = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
            var velocity = new Vec3d((Random.Shared.NextDouble() - 0.5) * 0.2, 0.2, (Random.Shared.NextDouble() - 0.5) * 0.2);
            var entity = sapi.World.SpawnItemEntity(stack, dropPos, velocity);
            if (persistent && entity != null)
            {
                LootKeeper.Track(entity, cfg.Rifts.StormEndLootLifetimeGameHours);
            }
        }
    }

    private void SweepDeadRifts()
    {
        foreach (var r in Registry.Active.ToArray())
        {
            if (r == null || !r.Alive)
            {
                Registry.Remove(r!);
            }
        }
    }
}
