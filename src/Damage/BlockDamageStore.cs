using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace TemporalSiege.Damage;

/// <summary>
/// Per-block damage tracking. Sparse: only damaged blocks
/// (0 &lt; damage &lt; resistance) live in the dict; missing key = full HP.
///
/// Storage: per-chunk <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/>
/// cached in memory, mirrored to <see cref="Vintagestory.API.Common.IWorldChunk.SetModdata(string, byte[])"/>
/// on every modification (write-through). Cache entries are evicted on
/// chunk-column unload to bound memory.
///
/// Server-side only. Per ADR-0003, this does not modify VS's block storage.
/// </summary>
public class BlockDamageStore
{
    private const string ModdataKey = "temporalsiege:blockdamage";

    private readonly ICoreServerAPI sapi;
    private readonly Dictionary<(int cx, int cy, int cz), Dictionary<BlockPos, float>> chunkDamage = new();

    public BlockDamageStore(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        sapi.Event.ChunkColumnLoaded += OnColumnLoaded;
        sapi.Event.ChunkColumnUnloaded += OnColumnUnloaded;
    }

    /// <summary>
    /// Apply positive damage at <paramref name="pos"/>. If accumulated damage
    /// reaches the block's resistance, the block is replaced with air and the
    /// entry is cleared.
    /// </summary>
    /// <returns>true if the block was destroyed by this hit.</returns>
    public bool ApplyDamage(BlockPos pos, float amount)
    {
        if (amount <= 0) return false;

        var block = sapi.World.BlockAccessor.GetBlock(pos);
        if (block == null || block.Id == 0) return false;
        var resistance = block.Resistance;
        if (resistance <= 0) return false; // unbreakable

        var key = ChunkKey(pos);
        if (!chunkDamage.TryGetValue(key, out var dict))
        {
            dict = new Dictionary<BlockPos, float>();
            chunkDamage[key] = dict;
        }

        dict.TryGetValue(pos, out var current);
        var next = current + amount;

        if (next >= resistance)
        {
            dict.Remove(pos);
            sapi.World.BlockAccessor.SetBlock(0, pos);
            FlushChunk(key, dict);
            return true;
        }

        dict[pos] = next;
        FlushChunk(key, dict);
        return false;
    }

    /// <summary>Reduce damage at <paramref name="pos"/>. If it reaches 0 the entry is removed.</summary>
    public void Repair(BlockPos pos, float amount)
    {
        if (amount <= 0) return;
        var key = ChunkKey(pos);
        if (!chunkDamage.TryGetValue(key, out var dict)) return;
        if (!dict.TryGetValue(pos, out var current)) return;

        var next = current - amount;
        if (next <= 0) dict.Remove(pos);
        else dict[pos] = next;
        FlushChunk(key, dict);
    }

    /// <summary>Read current damage. Returns 0 for undamaged or stale entries (lazily cleaned).</summary>
    public float GetDamage(BlockPos pos)
    {
        var key = ChunkKey(pos);
        if (!chunkDamage.TryGetValue(key, out var dict)) return 0;
        if (!dict.TryGetValue(pos, out var d)) return 0;

        // Lazy invalidation: if the block isn't there any more (player mined it,
        // explosion, etc.) drop the stale entry.
        var block = sapi.World.BlockAccessor.GetBlock(pos);
        if (block == null || block.Id == 0)
        {
            dict.Remove(pos);
            FlushChunk(key, dict);
            return 0;
        }
        return d;
    }

    /// <summary>Force-clear damage at <paramref name="pos"/>.</summary>
    public void Clear(BlockPos pos)
    {
        var key = ChunkKey(pos);
        if (!chunkDamage.TryGetValue(key, out var dict)) return;
        if (!dict.Remove(pos)) return;
        FlushChunk(key, dict);
    }

    /// <summary>Snapshot of all currently-damaged blocks across loaded chunks. Order undefined.</summary>
    public IEnumerable<KeyValuePair<BlockPos, float>> EnumerateDamaged()
    {
        foreach (var dict in chunkDamage.Values)
        {
            foreach (var kv in dict) yield return kv;
        }
    }

    /// <summary>Total entries across loaded chunks. Cheap.</summary>
    public int DamagedBlockCount
    {
        get
        {
            int n = 0;
            foreach (var dict in chunkDamage.Values) n += dict.Count;
            return n;
        }
    }

    private void OnColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        for (int cy = 0; cy < chunks.Length; cy++)
        {
            var chunk = chunks[cy];
            if (chunk == null) continue;
            var bytes = chunk.GetModdata(ModdataKey);
            if (bytes == null || bytes.Length == 0) continue;
            try
            {
                var dict = Deserialize(bytes);
                if (dict.Count > 0)
                    chunkDamage[(chunkCoord.X, cy, chunkCoord.Y)] = dict;
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[TemporalSiege] failed to deserialize damage for chunk ({0},{1},{2}): {3}", chunkCoord.X, cy, chunkCoord.Y, ex.Message);
            }
        }
    }

    private void OnColumnUnloaded(Vec3i chunkCoord)
    {
        // Write-through means moddata is already current; just evict cache.
        var toRemove = new List<(int, int, int)>();
        foreach (var key in chunkDamage.Keys)
        {
            if (key.cx == chunkCoord.X && key.cz == chunkCoord.Z) toRemove.Add(key);
        }
        foreach (var key in toRemove) chunkDamage.Remove(key);
    }

    private void FlushChunk((int cx, int cy, int cz) key, Dictionary<BlockPos, float> dict)
    {
        var chunk = sapi.WorldManager.GetChunk(key.cx, key.cy, key.cz);
        if (chunk == null) return;

        if (dict.Count == 0)
        {
            chunk.SetModdata(ModdataKey, (byte[]?)null);
            chunkDamage.Remove(key);
        }
        else
        {
            chunk.SetModdata(ModdataKey, Serialize(dict));
        }
        chunk.MarkModified();
    }

    private static (int cx, int cy, int cz) ChunkKey(BlockPos pos)
    {
        // VS chunk size is 32 (>>5). Use InternalY for dimension awareness.
        return (pos.X >> 5, pos.InternalY >> 5, pos.Z >> 5);
    }

    private static byte[] Serialize(Dictionary<BlockPos, float> dict)
    {
        using var ms = new MemoryStream(4 + dict.Count * 16);
        using var w = new BinaryWriter(ms);
        w.Write(dict.Count);
        foreach (var kv in dict)
        {
            w.Write(kv.Key.X);
            w.Write(kv.Key.InternalY);
            w.Write(kv.Key.Z);
            w.Write(kv.Value);
        }
        return ms.ToArray();
    }

    private static Dictionary<BlockPos, float> Deserialize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);
        var count = r.ReadInt32();
        var dict = new Dictionary<BlockPos, float>(count);
        for (int i = 0; i < count; i++)
        {
            int x = r.ReadInt32();
            int y = r.ReadInt32();
            int z = r.ReadInt32();
            float dmg = r.ReadSingle();
            dict[new BlockPos(x, y, z, 0)] = dmg;
        }
        return dict;
    }
}
