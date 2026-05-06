namespace TemporalSiege.Rifts;

/// <summary>
/// Live set of active <see cref="EntityRift"/> instances. Source of truth for
/// "which rifts are spawning mobs right now?" Other systems read from here
/// rather than scanning all loaded entities.
///
/// Rifts add themselves on spawn and remove on death/despawn via
/// <see cref="RiftSystem"/>. The registry survives multiple storms but is
/// cleared between them.
/// </summary>
public class RiftRegistry
{
    private readonly HashSet<EntityRift> active = new();

    public IReadOnlyCollection<EntityRift> Active => active;

    public int Count => active.Count;

    /// <summary>Raised on every successful Add — RiftSystem uses this to wire per-rift event handlers exactly once.</summary>
    public event Action<EntityRift>? OnRiftAdded;

    public void Add(EntityRift rift)
    {
        if (active.Add(rift)) OnRiftAdded?.Invoke(rift);
    }

    public void Remove(EntityRift rift) => active.Remove(rift);

    public void Clear() => active.Clear();
}
