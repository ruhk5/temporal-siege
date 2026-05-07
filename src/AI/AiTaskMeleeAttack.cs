using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using VanillaMeleeAttack = Vintagestory.GameContent.AiTaskMeleeAttack;

namespace TemporalSiege.AI;

/// <summary>
/// Diagnostic wrapper around vanilla AiTaskMeleeAttack. Inherits all behaviour
/// (target acquisition, raycast contact check, attack swing, animation, sound,
/// damage application) and adds detailed logging so we can see exactly which
/// internal gate is failing when drifters reach the player but don't engage.
///
/// Reflects out the relevant private fields so each ShouldExecute call logs:
///   - cooldown state (cooldownUntilMs, lastCheckOrAttackMs, attackDurationMs)
///   - emotion-state bridge (bhEmo presence)
///   - target distance vs minDist/minVerDist/attackRange
///   - whether base.ShouldExecute returned true/false
///
/// Storm-gated (Phase 5.3 / #21) — outside active storms the task is forced
/// off so the drifter falls back to vanilla idle/wander.
/// </summary>
public class AiTaskMeleeAttack : VanillaMeleeAttack
{
    private static readonly FieldInfo? CooldownUntilMsField =
        typeof(VanillaMeleeAttack).BaseType?.BaseType?.GetField("cooldownUntilMs", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? typeof(VanillaMeleeAttack).GetField("cooldownUntilMs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? LastCheckOrAttackMsField =
        typeof(VanillaMeleeAttack).GetField("lastCheckOrAttackMs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AttackDurationMsField =
        typeof(VanillaMeleeAttack).GetField("attackDurationMs", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? MinDistField =
        typeof(VanillaMeleeAttack).GetField("minDist", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? MinVerDistField =
        typeof(VanillaMeleeAttack).GetField("minVerDist", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AttackRangeField =
        typeof(VanillaMeleeAttack).GetField("attackRange", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? TargetEntityField =
        typeof(VanillaMeleeAttack).BaseType?.GetField("targetEntity", BindingFlags.NonPublic | BindingFlags.Instance);

    private long lastLogMs;

    public AiTaskMeleeAttack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        entity.World.Logger.Notification(
            "[TemporalSiege] AiTaskMeleeAttack instantiated for {0} | minDist={1} minVerDist={2} attackRange={3}",
            entity.Code, GetField(MinDistField), GetField(MinVerDistField), GetField(AttackRangeField));
    }

    public override bool ShouldExecute()
    {
        if (StormGate.IsClosedFor(entity)) return false;

        var now = entity.World.ElapsedMilliseconds;

        // Snapshot pre-call state
        var cooldownUntil = (long)(GetField(CooldownUntilMsField) ?? 0L);
        var lastCheck = (long)(GetField(LastCheckOrAttackMsField) ?? 0L);
        var attackDur = (int)(GetField(AttackDurationMsField) ?? 0);
        var preTarget = (Entity?)GetField(TargetEntityField);

        // Compute distance to nearest player just for diagnostic context
        Entity? nearestPlayer = null;
        double nearestDist = double.NaN;
        foreach (var p in entity.World.AllOnlinePlayers)
        {
            var pe = p?.Entity;
            if (pe == null || !pe.Alive) continue;
            var dx = pe.Pos.X - entity.Pos.X;
            var dy = pe.Pos.Y - entity.Pos.Y;
            var dz = pe.Pos.Z - entity.Pos.Z;
            var d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (nearestPlayer == null || d < nearestDist) { nearestPlayer = pe; nearestDist = d; }
        }

        var r = base.ShouldExecute();
        var postTarget = (Entity?)GetField(TargetEntityField);

        // Throttle: log every 1s when there's a nearby player, plus every result-change
        if (now - lastLogMs > 1000 && nearestPlayer != null && nearestDist < 6)
        {
            lastLogMs = now;
            entity.World.Logger.Notification(
                "[TemporalSiege] MeleeAttack on {0}: result={1} | nearestPlayer={2} dist={3:F2} | cdUntil={4} (now={5}, gated={6}) | lastAttack+dur={7} | preTarget={8} postTarget={9}",
                entity.EntityId, r,
                nearestPlayer.EntityId, nearestDist,
                cooldownUntil, now, cooldownUntil > now,
                lastCheck + attackDur,
                preTarget?.EntityId, postTarget?.EntityId);
        }
        return r;
    }

    private object? GetField(FieldInfo? f)
    {
        if (f == null) return null;
        try { return f.GetValue(this); } catch { return null; }
    }
}
