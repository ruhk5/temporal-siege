using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// Targeting task: maintains <see cref="AiTaskBaseTargetable.targetEntity"/>
/// pointed at the nearest online player within <c>range</c>. Doesn't move the
/// entity — pairs with motion/attack tasks downstream that read targetEntity.
///
/// JSON parameters (per ADR-0005):
/// <list type="bullet">
///   <item>range            — detection range in blocks. Default 24.</item>
///   <item>switchThreshold  — anti-flicker margin in blocks. Default 4.</item>
///   <item>scanIntervalMs   — how often to re-pick the target. Default 500.</item>
/// </list>
///
/// Beacon awareness is the design intent (per ADR-0005 / issue #5) but the
/// beacon system doesn't exist until Phase 7. The parameter hook will be added
/// when #25-29 land; v1 for now is straight nearest-player.
/// </summary>
public class AiTaskTargetNearestPlayer : AiTaskBaseTargetable
{
    private float range;
    private float switchThreshold;
    private float scanIntervalMs;
    private long lastScanMs;

    public AiTaskTargetNearestPlayer(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        range           = taskConfig["range"].AsFloat(24f);
        switchThreshold = taskConfig["switchThreshold"].AsFloat(4f);
        scanIntervalMs  = taskConfig["scanIntervalMs"].AsFloat(500f);
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;

        var now = entity.World.ElapsedMilliseconds;
        if (now - lastScanMs < scanIntervalMs)
            return IsTargetValid(targetEntity);

        lastScanMs = now;
        PickTarget();
        return targetEntity != null;
    }

    public override void StartExecute() { /* PickTarget already set targetEntity */ }

    public override bool ContinueExecute(float dt)
    {
        var now = entity.World.ElapsedMilliseconds;
        if (now - lastScanMs >= scanIntervalMs)
        {
            lastScanMs = now;
            PickTarget();
        }
        return IsTargetValid(targetEntity);
    }

    public override void FinishExecute(bool cancelled)
    {
        // Leave targetEntity set so other composed tasks (melee, path-attack)
        // can still read it. They each re-validate on their own ShouldExecute.
    }

    private bool IsTargetValid(Entity? t)
    {
        if (t == null || !t.Alive) return false;
        var dx = t.Pos.X - entity.Pos.X;
        var dy = t.Pos.Y - entity.Pos.Y;
        var dz = t.Pos.Z - entity.Pos.Z;
        return dx * dx + dy * dy + dz * dz <= range * range;
    }

    private void PickTarget()
    {
        var myX = entity.Pos.X;
        var myY = entity.Pos.Y;
        var myZ = entity.Pos.Z;
        var rangeSq = range * range;

        Entity? best = null;
        double bestDistSq = double.MaxValue;

        foreach (var p in entity.World.AllOnlinePlayers)
        {
            var pe = p?.Entity;
            if (pe == null || !pe.Alive) continue;
            var dx = pe.Pos.X - myX;
            var dy = pe.Pos.Y - myY;
            var dz = pe.Pos.Z - myZ;
            var distSq = dx * dx + dy * dy + dz * dz;
            if (distSq > rangeSq) continue;
            if (distSq < bestDistSq) { best = pe; bestDistSq = distSq; }
        }

        // Anti-flicker: only switch if the candidate is meaningfully closer than
        // the current target.
        if (targetEntity != null && targetEntity.Alive && best != null && best != targetEntity)
        {
            var dx = targetEntity.Pos.X - myX;
            var dy = targetEntity.Pos.Y - myY;
            var dz = targetEntity.Pos.Z - myZ;
            var curDist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var threshold = curDist - switchThreshold;
            if (threshold < 0) threshold = 0;
            if (Math.Sqrt(bestDistSq) > threshold) best = targetEntity;
        }

        targetEntity = best!;
    }
}
