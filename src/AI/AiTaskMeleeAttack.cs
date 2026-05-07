using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// On-contact melee damage. Self-contained — does not inherit from vanilla
/// <c>Vintagestory.GameContent.AiTaskMeleeAttack</c> because vanilla's
/// ShouldExecute is gated by raycasts, hostility filters, taming generations,
/// and other checks tuned for vanilla animal AI. For storm-AI we want a
/// simpler "if player in strike range, swing" loop.
///
/// JSON parameters:
/// <list type="bullet">
///   <item>damage           per-swing damage. Default 2.</item>
///   <item>damageType       damage type. Default BluntAttack.</item>
///   <item>damageTier       damage tier. Default 0.</item>
///   <item>strikeRange      max distance (centre-to-centre) to engage. Default 4.</item>
///   <item>strikeVerRange   max vertical distance. Default 4.</item>
///   <item>cooldownMs       cooldown between swings (ms). Default 1000.</item>
///   <item>animation        attack animation code. Default standattack.</item>
///   <item>animationSpeed   anim speed. Default 1.5.</item>
///   <item>knockback        target motion impulse on hit. Default 0.0.</item>
/// </list>
///
/// Storm-gated (Phase 5.3 / #21).
/// </summary>
public class AiTaskMeleeAttack : AiTaskBaseTargetable
{
    private float damage;
    private EnumDamageType damageType;
    private int damageTier;
    private float strikeRange;
    private float strikeVerRange;
    private long cooldownMs;
    private string? animation;
    private float animationSpeed;
    private float knockback;

    private long lastSwingMs;
    private bool didStartAnim;

    public AiTaskMeleeAttack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        damage         = taskConfig["damage"].AsFloat(2f);
        damageType     = Enum.TryParse<EnumDamageType>(taskConfig["damageType"].AsString("BluntAttack"), out var dt) ? dt : EnumDamageType.BluntAttack;
        damageTier     = taskConfig["damageTier"].AsInt(0);
        strikeRange    = taskConfig["strikeRange"].AsFloat(4f);
        strikeVerRange = taskConfig["strikeVerRange"].AsFloat(4f);
        cooldownMs     = (long)taskConfig["cooldownMs"].AsInt(1000);
        animation      = taskConfig["animation"].AsString(null);
        animationSpeed = taskConfig["animationSpeed"].AsFloat(1.5f);
        knockback      = taskConfig["knockback"].AsFloat(0f);
        entity.World.Logger.Notification("[TemporalSiege] *MY* AiTaskMeleeAttack instantiated for {0} (dmg={1}, range={2})",
            entity.Code, damage, strikeRange);
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;

        var now = entity.World.ElapsedMilliseconds;
        if (now - lastSwingMs < cooldownMs) return false;

        targetEntity = FindNearestPlayer(strikeRange);
        return targetEntity != null && IsInStrikeRange(targetEntity);
    }

    public override void StartExecute()
    {
        if (animation != null)
        {
            entity.AnimManager?.StartAnimation(new AnimationMetaData
            {
                Code = animation,
                Animation = animation,
                AnimationSpeed = animationSpeed,
                BlendMode = EnumAnimationBlendMode.Average,
                EaseInSpeed = 999,
                EaseOutSpeed = 999
            });
            didStartAnim = true;
        }
        ApplyHit();
        lastSwingMs = entity.World.ElapsedMilliseconds;
    }

    public override bool ContinueExecute(float dt)
    {
        // One-shot: do the strike on Start, then end. Cooldown gate in
        // ShouldExecute handles repeat cadence.
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        if (didStartAnim && animation != null)
        {
            entity.AnimManager?.StopAnimation(animation);
            didStartAnim = false;
        }
    }

    private bool IsInStrikeRange(Entity target)
    {
        var dx = target.Pos.X - entity.Pos.X;
        var dy = target.Pos.Y - entity.Pos.Y;
        var dz = target.Pos.Z - entity.Pos.Z;
        if (Math.Abs(dy) > strikeVerRange) return false;
        return dx * dx + dz * dz <= strikeRange * strikeRange;
    }

    private void ApplyHit()
    {
        if (targetEntity == null || !targetEntity.Alive) return;
        var dmgSrc = new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            Type = damageType,
            DamageTier = damageTier,
            KnockbackStrength = knockback,
        };
        targetEntity.ReceiveDamage(dmgSrc, damage);
    }

    private Entity? FindNearestPlayer(float range)
    {
        var rangeSq = range * range;
        Entity? best = null;
        double bestDistSq = double.MaxValue;
        foreach (var p in entity.World.AllOnlinePlayers)
        {
            var pe = p?.Entity;
            if (pe == null || !pe.Alive) continue;
            var dx = pe.Pos.X - entity.Pos.X;
            var dy = pe.Pos.Y - entity.Pos.Y;
            var dz = pe.Pos.Z - entity.Pos.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > rangeSq) continue;
            if (distSq < bestDistSq) { best = pe; bestDistSq = distSq; }
        }
        return best;
    }
}
