# ADR-0005: Composable AI behavior library, not bespoke classes

**Status:** Accepted
**Date:** 2026-05-05

## Context

v1 has one custom enemy (the Sundered). v2's stated ambition is a rich custom enemy roster (suicide-bomber drifter, ranged spitter, more). The v1 implementation choice for the Sundered's AI determines whether v2 enemies are "few hundred lines of JSON" or "few weeks of C# each."

VS's mod system supports JSON-data-driven entity *bodies* (HP, speed, drops, model) but AI *logic* must be C#: a class implementing `EntityBehavior` or `AiTaskBase`. The architectural question is whether the C# behaviors are *bespoke* (one large `EntityBehaviorSundered` class containing all of its logic) or *composed* (a small library of single-purpose, JSON-configurable behaviors that JSON entities pick from).

## Decision

Author v1's AI as a **small library of composable, JSON-configurable behaviors**. Compose the Sundered (and future enemies) by referencing these behaviors from entity JSON.

**v1 behavior library (target ~5 behaviors):**

| Behavior | Used by Sundered | Used by v2 enemies | Notes |
|---|---|---|---|
| `AiTaskTargetNearestPlayer` | Yes | All | Aggro to nearest player. Configurable range, switch threshold. Storm-aware (respects beacon distance). |
| `AiTaskAttackBlocksWeakestPath` | Yes | Most melee | Weakest-path A* + block-attack state machine. Configurable damage-per-attack, attack-rate, max-path-length, block-hardness-bias. |
| `AiTaskMeleeAttack` | Yes | All melee | On-contact damage. Configurable damage, knockback, armor-pierce. |
| `AiTaskChargeAtTarget` | Yes (Sundered signature) | v2 chargers | Beeline-with-windup. Configurable charge-speed, windup-time, knockback. |
| `AiTaskExplodeOnContact` | **No** | v2 suicide-bomber | Written in v1 with no v1 user. ~1 week. Means v2's suicide-bomber is pure JSON. |

**Behavior parameters live in entity JSON, not in C#.** Tuning the Sundered = JSON edit + reload, not a recompile.

**Bespoke (per-item) implementations are correct for single-instance content.** Active trap block-entities (Spike, Dart, Oil, Turret) are one-off custom classes. Refactor only when v1.5 adds a similar trap that shares logic. Single instance = bespoke is correct; wait for the second instance before abstracting.

## Consequences

- **Positive.** v2 enemies that fit the existing behavior set are pure JSON. The suicide-bomber drifter, for example:

  ```json
  {
    "code": "suicidedrifter",
    "behaviors": [
      { "code": "AiTaskTargetNearestPlayer", "range": 24 },
      { "code": "AiTaskAttackBlocksWeakestPath", "damage": 1, "attackRate": 1.5 },
      { "code": "AiTaskExplodeOnContact", "damage": 35, "radius": 3, "fuse": 0.5 }
    ]
  }
  ```

- **Positive.** Tuning velocity (JSON reload, no recompile).
- **Positive.** Aligned with VS's own modding patterns. `AiTaskBase` is the engine's abstraction; this decision uses it as designed, not against it.
- **Cost.** ~1–2 weeks of v1 schedule on architecture work with no v1 gameplay payoff. The "write `AiTaskExplodeOnContact` with no v1 user" pattern is the philosophical core; it must be embraced. Without that pattern, v2 enemies regress to bespoke C# classes and the entire architecture decision is wasted.
- **Cost.** Some up-front design effort to identify universal vs. enemy-specific behaviors. The split will be slightly wrong; refactor when discovered.
- **Open.** No public API stability promise in v1. Behaviors are documented for users but not stable for sub-mods. v2 reconsiders.
- **Open.** No hot-reload of JSON in v1 unless VS supports it cheaply. Tuning loop will be slower than ideal. Not a v1 requirement.
