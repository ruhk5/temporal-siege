using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using VanillaMeleeAttack = Vintagestory.GameContent.AiTaskMeleeAttack;

namespace TemporalSiege.AI;

/// <summary>
/// Storm-gated melee task. Thin wrapper around vanilla
/// <c>Vintagestory.GameContent.AiTaskMeleeAttack</c> that disables itself
/// outside an active storm (Phase 5.3 / #21). Reserved for future custom mobs
/// that should only swing during storms — stormdrifter itself uses vanilla
/// <c>meleeattack</c> directly so it follows the spec's "outside storms,
/// drifters revert to vanilla behavior".
///
/// Registered as <c>temporalsiege:meleeattack</c>. ADR-0005.
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
