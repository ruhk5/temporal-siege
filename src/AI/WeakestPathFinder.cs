using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace TemporalSiege.AI;

/// <summary>
/// 6-neighbor A* over the voxel grid where step cost = 1 for air and
/// 1 + Resistance × hardnessBias for solid blocks. Per ADR-0003: produces the
/// path that's *cheapest to chew through*, which is the iconic 7D2D weakest-
/// path behavior. The heuristic is straight-line Euclidean distance — admissible
/// since the minimum step cost is 1.
///
/// v1 trade-offs:
///   - 6-direction only (no diagonals). Simpler, cheaper.
///   - Search radius bounded by Euclidean distance from start, not path length.
///   - "block break-time × hardness" from ADR-0003 simplified to block.Resistance.
///     Refine if playtesting wants finer-grained tuning.
///   - Unbreakable blocks (Resistance ≤ 0) are impassable.
/// </summary>
internal static class WeakestPathFinder
{
    private static readonly Vec3i[] Neighbors =
    {
        new( 1,  0,  0), new(-1,  0,  0),
        new( 0,  1,  0), new( 0, -1,  0),
        new( 0,  0,  1), new( 0,  0, -1),
    };

    public static List<BlockPos>? FindPath(
        IBlockAccessor ba,
        BlockPos start,
        BlockPos goal,
        int maxRadiusBlocks,
        float hardnessBias)
    {
        if (start.Equals(goal)) return new List<BlockPos> { start };

        var open = new PriorityQueue<BlockPos, float>();
        var cameFrom = new Dictionary<BlockPos, BlockPos>();
        var gScore = new Dictionary<BlockPos, float> { [start] = 0f };
        open.Enqueue(start, Heuristic(start, goal));

        var maxRadiusSq = (long)maxRadiusBlocks * maxRadiusBlocks;
        BlockPos? reached = null;

        while (open.TryDequeue(out var current, out _))
        {
            if (current.Equals(goal)) { reached = current; break; }

            var currentG = gScore[current];

            foreach (var off in Neighbors)
            {
                var nb = new BlockPos(current.X + off.X, current.Y + off.Y, current.Z + off.Z, 0);

                // Hard cap: don't search beyond maxRadiusBlocks of the start.
                long dx = nb.X - start.X, dy = nb.Y - start.Y, dz = nb.Z - start.Z;
                if (dx * dx + dy * dy + dz * dz > maxRadiusSq) continue;

                var stepCost = StepCost(ba, nb, hardnessBias);
                if (float.IsPositiveInfinity(stepCost)) continue;

                // Drifters can't fly. A pure-vertical step UP into an air cell
                // (same X,Z, Y+1) requires either a jump or something to climb,
                // neither of which we model. Skip these so A* prefers
                // break-through-wall paths over flying-up-into-the-sky paths.
                // Skybase exploit (ADR-0003) is tolerated in v1: if no
                // horizontal path exists, A* fails and drifters give up.
                if (off.Y > 0 && off.X == 0 && off.Z == 0)
                {
                    var nbBlock = ba.GetBlock(nb);
                    if (nbBlock != null && nbBlock.Id == 0) continue;
                }

                var tentative = currentG + stepCost;
                if (gScore.TryGetValue(nb, out var existing) && tentative >= existing) continue;

                cameFrom[nb] = current;
                gScore[nb] = tentative;
                open.Enqueue(nb, tentative + Heuristic(nb, goal));
            }
        }

        if (reached == null) return null;

        var path = new List<BlockPos>();
        var p = reached;
        while (p != null)
        {
            path.Add(p);
            if (!cameFrom.TryGetValue(p, out var prev)) break;
            p = prev;
        }
        path.Reverse();
        return path;
    }

    private static float Heuristic(BlockPos a, BlockPos b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static float StepCost(IBlockAccessor ba, BlockPos pos, float hardnessBias)
    {
        var block = ba.GetBlock(pos);
        if (block == null) return float.PositiveInfinity;
        if (block.Id == 0) return 1f;
        if (block.Resistance <= 0) return float.PositiveInfinity;
        return 1f + block.Resistance * hardnessBias;
    }
}
