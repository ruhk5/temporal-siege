using TemporalSiege.Config;
using Vintagestory.API.Common;
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

    /// <summary>
    /// Force-start a storm session ignoring VS state. For /tsstorm debugging.
    /// Returns null on success; otherwise a human-readable reason the storm
    /// could not start (e.g. another storm is mid-wave).
    /// </summary>
    public string? ForceBeginStorm(string? intensity = null)
    {
        if (ActiveSession != null && ActiveSession.CurrentPhase != StormSession.Phase.Done)
        {
            // Already-subsiding session: skip the 5-minute straggler wait so
            // the next debug storm can fire immediately.
            if (ActiveSession.CurrentPhase == StormSession.Phase.Subsiding)
            {
                ActiveSession.FastForwardToDone();
            }
            else
            {
                return $"Storm already active (phase={ActiveSession.CurrentPhase}). Run /tsstorm end first.";
            }
        }
        BeginSession(intensity ?? intensityResolver.Resolve());
        return null;
    }

    /// <summary>Force the active session into the subsiding path.</summary>
    public void ForceEndStorm()
    {
        ActiveSession?.OnStormForcedToEnd();
    }

    /// <summary>
    /// Skip the straggler wait and finish the active session immediately. For
    /// /tsstorm finish — useful while testing because v1 doesn't have real mobs
    /// to hunt during the straggler phase, so it's just dead time.
    /// </summary>
    public bool ForceFinishStorm()
    {
        if (ActiveSession == null || ActiveSession.CurrentPhase == StormSession.Phase.Done) return false;
        ActiveSession.FastForwardToDone();
        return true;
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
        var totalWaves = ActiveSession.Schedule?.Waves.Count ?? 0;
        Announce($"storm begins (intensity={intensity}, {totalWaves} waves)");
        OnStormBegan?.Invoke(ActiveSession);
    }

    // Internal hooks called from StormSession state transitions.
    internal void RaiseWaveBegan(StormSession session, int waveNumber, WaveSpec wave)
    {
        var totalWaves = session.Schedule?.Waves.Count ?? 0;
        Announce($"wave {waveNumber}/{totalWaves} begins (target={wave.TotalCount} mobs)");
        OnWaveBegan?.Invoke(session, waveNumber, wave);
    }

    internal void RaiseWaveEnded(StormSession session, int waveNumber)
    {
        var nextLull = (session.Schedule != null && waveNumber - 1 < session.Schedule.Waves.Count)
            ? session.Schedule.Waves[waveNumber - 1].LullSecondsAfter : 0f;
        var lullStr = nextLull > 0 ? $"lull {nextLull:F0}s" : "final wave";
        Announce($"wave {waveNumber} ends ({lullStr})");
        OnWaveEnded?.Invoke(session, waveNumber);
    }

    internal void RaiseStormSubsiding(StormSession session)
    {
        var stragglerMin = cfg.Storm.StragglerPhaseMinutes;
        Announce($"storm subsiding ({session.CurrentWaveIndex + 1} waves cleared) — rifts collapse in 30s, storm fully ends after {stragglerMin:F0}min straggler-hunt phase");
        OnStormSubsiding?.Invoke(session);
    }

    internal void RaiseStormFullyEnded(StormSession session)
    {
        Announce("storm fully ended");
        OnStormFullyEnded?.Invoke(session);
        ActiveSession = null;
    }

    /// <summary>Send to server log AND broadcast to all players' chat with a [Storm] prefix.</summary>
    private void Announce(string msg)
    {
        sapi.Logger.Notification("[TemporalSiege] {0}", msg);
        sapi.BroadcastMessageToAllGroups($"[Storm] {msg}", EnumChatType.Notification, null);
    }
}
