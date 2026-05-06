using TemporalSiege.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace TemporalSiege.Storms;

/// <summary>
/// Per-storm state machine. Drives the wave loop while the storm is active and
/// runs the storm-end soft cutoff (subsiding announcement, no new spawns,
/// straggler phase, despawn sweep).
///
/// Other systems (rift placement, mob spawner) subscribe to events on
/// <see cref="StormCoordinator"/>; this class is the source of truth for
/// "which wave / phase are we in?" while the storm is live.
/// </summary>
public class StormSession
{
    public enum Phase
    {
        /// <summary>Between waves. <see cref="PhaseTimeRemaining"/> ticks down to zero, then we advance to a wave.</summary>
        Lull,
        /// <summary>Wave is live; mobs spawning. <see cref="PhaseTimeRemaining"/> isn't load-bearing here — wave end is event-driven (final mob killed / cap hit).</summary>
        Wave,
        /// <summary>Final wave ended. "Storm subsiding" announced. No new spawns. Straggler timer counting down.</summary>
        Subsiding,
        /// <summary>Despawn sweep already executed; storm-end is fully done.</summary>
        Done,
    }

    private readonly ICoreServerAPI sapi;
    private readonly TemporalSiegeConfig cfg;
    private readonly StormCoordinator coordinator;

    public string Intensity { get; }
    public IntensityWaves? Schedule { get; }
    public int CurrentWaveIndex { get; private set; } = -1;
    public Phase CurrentPhase { get; private set; } = Phase.Lull;
    public float PhaseTimeRemaining { get; private set; }

    public StormSession(ICoreServerAPI sapi, TemporalSiegeConfig cfg, StormCoordinator coordinator, string intensity)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        this.coordinator = coordinator;
        Intensity = intensity;
        cfg.Waves.ByIntensity.TryGetValue(intensity, out var waves);
        Schedule = waves;

        // Start with a brief opening lull so players have a moment after the
        // warning fires. Use the lull duration of an imaginary "wave 0".
        PhaseTimeRemaining = 5f;
        CurrentPhase = Phase.Lull;
    }

    public void Tick(float dt)
    {
        if (CurrentPhase == Phase.Done) return;

        PhaseTimeRemaining -= dt;
        if (PhaseTimeRemaining > 0) return;

        switch (CurrentPhase)
        {
            case Phase.Lull:      AdvanceToNextWaveOrFinish(); break;
            case Phase.Wave:      EndCurrentWave();             break;
            case Phase.Subsiding: ExecuteDespawnSweep();        break;
        }
    }

    /// <summary>
    /// Called by <see cref="StormCoordinator"/> when VS's native temporal-storm
    /// edge-transitions to inactive. Forces us into the subsiding path even if
    /// we're still mid-wave.
    /// </summary>
    public void OnStormForcedToEnd()
    {
        if (CurrentPhase == Phase.Subsiding || CurrentPhase == Phase.Done) return;
        EnterSubsiding();
    }

    /// <summary>
    /// Skip the straggler-cleanup wait and fully end the storm right now.
    /// Used by /tsstorm begin when the previous session is already subsiding —
    /// for debug we don't want a 5-minute dead window before the next storm
    /// can fire.
    /// </summary>
    public void FastForwardToDone()
    {
        if (CurrentPhase == Phase.Done) return;
        if (CurrentPhase != Phase.Subsiding) EnterSubsiding();
        ExecuteDespawnSweep();
    }

    private void AdvanceToNextWaveOrFinish()
    {
        if (Schedule == null || CurrentWaveIndex + 1 >= Schedule.Waves.Count)
        {
            // Out of waves. Begin the storm-end soft cutoff.
            EnterSubsiding();
            return;
        }

        CurrentWaveIndex++;
        var wave = Schedule.Waves[CurrentWaveIndex];
        CurrentPhase = Phase.Wave;
        // Wave length is open-ended: rifts spawn the wave's mobs, lull begins
        // when wave's quota is exhausted (event-driven from Phase 4 spawner).
        // For Phase 3's standalone behaviour, use a placeholder wave duration so
        // the state machine still advances without a spawner.
        PhaseTimeRemaining = wave.LullSecondsAfter > 0 ? wave.LullSecondsAfter : 30f;

        coordinator.RaiseWaveBegan(this, CurrentWaveIndex + 1, wave);
    }

    private void EndCurrentWave()
    {
        if (Schedule == null) { EnterSubsiding(); return; }
        var wave = Schedule.Waves[CurrentWaveIndex];
        coordinator.RaiseWaveEnded(this, CurrentWaveIndex + 1);

        if (CurrentWaveIndex + 1 >= Schedule.Waves.Count)
        {
            EnterSubsiding();
            return;
        }

        CurrentPhase = Phase.Lull;
        PhaseTimeRemaining = wave.LullSecondsAfter;
    }

    private void EnterSubsiding()
    {
        CurrentPhase = Phase.Subsiding;
        PhaseTimeRemaining = cfg.Storm.StragglerPhaseMinutes * 60f;
        coordinator.RaiseStormSubsiding(this);
    }

    private void ExecuteDespawnSweep()
    {
        var radius = cfg.Storm.StragglerDespawnDistanceBlocks;
        var radiusSq = radius * radius;
        int despawned = 0;

        // Find every entity tagged as ours that's >radius from any online player.
        // Spawning code (Phase 4+) will tag mobs via the WatchedAttribute below.
        var players = sapi.World.AllOnlinePlayers;
        foreach (var entity in sapi.World.LoadedEntities.Values.ToArray())
        {
            if (entity == null || !entity.Alive) continue;
            if (entity.WatchedAttributes?.GetBool("temporalsiege:fromstorm", false) != true) continue;

            bool nearAnyPlayer = false;
            foreach (var p in players)
            {
                var pe = p?.Entity;
                if (pe == null) continue;
                var dx = pe.Pos.X - entity.Pos.X;
                var dy = pe.Pos.Y - entity.Pos.Y;
                var dz = pe.Pos.Z - entity.Pos.Z;
                if (dx * dx + dy * dy + dz * dz <= radiusSq) { nearAnyPlayer = true; break; }
            }
            if (nearAnyPlayer) continue;

            entity.Die(EnumDespawnReason.Removed);
            despawned++;
        }

        sapi.Logger.Notification("[TemporalSiege] despawn sweep complete, despawned {0} stragglers", despawned);

        CurrentPhase = Phase.Done;
        PhaseTimeRemaining = 0;
        coordinator.RaiseStormFullyEnded(this);
    }
}
