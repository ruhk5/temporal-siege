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
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;
        if (targetEntity == null || !targetEntity.Alive) return false;
        EnsureDamageStore();
        // Never crash a mob if the damage system isn't present (e.g., misconfig); just skip.
        return damageStore != null;
    }

    public override void StartExecute()
    {
        ComputePath();
        pathIdx = 0;
        lastRepathMs = entity.World.ElapsedMilliseconds;
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

        if (path == null || pathIdx >= path.Count) return false;

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
                if (damageStore.ApplyDamage(nextPos, damagePerAttack))
                {
                    // Block destroyed by this swing — advance immediately.
                    pathIdx++;
                }
            }
            return true;
        }

        // Air — walk toward block centre.
        var dx = nextPos.X + 0.5 - entity.Pos.X;
        var dz = nextPos.Z + 0.5 - entity.Pos.Z;
        var len2d = Math.Sqrt(dx * dx + dz * dz);
        if (len2d > 0.001)
        {
            entity.Controls.WalkVector.Set(dx / len2d, 0, dz / len2d);
            entity.Controls.Forward = true;
        }

        // Advance once we're close enough to this waypoint.
        if (len2d < 0.6) pathIdx++;

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
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
