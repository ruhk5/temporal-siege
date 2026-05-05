# ADR-0003: Full block destruction with weakest-path player-targeting AI

**Status:** Accepted
**Date:** 2026-05-05

## Context

The horde-defense fantasy ("build a sturdy base, ward off attackers, rebuild between storms") only works if enemies can damage player-built structures meaningfully and if the AI's pathing choices make base design *strategic* rather than incidental.

Two coupled questions:
1. Can mobs destroy player blocks? (None / weak-only / tiered / full)
2. How do mobs target and path? (Player-target shortest / player-target weakest-path / base-target / hybrid split-roster)

VS does not natively support mobs breaking placed blocks — drifters cannot chew through a dirt wall in vanilla. This is custom mod work.

## Decision

**Block destruction:** Full. Any block can be damaged and broken. Per-block damage tracker stored as a chunk-attached side-data store (`Dictionary<BlockPos, float>`), not a modification of VS's block storage. When damage ≥ block.Resistance, replace with air or a "broken_<material>" decoration block.

**Pathing/targeting:** Player-targeting weakest-path A* (7D2D model). Mobs target the nearest player; pathfind via A* with edge weights = block break-time × hardness. No separate base-targeting AI as a system rule.

**The Sundered is an exception:** explicit base-targeting AI override on a single enemy. Treats base-targeting as a *single-enemy exception*, not a system rule.

**No structural-integrity simulation in v1.** Skybase exploits are tolerated. Deferred to v2.

## Consequences

- **Positive.** Real defensive depth. Material tiering (thatch → wood → stone → metal) maps directly to base-defense progression. Repair matters. Rebuild matters.
- **Positive.** Weakest-path produces the iconic 7D2D base-design pattern (kill-channels, sacrificial corridors, funnels). Without it, optimal base = "thick wall, end of story."
- **Positive.** Side-data block-damage store keeps the implementation compatible with vanilla VS and other mods that touch blocks.
- **Positive.** Player-targeting + Sundered-as-exception gets the "horde destroys my base" experience without needing system-wide base-targeting AI. When players are inside the base, mobs grind through walls to reach them — visually equivalent to base-targeting from the player's POV.
- **Positive.** Co-op drama emerges naturally — players can leave the base mid-storm to draw mobs away from a less-prepared teammate. The horde follows because the AI tracks players, not structures.
- **Cost.** A* with edge-weighted breakable-blocks is more expensive than binary passable/impassable A*. Mitigation: cache paths per-mob, recompute every N seconds or when a block in the path changes. Don't aim for perfect realtime; aim for believable.
- **Cost.** Early-game brutality. First storm in dirt hut may wipe. Mitigated by progression scaling (ADR-0002).
- **Deferred.** Skybase exploit. Players will discover within a week of release. Document the limitation; v2 closes it via rage-mode tunneling, max-elevation spawn rule, or another mechanism — TBD in v2 design.
- **Deferred.** "Wake up to destroyed remote outpost" fantasy. Player-targeting means logged-off / far-away bases are immortal between sessions. Acceptable trade for active-siege fantasy.
