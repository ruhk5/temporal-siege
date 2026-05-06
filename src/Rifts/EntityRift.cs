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

    public override bool ReceiveDamage(DamageSource damageSource, float damage)
    {
        // Block damage during collapse — the rift is dying on its own timer.
        if (IsCollapsing) return false;

        var accepted = base.ReceiveDamage(damageSource, damage);
        if (!accepted) return false;

        var health = GetBehavior<EntityBehaviorHealth>();
        if (health != null && health.Health <= 0 && !deathHandled)
        {
            deathHandled = true;
            HandleDeath();
        }
        return accepted;
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
        // arg = null means "everyone"). The pillar is a column above the rift.
        var minPos = new Vec3d(Pos.X - 0.4, Pos.Y, Pos.Z - 0.4);
        var maxPos = new Vec3d(Pos.X + 0.4, Pos.Y + ParticlePillarHeight, Pos.Z + 0.4);
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
        var minPos = new Vec3d(Pos.X - 0.6, Pos.Y + ParticlePillarHeight * 0.6, Pos.Z - 0.6);
        var maxPos = new Vec3d(Pos.X + 0.6, Pos.Y + ParticlePillarHeight * 0.9, Pos.Z + 0.6);
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
