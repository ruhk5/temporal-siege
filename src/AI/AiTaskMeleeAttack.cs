using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using VanillaMeleeAttack = Vintagestory.GameContent.AiTaskMeleeAttack;

namespace TemporalSiege.AI;

/// <summary>
/// On-contact melee damage. Thin wrapper over vanilla
/// <c>Vintagestory.GameContent.AiTaskMeleeAttack</c>: vanilla already exposes
/// damage / knockback / armor-pierce / attackRate via JSON, which is exactly
/// what issue #6 asks for.
///
/// We register it under the <c>temporalsiege:meleeattack</c> code so entity
/// JSON references our namespace per ADR-0005, leaving a stable override slot
/// for storm-only damage scaling, special hit effects, etc. Also gates the
/// task off outside active storms (Phase 5.3 / #21) so storm-spawned mobs
/// stop attacking once the storm fully ends.
/// </summary>
public class AiTaskMeleeAttack : VanillaMeleeAttack
{
    public AiTaskMeleeAttack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig) { }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;
        return base.ShouldExecute();
    }
}
