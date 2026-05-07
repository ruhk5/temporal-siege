using TemporalSiege.Storms;
using Vintagestory.API.Common.Entities;

namespace TemporalSiege.AI;

/// <summary>
/// Storm-only AI gating (Phase 5.3 / #21).
///
/// Storm-spawned mobs (tagged with the <c>temporalsiege:fromstorm</c>
/// watched-attribute by <see cref="Rifts.WaveMobSpawner"/>) only run their
/// storm-AI tasks while a storm is active. After the storm fully ends
/// (<see cref="StormSession.Phase.Done"/>), the gate closes and these tasks
/// short-circuit so the underlying <c>EntityDrifter</c> falls back to its
/// vanilla idle/wander behaviour. CONTEXT.md § Glossary: "Outside storms,
/// drifters revert to vanilla behavior."
///
/// Subsiding is treated as still-storm — per spec, "existing mobs continue
/// current AI tasks" through the straggler-hunt phase.
///
/// Non-storm-tagged entities (e.g. a custom-spawned debug mob) are never
/// gated — the check only fires when the watched-attribute is set.
/// </summary>
internal static class StormGate
{
    private const string FromStormAttributeKey = "temporalsiege:fromstorm";

    /// <summary>
    /// Returns true if the storm-AI task should NOT run for this entity right
    /// now. Tasks call this at the top of <c>ShouldExecute</c>.
    /// </summary>
    public static bool IsClosedFor(Entity entity)
    {
        if (entity?.WatchedAttributes == null) return false;
        if (!entity.WatchedAttributes.GetBool(FromStormAttributeKey, false)) return false;

        var mod = entity.Api?.ModLoader.GetModSystem<TemporalSiegeModSystem>();
        var session = mod?.Storms?.ActiveSession;
        if (session == null) return true;
        return session.CurrentPhase == StormSession.Phase.Done;
    }
}
