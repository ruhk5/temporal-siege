using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// Beeline-with-windup at <see cref="AiTaskBaseTargetable.targetEntity"/>.
/// Sundered's signature attack but generic.
///
/// State machine:
/// <list type="number">
///   <item>Windup — freeze in place for <c>windupTime</c> seconds, locking the
///         charge direction at the end of the windup.</item>
///   <item>Charge — drive WalkVector along the locked direction with
///         <c>MovespeedMultiplier = chargeSpeed</c> until contact, timeout, or
///         loss of target.</item>
/// </list>
///
/// Contact applies a knockback impulse to the target's motion vector and ends
/// the charge. Damage is delegated to a paired AiTaskMeleeAttack.
///
/// JSON parameters:
/// <list type="bullet">
///   <item>windupTime         seconds locked in place before charging. Default 0.6.</item>
///   <item>chargeSpeed        movespeed multiplier during charge. Default 1.8.</item>
///   <item>maxChargeDuration  safety cap on the charge phase, seconds. Default 2.5.</item>
///   <item>knockback          impulse magnitude applied to the target on contact. Default 0.4.</item>
///   <item>contactRange       distance to target that ends the charge. Default 1.4.</item>
/// </list>
/// </summary>
public class AiTaskChargeAtTarget : AiTaskBaseTargetable
{
    private float windupTime;
    private float chargeSpeed;
    private float maxChargeDuration;
    private float knockback;
    private float contactRange;

    private enum Phase { Windup, Charging }
    private Phase phase;
    private float windupRemaining;
    private float chargeRemaining;
    private Vec3d chargeDir = new(0, 0, 0);

    public AiTaskChargeAtTarget(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        windupTime        = taskConfig["windupTime"].AsFloat(0.6f);
        chargeSpeed       = taskConfig["chargeSpeed"].AsFloat(1.8f);
        maxChargeDuration = taskConfig["maxChargeDuration"].AsFloat(2.5f);
        knockback         = taskConfig["knockback"].AsFloat(0.4f);
        contactRange      = taskConfig["contactRange"].AsFloat(1.4f);
    }

    public override bool ShouldExecute()
    {
        return targetEntity != null && targetEntity.Alive;
    }

    public override void StartExecute()
    {
        phase = Phase.Windup;
        windupRemaining = windupTime;
        entity.Controls.WalkVector.Set(0, 0, 0);
        entity.Controls.Forward = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null || !targetEntity.Alive) return false;

        if (phase == Phase.Windup)
        {
            windupRemaining -= dt;
            if (windupRemaining > 0) return true;

            // Lock the charge direction at windup-end. If we're effectively on
            // top of the target already, abort.
            var dx = targetEntity.Pos.X - entity.Pos.X;
            var dy = targetEntity.Pos.Y - entity.Pos.Y;
            var dz = targetEntity.Pos.Z - entity.Pos.Z;
            var len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 0.001) return false;
            chargeDir.Set(dx / len, dy / len, dz / len);
            phase = Phase.Charging;
            chargeRemaining = maxChargeDuration;
            return true;
        }

        // Phase.Charging
        chargeRemaining -= dt;
        if (chargeRemaining <= 0) return false;

        entity.Controls.WalkVector.Set(chargeDir.X, 0, chargeDir.Z);
        entity.Controls.Forward = true;
        entity.Controls.Sprint = true;
        entity.Controls.MovespeedMultiplier = chargeSpeed;

        var ndx = targetEntity.Pos.X - entity.Pos.X;
        var ndy = targetEntity.Pos.Y - entity.Pos.Y;
        var ndz = targetEntity.Pos.Z - entity.Pos.Z;
        if (ndx * ndx + ndy * ndy + ndz * ndz <= contactRange * contactRange)
        {
            ApplyKnockback();
            return false;
        }
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        entity.Controls.Forward = false;
        entity.Controls.Sprint = false;
        entity.Controls.MovespeedMultiplier = 1f;
        phase = Phase.Windup;
    }

    private void ApplyKnockback()
    {
        if (targetEntity == null) return;
        var motion = targetEntity.Pos.Motion;
        motion.X += chargeDir.X * knockback;
        motion.Y += knockback * 0.3f;
        motion.Z += chargeDir.Z * knockback;
    }
}
