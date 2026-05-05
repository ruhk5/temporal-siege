# ADR-0002: Hook storm trigger to VS temporal storms; scale intensity by progression

**Status:** Accepted
**Date:** 2026-05-05

## Context

The horde event needs a trigger model. Candidates:
- Fixed cadence (every Nth in-game day, à la 7D2D blood-moon)
- Random within a window
- Progression-gated
- Hook into VS's native temporal-storm system
- Hybrid

VS already ships a temporal-storm system: periodic (~9–13 in-game days) world events with native warning UI, audio cues, screen distortion, and lore framing. Drifters are *already* tied to temporal instability in VS lore.

A pure cadence trigger means re-implementing warnings, calendars, and atmosphere VS already provides. A pure progression trigger means without a calendar, players can't plan. Pure random is fine but unguided.

## Decision

The horde event's *when* hooks VS's native temporal-storm trigger. **Every** temporal storm becomes a horde event. The horde event's *intensity* (Minor / Moderate / Major) scales with player progression: tool material, armor tier, days survived, or a composite metric (TBD in tuning).

Specific implications:
- All temporal storms become horde events. There is no separate "horde-only" trigger.
- A vanilla-VS-temporal-storms-disabled world is not supported. README documents the requirement.
- Intensity tier governs wave count, tier ceiling of stormdrifters, and Sundered count.

## Consequences

- **Positive.** Free reuse of VS's calendar, warning UI, audio cues, screen-effect, and lore framing.
- **Positive.** Lore-coherent. Storm-summoned drifters extend existing fiction, not a parallel mechanic.
- **Positive.** Plannable pacing without metronome — VS's native ~9–13 day variance gives "soonish" dread for free.
- **Positive.** Progression scaling resolves the early-game brutality problem from full-block-destruction (ADR-0003). Day-2 storm in a dirt hut spawns 2 waves of Locusts, not 5 waves with a Sundered.
- **Constraint.** Players who turn off temporal storms in vanilla VS settings cannot play this mod meaningfully. README is the only mitigation.
- **Constraint.** Storm cadence is not directly user-configurable in v1 — it inherits VS's temporal-storm settings. Acceptable.
- **Open question (tuning).** Which player metric(s) drive intensity? Defer to alpha tuning. Candidates: most-recent tool material, days survived, base footprint, or composite.
