using TemporalSiege.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalSiege.Storms;

/// <summary>
/// Top-level lifecycle for storm events. Polls VS's <see cref="SystemTemporalStability"/>
/// for edge transitions of <c>StormData.nowStormActive</c> (vanilla doesn't expose
/// an event), resolves intensity at storm-start, and drives a <see cref="StormSession"/>
/// across waves and storm-end.
///
/// Other systems (rifts, mob spawner, HUD) subscribe to the public events here
/// rather than touching VS internals.
/// </summary>
public class StormCoordinator
{
    private readonly ICoreServerAPI sapi;
    private readonly TemporalSiegeConfig cfg;
    private readonly StormIntensityResolver intensityResolver;

    private SystemTemporalStability? vsStorms;
    private bool wasStormActive;

    public StormSession? ActiveSession { get; private set; }

    public event Action<StormSession>?              OnStormBegan;
    public event Action<StormSession>?              OnStormSubsiding;
    public event Action<StormSession>?              OnStormFullyEnded;
    public event Action<StormSession, int, WaveSpec>? OnWaveBegan;
    public event Action<StormSession, int>?         OnWaveEnded;

    public StormCoordinator(ICoreServerAPI sapi, TemporalSiegeConfig cfg)
    {
        this.sapi = sapi;
        this.cfg = cfg;
        intensityResolver = new StormIntensityResolver(sapi, cfg.Storm);
        sapi.Event.RegisterGameTickListener(OnTick, millisecondInterval: 1000);
    }

    /// <summary>Force-start a storm session ignoring VS state. For /tsstorm debugging.</summary>
    public void ForceBeginStorm(string? intensity = null)
    {
        if (ActiveSession != null && ActiveSession.CurrentPhase != StormSession.Phase.Done) return;
        BeginSession(intensity ?? intensityResolver.Resolve());
    }

    /// <summary>Force the active session into the subsiding path.</summary>
    public void ForceEndStorm()
    {
        ActiveSession?.OnStormForcedToEnd();
    }

    private void OnTick(float dt)
    {
        if (vsStorms == null)
        {
            vsStorms = sapi.ModLoader.GetModSystem<SystemTemporalStability>();
            // Vanilla survival mod always loads, but if for some reason it isn't
            // there (config / mod conflict) just keep polling — debug commands
            // still work.
        }

        var nowActive = vsStorms?.StormData?.nowStormActive ?? false;
        if (nowActive && !wasStormActive)
        {
            BeginSession(intensityResolver.Resolve());
        }
        else if (!nowActive && wasStormActive)
        {
            ActiveSession?.OnStormForcedToEnd();
        }
        wasStormActive = nowActive;

        ActiveSession?.Tick(dt);
    }

    private void BeginSession(string intensity)
    {
        ActiveSession = new StormSession(sapi, cfg, this, intensity);
        sapi.Logger.Notification("[TemporalSiege] storm begins, intensity={0}", intensity);
        OnStormBegan?.Invoke(ActiveSession);
    }

    // Internal hooks called from StormSession state transitions.
    internal void RaiseWaveBegan(StormSession session, int waveNumber, WaveSpec wave)
    {
        sapi.Logger.Notification("[TemporalSiege] wave {0}/{1} begins ({2} mobs target)", waveNumber, session.Schedule?.Waves.Count ?? 0, wave.TotalCount);
        OnWaveBegan?.Invoke(session, waveNumber, wave);
    }

    internal void RaiseWaveEnded(StormSession session, int waveNumber)
    {
        sapi.Logger.Notification("[TemporalSiege] wave {0} ends", waveNumber);
        OnWaveEnded?.Invoke(session, waveNumber);
    }

    internal void RaiseStormSubsiding(StormSession session)
    {
        sapi.Logger.Notification("[TemporalSiege] storm subsiding (intensity {0}, {1} waves cleared)", session.Intensity, session.CurrentWaveIndex + 1);
        OnStormSubsiding?.Invoke(session);
    }

    internal void RaiseStormFullyEnded(StormSession session)
    {
        sapi.Logger.Notification("[TemporalSiege] storm fully ended");
        OnStormFullyEnded?.Invoke(session);
        ActiveSession = null;
    }
}
