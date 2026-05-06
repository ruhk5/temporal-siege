using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// Suicide-bomber attack: when within <c>contactRange</c> of the target, start
/// a fuse; when the fuse expires, deal AOE damage to entities in <c>radius</c>
/// and despawn self.
///
/// <b>No v1 user.</b> Authored per ADR-0005 so the v2 suicide-bomber drifter
/// is pure JSON. Don't drop this under schedule pressure — skipping it defeats
/// the architecture decision.
///
/// JSON parameters:
/// <list type="bullet">
///   <item>damage        — AOE damage applied to entities in radius. Default 35.</item>
///   <item>radius        — AOE radius in blocks. Default 3.</item>
///   <item>fuse          — fuse delay in seconds before detonation. Default 0.5.</item>
///   <item>contactRange  — how close the target must be for the fuse to start. Default 1.5.</item>
/// </list>
/// </summary>
public class AiTaskExplodeOnContact : AiTaskBaseTargetable
{
    private float damage;
    private float radius;
    private float fuse;
    private float contactRange;

    private float fuseRemaining;
    private bool armed;

    public AiTaskExplodeOnContact(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        damage       = taskConfig["damage"].AsFloat(35f);
        radius       = taskConfig["radius"].AsFloat(3f);
        fuse         = taskConfig["fuse"].AsFloat(0.5f);
        contactRange = taskConfig["contactRange"].AsFloat(1.5f);
    }

    public override bool ShouldExecute()
    {
        if (armed) return true;
        if (StormGate.IsClosedFor(entity)) return false;
        if (targetEntity == null || !targetEntity.Alive) return false;
        return DistanceToTargetSquared() <= contactRange * contactRange;
    }

    public override void StartExecute()
    {
        armed = true;
        fuseRemaining = fuse;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!armed) return false;
        fuseRemaining -= dt;

        if (fuseRemaining <= 0)
        {
            Detonate();
            return false;
        }

        // If the target slips away before fuse expires, keep counting down anyway —
        // the bomber has committed.
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        armed = false;
        fuseRemaining = 0;
    }

    private double DistanceToTargetSquared()
    {
        var t = targetEntity!;
        var dx = t.Pos.X - entity.Pos.X;
        var dy = t.Pos.Y - entity.Pos.Y;
        var dz = t.Pos.Z - entity.Pos.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private void Detonate()
    {
        var center = entity.Pos.XYZ;
        var radiusSq = radius * radius;

        var damageSource = new DamageSource
        {
            Source = EnumDamageSource.Entity,
            SourceEntity = entity,
            Type = EnumDamageType.Injury
        };

        var nearby = entity.World.GetEntitiesAround(
            center,
            horRange: radius,
            vertRange: radius,
            matches: e => e != entity && e.Alive
        );

        foreach (var e in nearby)
        {
            var dx = e.Pos.X - center.X;
            var dy = e.Pos.Y - center.Y;
            var dz = e.Pos.Z - center.Z;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > radiusSq) continue;

            // Linear falloff: full damage at center, zero at radius edge.
            var falloff = 1f - (float)(Math.Sqrt(distSq) / radius);
            e.ReceiveDamage(damageSource, damage * falloff);
        }

        // Visual + audio. Vanilla "burst" particle is a reasonable placeholder
        // until the v2 suicide-bomber gets art.
        entity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/blockbreak"), center.X, center.Y, center.Z, range: 32f);

        // Self-destruct.
        entity.Die(EnumDespawnReason.Death, damageSource);
    }
}
