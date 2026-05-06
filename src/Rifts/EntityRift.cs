using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalSiege.Rifts;

/// <summary>
/// Custom entity class for a temporal rift. Server-authoritative HP, immobile,
/// emits a vertical pillar of light (server-broadcast particles) and a periodic
/// audio loop. Damageable. Closing it (HP→0) raises the close event so the
/// rift system can drop shards and stop spawning from it.
///
/// Visual identity is the particle pillar — the entity's own shape is a minimal
/// placeholder. JSON entity definition: <c>assets/temporalsiege/entities/land/rift.json</c>.
///
/// Lifecycle:
///   - Spawn: <see cref="OnEntitySpawn"/> — the rift is created via
///     <see cref="RiftPlacementService"/> on storm-start / between waves.
///   - Tick:  <see cref="OnGameTick"/> — emits particles, plays audio loop,
///     suppresses motion (no physics behaviour means it's already immobile,
///     but we also zero velocity defensively).
///   - Death: <see cref="ReceiveDamage"/> calls <see cref="HandleDeath"/> when
///     <see cref="EntityBehaviorHealth"/> reports zero HP. Entity then dies
///     normally; the rift system drops loot in its <see cref="OnRiftClosed"/>
///     handler (subscribed via <see cref="RiftRegistry"/>).
/// </summary>
public class EntityRift : Entity
{
    private const int ParticlePillarHeight = 18;
    private const float ParticlePillarBaseOffset = 1.0f;
    private const int ParticlesPerTick = 6;
    private const float SoundLoopIntervalSec = 4f;

    /// <summary>Raised server-side when the rift's HP hits zero from damage.</summary>
    public event Action<EntityRift>? OnPlayerClosed;

    /// <summary>Raised server-side when the rift is despawned by storm-end collapse (NOT by combat).</summary>
    public event Action<EntityRift>? OnStormEndCollapsed;

    /// <summary>True once collapse begins — disables further spawns from this rift and starts the death timer.</summary>
    public bool IsCollapsing { get; private set; }

    private float collapseSecondsRemaining;
    private float secondsSinceLastSound;
    private bool deathHandled;

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        // Defensive: zero velocity so the entity stays put. We don't run the
        // "physics" behaviour, so gravity isn't applied, but other code paths
        // can nudge motion (e.g. knockback). This keeps it pinned.
        Pos.Motion.Set(0, 0, 0);
        SelfRegister();
    }

    public override void OnEntityLoaded()
    {
        // Fires when a saved rift entity is rehydrated from a chunk on world
        // load. Without this, the in-memory RiftRegistry stays empty after a
        // save/exit/reload even though the rift entities themselves persisted,
        // so debug commands and storm-end collapse can't find them.
        base.OnEntityLoaded();
        SelfRegister();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        base.OnEntityDespawn(despawn);
        SelfUnregister();
    }

    private void SelfRegister()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        var mod = Api.ModLoader.GetModSystem<TemporalSiegeModSystem>();
        mod?.Rifts?.Registry.Add(this);
    }

    private void SelfUnregister()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        var mod = Api.ModLoader.GetModSystem<TemporalSiegeModSystem>();
        mod?.Rifts?.Registry.Remove(this);
    }

    public override void OnGameTick(float dt)
    {
        base.OnGameTick(dt);
        if (World.Side != EnumAppSide.Server) return;

        // Pin in place — base.OnGameTick may have applied tiny motion deltas.
        Pos.Motion.Set(0, 0, 0);

        if (IsCollapsing)
        {
            collapseSecondsRemaining -= dt;
            EmitCollapseParticles();
            if (collapseSecondsRemaining <= 0 && !deathHandled)
            {
                deathHandled = true;
                OnStormEndCollapsed?.Invoke(this);
                Die(EnumDespawnReason.Removed, null);
            }
            return;
        }

        EmitPillarParticles();

        secondsSinceLastSound += dt;
        if (secondsSinceLastSound >= SoundLoopIntervalSec)
        {
            secondsSinceLastSound = 0;
            PlayLoopSound();
        }
    }

    public override bool ShouldReceiveDamage(DamageSource damageSource, float damage)
    {
        // Defensive override: the base implementation gates damage on flags
        // (e.g. invulnerability) that don't apply to the rift.
        return !IsCollapsing;
    }

    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        if (IsCollapsing) return false;

        var accepted = base.ReceiveDamage(damageSource, damage);
        var health = GetBehavior<EntityBehaviorHealth>();

        if (accepted)
        {
            EmitHitFeedback(damageSource);
        }

        if (accepted && health != null && health.Health <= 0 && !deathHandled)
        {
            deathHandled = true;
            HandleDeath();
        }
        return accepted;
    }

    private void EmitHitFeedback(DamageSource damageSource)
    {
        // Particle burst at the hit position so the player sees per-swing impact.
        // Fall back to the rift's centre if the source didn't carry a hit pos.
        var hp = damageSource?.HitPosition;
        var origin = hp != null
            ? new Vec3d(Pos.X + hp.X, Pos.Y + hp.Y, Pos.Z + hp.Z)
            : new Vec3d(Pos.X, Pos.Y + 0.75, Pos.Z);

        var minPos = new Vec3d(origin.X - 0.2, origin.Y - 0.2, origin.Z - 0.2);
        var maxPos = new Vec3d(origin.X + 0.2, origin.Y + 0.2, origin.Z + 0.2);
        var minVel = new Vec3f(-1.5f, -0.5f, -1.5f);
        var maxVel = new Vec3f( 1.5f,  1.5f,  1.5f);
        const int colorYellow = unchecked((int)0xFFFFEE40);

        World.SpawnParticles(
            quantity: 14,
            color: colorYellow,
            minPos: minPos,
            maxPos: maxPos,
            minVelocity: minVel,
            maxVelocity: maxVel,
            lifeLength: 0.6f,
            gravityEffect: 0.3f,
            scale: 0.4f,
            model: EnumParticleModel.Quad,
            dualCallByPlayer: null);

        // Distinct hit sound — cuts through the ambient pillar loop.
        World.PlaySoundAt(
            new AssetLocation("game:sounds/effect/translocate-breakdimension"),
            Pos.X, Pos.Y + 1.0, Pos.Z,
            dualCallByPlayer: null,
            randomizePitch: true,
            range: 24f,
            volume: 0.8f);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
    {
        // VS dispatches player left-click melee through OnInteract(mode=Attack).
        // The base Entity.OnInteract does nothing with this — only EntityAgent
        // turns it into damage. Since EntityRift extends Entity (not EntityAgent),
        // we have to translate Attack-mode interactions into damage ourselves.
        if (mode == EnumInteractMode.Attack && Alive && !IsCollapsing && World.Side == EnumAppSide.Server)
        {
            var dmg = itemslot?.Itemstack?.Collectible?.AttackPower ?? 0.5f;
            ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = byEntity,
                HitPosition = hitPosition,
                Type = EnumDamageType.BluntAttack,
            }, dmg);
            return;
        }
        base.OnInteract(byEntity, itemslot, hitPosition, mode);
    }

    /// <summary>
    /// Begin the storm-end collapse. The rift stops accepting damage, plays a
    /// collapse particle effect, and despawns after <paramref name="seconds"/>.
    /// Caller is responsible for spawning the persistent loot pile.
    /// </summary>
    public void BeginStormEndCollapse(float seconds)
    {
        if (IsCollapsing || deathHandled) return;
        IsCollapsing = true;
        collapseSecondsRemaining = seconds;
    }

    private void HandleDeath()
    {
        OnPlayerClosed?.Invoke(this);
        Die(EnumDespawnReason.Death, null);
    }

    private void EmitPillarParticles()
    {
        // Server broadcasts the particle to all clients in range (the IPlayer
        // arg = null means "everyone"). The pillar starts above the rift's
        // body so the cube isn't drowned by the dense base of the column.
        var pillarBase = Pos.Y + ParticlePillarBaseOffset;
        var minPos = new Vec3d(Pos.X - 0.4, pillarBase, Pos.Z - 0.4);
        var maxPos = new Vec3d(Pos.X + 0.4, pillarBase + ParticlePillarHeight, Pos.Z + 0.4);
        var minVel = new Vec3f(-0.05f, 0.4f, -0.05f);
        var maxVel = new Vec3f( 0.05f, 0.8f,  0.05f);

        // Cycle through a couple of colours to give the pillar that "unstable
        // temporal energy" feel. Server-deterministic so all clients agree.
        const int colorPurple = unchecked((int)0xFFAA40FF);
        const int colorMagenta = unchecked((int)0xFFFF40AA);
        var color = (World.ElapsedMilliseconds / 200 % 2 == 0) ? colorPurple : colorMagenta;

        World.SpawnParticles(
            quantity: ParticlesPerTick,
            color: color,
            minPos: minPos,
            maxPos: maxPos,
            minVelocity: minVel,
            maxVelocity: maxVel,
            lifeLength: 1.5f,
            gravityEffect: -0.02f,
            scale: 0.35f,
            model: EnumParticleModel.Quad,
            dualCallByPlayer: null);
    }

    private void EmitCollapseParticles()
    {
        // Inward implosion — particles spawn at the top of the pillar and
        // converge on the rift origin. Visually distinct from the steady pillar.
        var pillarBase = Pos.Y + ParticlePillarBaseOffset;
        var minPos = new Vec3d(Pos.X - 0.6, pillarBase + ParticlePillarHeight * 0.6, Pos.Z - 0.6);
        var maxPos = new Vec3d(Pos.X + 0.6, pillarBase + ParticlePillarHeight * 0.9, Pos.Z + 0.6);
        var minVel = new Vec3f(-0.2f, -1.5f, -0.2f);
        var maxVel = new Vec3f( 0.2f, -0.8f,  0.2f);
        const int colorWhite = unchecked((int)0xFFFFFFE0);

        World.SpawnParticles(
            quantity: 12,
            color: colorWhite,
            minPos: minPos,
            maxPos: maxPos,
            minVelocity: minVel,
            maxVelocity: maxVel,
            lifeLength: 0.8f,
            gravityEffect: 0f,
            scale: 0.5f,
            model: EnumParticleModel.Quad,
            dualCallByPlayer: null);
    }

    private void PlayLoopSound()
    {
        // VS has no built-in looping sound source on entities — we cadence
        // PlaySoundAt ourselves. Vanilla "translocate-active" is a stable
        // looping-feel ambience available in 1.22.x.
        World.PlaySoundAt(
            new AssetLocation("game:sounds/effect/translocate-active"),
            Pos.X, Pos.Y, Pos.Z,
            dualCallByPlayer: null,
            randomizePitch: true,
            range: 24f,
            volume: 0.6f);
    }
}
