using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace TemporalSiege.Loot;

/// <summary>
/// Keeps spawned <see cref="EntityItem"/> instances alive longer than VS's
/// default ~10-minute despawn. Storm-end loot piles need ~24 in-game hours of
/// persistence (CONTEXT.md § Spawn (rifts)).
///
/// Approach: VS despawns dropped items by comparing
/// <c>EntityItem.itemSpawnedMilliseconds</c> against world time. We bump that
/// field forward periodically until our caller-supplied lifetime expires, then
/// stop bumping and let VS reap the entity normally.
///
/// We track entries in a list and tick once per real second — cheap, and the
/// list is short (a handful of loot piles per storm at most).
/// </summary>
public class PersistentLootKeeper
{
    private record Tracked(Entity Entity, double GameHourDeadline);

    private readonly ICoreServerAPI sapi;
    private readonly List<Tracked> tracked = new();

    public PersistentLootKeeper(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        // Tick once per second — itemSpawnedMilliseconds bumps don't need to
        // be high-frequency, default item lifetime is on the minutes scale.
        sapi.Event.RegisterGameTickListener(_ => Tick(), millisecondInterval: 1000);
    }

    /// <summary>
    /// Track <paramref name="entity"/> (an EntityItem) so it stays alive for
    /// <paramref name="gameHourLifetime"/> in-game hours from now.
    /// </summary>
    public void Track(Entity entity, float gameHourLifetime)
    {
        if (entity is not EntityItem) return;
        var deadline = sapi.World.Calendar.TotalHours + gameHourLifetime;
        tracked.Add(new Tracked(entity, deadline));
    }

    private void Tick()
    {
        if (tracked.Count == 0) return;

        var now = sapi.World.Calendar.TotalHours;
        var serverNowMs = sapi.World.ElapsedMilliseconds;

        // Iterate in reverse so we can remove without breaking the index.
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var t = tracked[i];
            if (t.Entity is not EntityItem item || !item.Alive)
            {
                tracked.RemoveAt(i);
                continue;
            }

            if (now >= t.GameHourDeadline)
            {
                // Lifetime expired — stop bumping. VS's next despawn check
                // will reap the item naturally.
                tracked.RemoveAt(i);
                continue;
            }

            // Bump the field forward so the despawn check (which compares
            // ElapsedMilliseconds - itemSpawnedMilliseconds against the
            // configured threshold) never fires.
            item.itemSpawnedMilliseconds = serverNowMs;
        }
    }
}
