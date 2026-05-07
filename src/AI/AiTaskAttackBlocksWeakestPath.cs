using TemporalSiege.Damage;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TemporalSiege.AI;

/// <summary>
/// Weakest-path A* + block-attack state machine (ADR-0003). Composes:
///   - <see cref="WeakestPathFinder"/> for path computation
///   - <see cref="BlockDamageStore"/> for damage application (#4)
///
/// Per-task path cache: recomputed every <c>repathIntervalSec</c>, so the cost
/// of A* is amortised across many ticks. Don't aim for perfect realtime; aim
/// for believable (per ADR-0003).
///
/// JSON parameters:
/// <list type="bullet">
///   <item>damagePerAttack    block damage per attack swing. Default 2.</item>
///   <item>attackRate         attacks per second. Default 1.5.</item>
///   <item>maxPathLength      A* search radius bound in blocks. Default 64.</item>
///   <item>blockHardnessBias  multiplier on block Resistance in step cost. Default 1.</item>
///   <item>repathIntervalSec  re-run A* this often (seconds). Default 2.</item>
/// </list>
/// </summary>
public class AiTaskAttackBlocksWeakestPath : AiTaskBaseTargetable
{
    private float damagePerAttack;
    private float attackRate;
    private int   maxPathLength;
    private float blockHardnessBias;
    private float repathIntervalSec;
    private float targetRange;
    private float movespeed;
    private float meleeYieldRange;
    private string? walkAnimation;
    private float walkAnimationSpeed;
    private long  lastTargetScanMs;

    private List<BlockPos>? path;
    private int pathIdx;
    private long lastRepathMs;
    private long lastAttackMs;
    private BlockDamageStore? damageStore;

    public AiTaskAttackBlocksWeakestPath(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        damagePerAttack    = taskConfig["damagePerAttack"].AsFloat(2f);
        attackRate         = taskConfig["attackRate"].AsFloat(1.5f);
        maxPathLength      = taskConfig["maxPathLength"].AsInt(64);
        blockHardnessBias  = taskConfig["blockHardnessBias"].AsFloat(1f);
        repathIntervalSec  = taskConfig["repathIntervalSec"].AsFloat(2f);
        targetRange        = taskConfig["targetRange"].AsFloat(128f);
        movespeed          = taskConfig["movespeed"].AsFloat(0.04f);
        meleeYieldRange    = taskConfig["meleeYieldRange"].AsFloat(2.5f);
        walkAnimation      = taskConfig["animation"].AsString(null);
        walkAnimationSpeed = taskConfig["animationSpeed"].AsFloat(1f);
        entity.World.Logger.Notification("[TemporalSiege] AiTaskAttackBlocksWeakestPath instantiated for {0} (targetRange={1})", entity.Code, targetRange);
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity))
        {
            DiagOnce("gated-off", () => $"AttackBlocksWeakestPath.ShouldExecute=false (storm gate closed) on {entity.EntityId}");
            return false;
        }

        var now = entity.World.ElapsedMilliseconds;
        if (targetEntity == null || !targetEntity.Alive || (now - lastTargetScanMs) > 500)
        {
            targetEntity = FindNearestPlayer();
            lastTargetScanMs = now;
        }
        if (targetEntity == null)
        {
            DiagOnce("no-target", () => $"AttackBlocksWeakestPath.ShouldExecute=false (no player in range {targetRange}) on {entity.EntityId}");
            return false;
        }

        // Yield slot to MeleeAttack when target is within strike range — otherwise
        // we monopolize slot 0 and the melee task never gets to swing.
        var dx = targetEntity.Pos.X - entity.Pos.X;
        var dy = targetEntity.Pos.Y - entity.Pos.Y;
        var dz = targetEntity.Pos.Z - entity.Pos.Z;
        if (dx * dx + dy * dy + dz * dz <= meleeYieldRange * meleeYieldRange)
        {
            DiagOnce("yield-melee", () => $"AttackBlocksWeakestPath yielding to MeleeAttack (target {targetEntity.EntityId} within {meleeYieldRange})");
            return false;
        }

        EnsureDamageStore();
        if (damageStore == null)
        {
            DiagOnce("no-damagestore", () => $"AttackBlocksWeakestPath.ShouldExecute=false (no BlockDamageStore) on {entity.EntityId}");
            return false;
        }

        DiagOnce("ok", () => $"AttackBlocksWeakestPath.ShouldExecute=TRUE for {entity.EntityId} -> target {targetEntity.EntityId}");
        return true;
    }

    private string lastDiag = "";
    private int diagCallCount;
    private void DiagOnce(string key, Func<string> msg)
    {
        // First call: log unconditionally so we can see if ShouldExecute is
        // even being polled by the task manager. After that, only log when
        // state changes — avoids spamming every tick.
        diagCallCount++;
        if (diagCallCount <= 3 || lastDiag != key)
        {
            lastDiag = key;
            entity.World.Logger.Notification("[TemporalSiege] AttackBlocks call#{0}: {1}", diagCallCount, msg());
        }
    }

    private Entity? FindNearestPlayer()
    {
        var rangeSq = targetRange * targetRange;
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

    public override void StartExecute()
    {
        // base.StartExecute() drives animation start (reads animMeta from
        // JSON), cooldown init, optional sound. Without it, drifters move
        // with frozen legs and no walk audio.
        base.StartExecute();
        ComputePath();
        pathIdx = 0;
        lastRepathMs = entity.World.ElapsedMilliseconds;
        var pathSummary = path == null || path.Count == 0
            ? "<no path found>"
            : $"length {path.Count}, first 3=[{string.Join(",", path.Take(3).Select(p => $"({p.X},{p.Y},{p.Z})"))}], last=({path[path.Count-1].X},{path[path.Count-1].Y},{path[path.Count-1].Z})";
        entity.World.Logger.Notification("[TemporalSiege] AttackBlocks.StartExecute on {0} -> {1}",
            entity.EntityId, pathSummary);
    }

    public override bool ContinueExecute(float dt)
    {
        if (targetEntity == null || !targetEntity.Alive || damageStore == null) return false;

        var now = entity.World.ElapsedMilliseconds;

        if ((now - lastRepathMs) / 1000f >= repathIntervalSec)
        {
            ComputePath();
            pathIdx = 0;
            lastRepathMs = now;
        }

        if (path == null || pathIdx >= path.Count)
        {
            DiagOnce("no-path", () => $"AttackBlocks.ContinueExecute: path null/exhausted (path={path?.Count ?? -1}, idx={pathIdx})");
            return false;
        }

        var nextPos = path[pathIdx];
        var block = entity.World.BlockAccessor.GetBlock(nextPos);

        // Solid in the way — attack it instead of walking through.
        if (block != null && block.Id != 0 && block.Resistance > 0)
        {
            entity.Controls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);

            var attackPeriodMs = 1000f / Math.Max(attackRate, 0.01f);
            if (now - lastAttackMs >= attackPeriodMs)
            {
                lastAttackMs = now;
                var destroyed = damageStore.ApplyDamage(nextPos, damagePerAttack);

                // Drifter hitbox is 1.3 blocks tall — a 1-block-tall hole isn't
                // wide enough to walk through without head-clipping. Mirror the
                // damage onto the block immediately above so we open a 2-tall
                // corridor when chewing through walls.
                var headPos = new BlockPos(nextPos.X, nextPos.Y + 1, nextPos.Z, 0);
                var headBlock = entity.World.BlockAccessor.GetBlock(headPos);
                if (headBlock != null && headBlock.Id != 0 && headBlock.Resistance > 0)
                {
                    damageStore.ApplyDamage(headPos, damagePerAttack);
                }

                DiagOnce($"attack-{destroyed}", () => $"AttackBlocks attacking ({nextPos.X},{nextPos.Y},{nextPos.Z}) R={block.Resistance:F0} destroyed={destroyed}");

                if (destroyed)
                {
                    // Block destroyed by this swing — advance immediately.
                    pathIdx++;
                }
            }
            return true;
        }

        // Air — walk toward block centre.
        var dx = nextPos.X + 0.5 - entity.Pos.X;
        var dy = nextPos.Y + 0.5 - entity.Pos.Y;
        var dz = nextPos.Z + 0.5 - entity.Pos.Z;
        var len2d = Math.Sqrt(dx * dx + dz * dz);
        if (len2d > 0.001)
        {
            // WalkVector magnitude IS the per-tick speed. Vanilla mobs use
            // movespeed of ~0.018 (slow) to ~0.04 (running). Setting a unit
            // vector here makes them teleport.
            entity.Controls.WalkVector.Set((dx / len2d) * movespeed, 0, (dz / len2d) * movespeed);
            entity.Controls.Forward = true;
            // Face the direction of travel so animations and step-up work correctly.
            entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
        }

        // Advance only when the drifter is actually inside the waypoint's
        // cell. The earlier `len2d < 1.5` check matched any waypoint within
        // 1.5 blocks, so the path was getting consumed in a single tick before
        // the drifter had moved. Cell-based advance keeps step in sync with
        // physics movement.
        var driftBlock = entity.Pos.AsBlockPos;
        if (driftBlock.X == nextPos.X && driftBlock.Z == nextPos.Z && Math.Abs(driftBlock.Y - nextPos.Y) <= 1)
        {
            pathIdx++;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        // base.FinishExecute drives animation stop + cooldown set.
        base.FinishExecute(cancelled);
        entity.Controls.Forward = false;
        entity.Controls.WalkVector.Set(0, 0, 0);
        path = null;
    }

    private void EnsureDamageStore()
    {
        if (damageStore != null) return;
        var modsys = entity.World.Api.ModLoader.GetModSystem<TemporalSiegeModSystem>();
        damageStore = modsys?.BlockDamage;
    }

    private void ComputePath()
    {
        if (targetEntity == null) { path = null; return; }
        var start = new BlockPos((int)Math.Floor(entity.Pos.X), (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z), 0);
        var goal  = new BlockPos((int)Math.Floor(targetEntity.Pos.X), (int)Math.Floor(targetEntity.Pos.Y), (int)Math.Floor(targetEntity.Pos.Z), 0);
        path = WeakestPathFinder.FindPath(entity.World.BlockAccessor, start, goal, maxPathLength, blockHardnessBias);
    }
}
