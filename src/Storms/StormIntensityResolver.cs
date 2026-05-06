using TemporalSiege.Config;
using Vintagestory.API.Server;

namespace TemporalSiege.Storms;

/// <summary>
/// Resolves a storm's intensity tier (minor / moderate / major) at storm-start.
///
/// Per ADR-0002, the *actual* metric (tool material, days survived, base footprint,
/// composite, ...) is TBD-in-tuning. v1 ships a placeholder driven by world-elapsed
/// game days, with thresholds in <see cref="StormConfig"/>. Future intensity work
/// swaps the body of <see cref="Resolve"/> without touching callers.
/// </summary>
public class StormIntensityResolver
{
    private readonly ICoreServerAPI sapi;
    private readonly StormConfig cfg;

    public StormIntensityResolver(ICoreServerAPI sapi, StormConfig cfg)
    {
        this.sapi = sapi;
        this.cfg = cfg;
    }

    /// <summary>Returns "minor" / "moderate" / "major".</summary>
    public string Resolve()
    {
        var score = ComputeScore();
        if (score >= cfg.MajorThreshold)    return "major";
        if (score >= cfg.ModerateThreshold) return "moderate";
        return "minor";
    }

    private float ComputeScore()
    {
        // Stub metric: world-elapsed days. Cheap, deterministic, plays nicely with
        // the JSON thresholds (default 0/3/6 -> minor for week 1, moderate weeks 2-3,
        // major thereafter). Swap this body when alpha tuning picks the real metric.
        return (float)sapi.World.Calendar.TotalDays;
    }
}
