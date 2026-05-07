using TemporalSiege.Damage;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// Storm-horde long-range pursuit task. Adds two capabilities on top of vanilla
/// drifter AI:
///
///   1. <b>Unlimited-range player tracking</b>. Vanilla <c>seekentity</c> caps
///      out at ~32 blocks (entity-partition grid limit). This task scans all
///      online players directly and locks on regardless of distance, then
///      hands off to <c>seekentity</c> + <c>meleeattack</c> when the drifter
///      gets close enough.
///
///   2. <b>Break-when-stuck</b>. Vanilla <see cref="WaypointsTraverser"/> calls
///      its <c>OnStuck</c> callback when no path can be found or movement has
///      halted. We switch to "attack the block in front" mode, chew through,
///      then resume navigation. Multi-block walls are handled naturally via
///      repeat (break, walk into gap, hit next block, break, etc.).
///
/// Priority should be set to <i>just below</i> vanilla <c>seekentity</c>
/// (1.5) so vanilla seek/melee preempt when the player is in close range.
///
/// Storm-gated (Phase 5.3 / #21) — outside an active storm the task short-
/// circuits and the drifter falls back to vanilla idle/wander.
/// </summary>
public class AiTaskHordePursue : AiTaskBaseTargetable
{
    private float scanRange;
    private float moveSpeed;
    private float damagePerAttack;
    private float attackRate;
    private float renavIntervalSec;

    private bool stuck;
    private BlockPos? attackingBlock;
    private long lastAttackMs;
    private long lastRenavMs;
    private BlockDamageStore? damageStore;

    public AiTaskHordePursue(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        scanRange        = taskConfig["scanRange"].AsFloat(256f);
        moveSpeed        = taskConfig["moveSpeed"].AsFloat(0.022f);
        damagePerAttack  = taskConfig["damagePerAttack"].AsFloat(2f);
        attackRate       = taskConfig["attackRate"].AsFloat(1.2f);
        renavIntervalSec = taskConfig["renavIntervalSec"].AsFloat(2f);
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;
        targetEntity = FindNearestPlayer(scanRange);
        return targetEntity != null;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        EnsureDamageStore();
        stuck = false;
        attackingBlock = null;
        lastRenavMs = entity.World.ElapsedMilliseconds;
        BeginNavigation();
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null || !targetEntity.Alive) return false;

        // While breaking through a block, ignore navigation. Resume nav on break.
        if (stuck)
        {
            return ContinueAttackBlock();
        }

        // Periodically re-issue NavigateTo so the path tracks the player as
        // they move. Vanilla's WaypointsTraverser doesn't auto-update target.
        var now = entity.World.ElapsedMilliseconds;
        if ((now - lastRenavMs) / 1000f >= renavIntervalSec)
        {
            lastRenavMs = now;
            BeginNavigation();
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser?.Stop();
        attackingBlock = null;
        stuck = false;
    }

    private void BeginNavigation()
    {
        if (targetEntity == null || pathTraverser == null) return;
        pathTraverser.NavigateTo(
            targetEntity.Pos.XYZ,
            moveSpeed,
            OnGoalReached: () => { /* arrived; handoff to seekentity/melee */ },
            OnStuck: OnPathStuck,
            onNoPath: OnPathStuck,
            mhdistanceTolerance: 0,
            creatureType: null);
    }

    private void OnPathStuck()
    {
        stuck = true;
        attackingBlock = null;
    }

    private bool ContinueAttackBlock()
    {
        if (damageStore == null) return false;

        // Pick a block to attack if we don't have one
        if (attackingBlock == null)
        {
            attackingBlock = FindBlockInFront();
            if (attackingBlock == null)
            {
                // Nothing chewable — give up, let wander/lookaround take over
                stuck = false;
                return false;
            }
        }

        var block = entity.World.BlockAccessor.GetBlock(attackingBlock);
        if (block == null || block.Id == 0 || block.Resistance <= 0)
        {
            // Block already destroyed (or never breakable). Resume nav.
            attackingBlock = null;
            stuck = false;
            BeginNavigation();
            return true;
        }

        // Stop walking — we're hitting a wall
        entity.Controls.Forward = false;
        entity.Controls.WalkVector.Set(0, 0, 0);

        var now = entity.World.ElapsedMilliseconds;
        var attackPeriodMs = 1000f / Math.Max(attackRate, 0.01f);
        if (now - lastAttackMs >= attackPeriodMs)
        {
            lastAttackMs = now;
            var destroyed = damageStore.ApplyDamage(attackingBlock, damagePerAttack);

            // Also damage the block above so a 2-tall corridor opens — drifter
            // hitbox is 1.3 blocks tall, won't fit through a 1-block hole.
            var headPos = new BlockPos(attackingBlock.X, attackingBlock.Y + 1, attackingBlock.Z, 0);
            var headBlock = entity.World.BlockAccessor.GetBlock(headPos);
            if (headBlock != null && headBlock.Id != 0 && headBlock.Resistance > 0)
            {
                damageStore.ApplyDamage(headPos, damagePerAttack);
            }

            if (destroyed)
            {
                attackingBlock = null;
                stuck = false;
                BeginNavigation();
            }
        }
        return true;
    }

    private BlockPos? FindBlockInFront()
    {
        if (targetEntity == null) return null;
        var dx = targetEntity.Pos.X - entity.Pos.X;
        var dz = targetEntity.Pos.Z - entity.Pos.Z;
        var len = Math.Sqrt(dx * dx + dz * dz);
        if (len < 0.01) return null;
        var nx = dx / len;
        var nz = dz / len;

        // Step out 1-3 blocks toward the player at body height. First solid
        // breakable block wins.
        for (int i = 1; i <= 3; i++)
        {
            var cx = (int)Math.Floor(entity.Pos.X + nx * i);
            var cz = (int)Math.Floor(entity.Pos.Z + nz * i);
            for (int dy = 0; dy <= 1; dy++)
            {
                var cy = (int)Math.Floor(entity.Pos.Y) + dy;
                var pos = new BlockPos(cx, cy, cz, 0);
                var block = entity.World.BlockAccessor.GetBlock(pos);
                if (block != null && block.Id != 0 && block.Resistance > 0)
                {
                    return pos;
                }
            }
        }
        return null;
    }

    private void EnsureDamageStore()
    {
        if (damageStore != null) return;
        var modsys = entity.World.Api.ModLoader.GetModSystem<TemporalSiegeModSystem>();
        damageStore = modsys?.BlockDamage;
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
