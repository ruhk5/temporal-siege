# Project context

## What this is

A Vintage Story (VS) mod that adds a 7 Days to Die-style horde-night loop on top of VS's survival craft gameplay. Players play vanilla VS — knapping, smelting, basebuilding — but every ~9–13 in-game days the world's native temporal storms become active sieges: visible rifts open around a player-placed beacon, waves of corrupted entities spawn from them and converge on the players' position, breaking through walls along the weakest path. Players must repair, restock, and prepare between storms.

The mod ships under the modid `temporalsiege` (repo: `temporal-siege`). The project is a mod, not a from-scratch engine. See ADR-0001.

## Project shape

- **Vintage Story mod**, loaded into VS via the standard mod system. (ADR-0001)
- **Target**: VS 1.22.x latest only. Standalone — no third-party mod dependencies. Compatible with existing worlds (additive — drop the zip into `Mods/`, the next temporal storm is the no-beacon tutorial).
- **Players**: small co-op (1–4 players, host-and-play). Solo falls out as a special case.
- **Realistic v1 timeline (estimate)**: 6–9 months of focused solo work.

## Core loop

```
[Vanilla VS gameplay]
    ↓ (in-game ~9–13 days)
[Temporal-storm warning fires — VS's native warning + custom "anchor your beacon" hint]
    ↓
[Players converge on beacon, top off defenses]
    ↓
[Storm: 5 waves over ~20–30 min, mobs from rifts breach walls toward players]
    ↓ ↑ (between waves: 30–45s lull → repair, reload, reposition)
[Wave 5 climax → "storm subsiding" → straggler cleanup phase ~5 min]
    ↓
[Rebuild, restock, claim rift loot, prep for next storm]
    ↓
[Loop]
```

## Glossary

Use these terms as defined here. Don't drift to synonyms.

- **Stormdrifter** — A vanilla VS drifter visually rethemed (red glow shader, particle aura, modified audio) and behaviorally augmented (weakest-path AI, block-attack) during storms only. Same model and tier system as vanilla drifters (Locust → Surface → Deep → Tainted → Corrupt → Nightmare). Outside storms, drifters revert to vanilla behavior.

- **The Sundered** — v1's signature custom enemy. Slow, tanky, base-targeting (the only enemy in v1 that explicitly seeks structures regardless of player position). Spawns in late waves of major storms. Drops *aberrant core* on death.

- **Temporal rift** — A visible spawn portal that opens at storm-start. 3–5 rifts open in a ~30–50 block ring around the active Tempering Beacon (or, on first/no-beacon storm, around each online player). Each rift emits a vertical pillar of light + audio loop. Rifts spawn mobs throughout the storm. Rifts can be attacked and closed by players (very tanky, ~30–60s focused attack), reducing spawn pressure from that direction. New rifts open between waves.

- **Tempering Beacon** — A craftable block (T2 — 5 temporal shards) that designates the storm's target location. The active beacon defines where rifts spawn and where the horde converges. Only one beacon may be active per world. Activation locks once a temporal storm is imminent (~1 in-game hour out). Subject to a 50-block proximity check at storm-start (ADR-0004).

- **Temporal shard** — Common storm-loot drop (stormdrifters, closed rifts, storm-end completion bonus). Used in T2 defense crafting and as fuel for the *repair hammer* (1 shard per wall-repair strike). Recurring economy: shards never become obsolete because hammer-repair burns shards forever.

- **Aberrant core** — Rare drop. Sundered drops one guaranteed; Nightmare stormdrifters drop on a low chance. Used in T3 defense crafting (manual-reload turret). Ascending economy: cores unlock new defenses, not maintenance.

- **Storm intensity tier** — Minor / Moderate / Major. Scales with player progression (specific metric — TBD in tuning). Determines wave count, tier ceiling of stormdrifters, and Sundered count.

- **Wave** — A discrete pulse of mob spawns inside a storm. v1 uses up to 5 waves per major storm, separated by 20–45s lulls. Wave 5 is always the climactic boss wave.

- **Skybase** — Player exploit where a base is built high enough above terrain that mobs cannot path to it. v1 tolerates this (ADR-0003). v2 will close.

## Players

- Solo or small co-op (1–4 players, host-and-play).
- Storm intensity scales with progression, not raw player count (count adjusts mob density via configuration).
- Deaths follow vanilla VS rules: drop full inventory at death point, respawn at sleeping bag (ADR not needed — vanilla VS behavior). Convention: place a bedroll inside your base. First-storm chat hint surfaces this.
- Players cluster around the active beacon during storms. Players >50 blocks from the beacon at storm-start are not "claimed" by it (ADR-0004).
- No PvP design considerations in v1.

## The storm event

### Trigger
- Hooks VS's native temporal-storm cadence (~weekly in-game). All temporal storms become horde events. (ADR-0002)
- Storm intensity scales with player progression.
- Vanilla-VS-temporal-storm-disabled worlds: not supported. Documented in README.

### Duration & shape
- One storm = ~20–30 min real-time, matching VS's native temporal-storm window.
- Major storm: 5 waves separated by 20–45s lulls. Mob count and tier composition both escalate (hybrid escalation — shifting mix + growing count).
- Final wave is always the climax: Corrupt + Nightmare drifters + 2 Sundered.
- Lower-tier storms truncate (2–4 waves, lower tier ceiling, 0–1 Sundered).

### Wave composition matrix (major storm baseline)

Counts are tuning placeholders. Cap simultaneous on-field mobs at ~22 to stay within VS's entity-sim comfort zone.

| Wave | Tier mix | Approx count (4-player) | Role |
|------|----------|------------------------|------|
| 1 | Locust + Surface | 8 | Pressure-test perimeter |
| 2 | Locust + Surface + Deep | 14 | Establish density |
| 3 | Surface + Deep + Tainted | 18 | Composition shift |
| 4 | Deep + Tainted + Corrupt + 1 Sundered | 22 | Peak swarm, first Sundered |
| 5 | Corrupt + Nightmare + 2 Sundered | 16 elites | Boss wave |

### Spawn (rifts)
- 3–5 rifts open at storm-start in a ~30–50 block ring around the active beacon (or per-player on the no-beacon fallback).
- 1–2 new rifts open between subsequent waves at fresh compass points.
- Rifts placed only on natural terrain ≥3 air blocks above ground; rejected if buried/underwater.
- Rifts are very tanky. Closing one alone takes ~30–60s of focused attack and is dangerous (mob density at the rift).
- Closed rift drops 3–5 shards + chance of a bonus item.
- At storm-end, all open rifts collapse over ~30s and drop a smaller loot pile (1–2 shards) that persists ~24 in-game hours.

### Pathing & targeting
- Player-targeting weakest-path A* (ADR-0003).
- No structural-integrity simulation in v1.
- The Sundered is an exception — explicit base-targeting AI override on a single enemy.

### Storm-end (soft cutoff)
1. Wave 5 ends → "storm subsiding" announcement + audio cue.
2. No new spawns; existing mobs continue current AI tasks.
3. Rifts collapse visually over ~30s.
4. Active straggler phase ~5 min: players hunt remaining mobs.
5. Despawn sweep at ~5 min: mobs >100 blocks from any online player despawn quietly. Mobs near players persist.
6. Beacon's "siege lock" releases. Player can deactivate/relocate before next storm.

### Failure state
None. Storms are events, not matches. Players cannot "lose" a storm. Wipes respawn at bedroll, storm continues. Base damage is the implicit failure narrative.

## Enemies

### v1 roster

| Enemy | Source | AI behaviors (composed) | Role |
|---|---|---|---|
| Stormdrifter (Locust) | Retexture vanilla | TargetNearestPlayer + AttackBlocksWeakestPath + MeleeAttack | Filler, waves 1–3 |
| Stormdrifter (Surface) | Retexture vanilla | Same | Filler, waves 1–4 |
| Stormdrifter (Deep) | Retexture vanilla | Same | Mid-tier, waves 2–5 |
| Stormdrifter (Tainted) | Retexture vanilla | Same, higher dmg | Mid-tier, waves 3–5 |
| Stormdrifter (Corrupt) | Retexture vanilla | Same, higher dmg | High-tier, waves 4–5 |
| Stormdrifter (Nightmare) | Retexture vanilla | Same, highest dmg | Climax, wave 5 |
| The Sundered | Custom entity | TargetNearestPlayer (overridden to base-target) + ChargeAtTarget + AttackBlocksWeakestPath | Signature. Wave 4–5. Drops aberrant core. |

### Deferred enemies
See `memory/defense_content_deferred.md` indirectly (defense list); enemy-specific deferred list:
- Suicide-bomber drifter (already-built `AiTaskExplodeOnContact` + JSON + art only — see ADR-0005)
- Ranged spitter
- Stealth / teleport scout
- Summoner / spawner
- Themed wave bosses

## Defenses

### v1 menu (10 items)

| # | Item | Tier | Refurb model | Role |
|---|---|---|---|---|
| 1 | Reinforced wood wall | T1 (vanilla) | HP, hammer-repair | Starter fortification |
| 2 | Stone-banded wall | T2 (shards) | HP, hammer-repair | First "real" defense |
| 3 | Reinforced gate | T2 (shards) | HP, hammer-repair | Choke point |
| 4 | Sharpened-stake palisade | T1 (vanilla) | HP, replace | Ablative outer layer + chip damage |
| 5 | Spike pit | T1 (vanilla) | Spike condition (10 kills) | Funnel kill primitive |
| 6 | Dart launcher | T2 (shards) | 30-bolt magazine | Ranged trap |
| 7 | Oil pour / pitch trough | T2 (shards) | Single-use per pour | Anti-swarm AOE |
| 8 | Kill-channel grate | T1 (vanilla) | HP (high) | Forced-funnel geometry |
| 9 | Manual-reload turret | T3 (shards + cores) | 20-bolt magazine + HP | Player-loaded automated fire |
| 10 | Repair hammer | T2 (shards) | 1 shard / strike | Wall repair tool |

Active vs. passive distinction:
- Active (5–7, 9): consume ammo / fuel; deplete with use.
- Passive (1–4, 8): HP-based, break under enemy attack.
- Hybrid (9): both.

Auto-fire turrets, electric fences, alarm bells, lures, tier-3 walls, and most awareness/manipulation tools are deferred to v1.5/v2. See `memory/defense_content_deferred.md` for the full list and gating dependencies.

## Economy

- **Temporal shards** — recurring. Earned from stormdrifters, closed rifts, storm-end completion. Spent on T2 defenses, hammer repairs, beacon activation. Stays valuable indefinitely because hammer-repair consumes shards on every wall fix.
- **Aberrant cores** — ascending. Earned from Sundered (guaranteed) and Nightmare drifters (chance). Spent on T3 defense unlocks. Not used in routine maintenance.
- **Vanilla VS materials** — for T1 defenses, palisade replacement, spike pit refurbishment, oil pour pitch.

The mod's defensive tech tree is *parallel to* vanilla VS progression. Players who never engage storms can still reach steel-age vanilla content. Storm-tree top-tier defenses cannot be reached without storm engagement.

## Architecture

- **Composable AI behaviors** in C#, configured via JSON. v1 ships ~5 reusable behaviors. Suicide-bomber drifter and other v2 enemies become pure JSON. (ADR-0005)
- **Data-driven content**:
  - Wave composition matrix → JSON
  - Rift placement parameters → JSON
  - Storm intensity scaling formulas → JSON
  - Beacon proximity radius → JSON
  - Enemy parameters and behavior compositions → entity JSON
  - Drop tables → vanilla VS drop-table system
- **Bespoke (per-item) implementations** for active trap block-entities (Spike, Dart, Oil, Turret). Refactor only when v1.5 adds a similar trap that shares logic. Single-instance = bespoke is correct.
- **Server vs. client split**: mostly server-side (storm logic, AI, world events). Small client-side component for HUD elements.
- **No public API for sub-mods in v1.** Behaviors are documented but not stable.

## Compatibility

- VS 1.22.x latest only. New VS major versions = new mod branch, not runtime compat.
- Standalone mod. No third-party dependencies.
- Existing-world additive.
- Save-file forward compat within the v1.x line. v1.x → v2.0 may break saves.

## v1 scope boundary

### In v1
- Storm event system (trigger, wave structure, lulls, end)
- Tempering Beacon (block + activation + proximity check + lock-in + collapse)
- Visible temporal rifts (entity, spawn logic, damage state, close mechanic)
- Stormdrifter retheme (palette, particle, audio)
- Sundered enemy (custom entity + AI)
- 10-item defense menu
- Temporal shard + aberrant core item drops + drop tables
- Light HUD (wave counter, storm timer, beacon HP, transition flashes)
- Vanilla VS death rules + bedroll convention
- 5 reusable AI behaviors (forward-loaded for v2)
- JSON config system for tuning

### Deferred to v1.5
- Alarm bell / scouting items
- Ranged enemy (spitter)
- Tier-3 walls (steel-clad)
- Auto-reload turret variants
- Bear traps / leg-snare
- Localization beyond English
- First-storm dedicated tutorial UI

### Deferred to v2
- Skybase fix (rage-mode tunneling / max-elevation rule / vertical pathing — TBD)
- Structural-integrity sim (if needed)
- Suicide-bomber drifter (`AiTaskExplodeOnContact` is pre-built; needs JSON + art only)
- Stealth / teleport / summoner enemies
- Themed waves
- Failure-state hooks (consequences for severe damage)
- Electric fence (mechanical-power integration)
- Sound lure / decoy block (aggro override system)
- Public mod API for sub-mods
- Dedicated-server-scale targeting (16+ players, persistent worlds)
- "World remembers" cosmetic residue
- Compass/minimap rift markers
- Base damage HUD aggregate

## Decision pointers

| Decision | ADR |
|---|---|
| Build as VS mod, not engine clone | ADR-0001 |
| Hook storm trigger to VS's temporal storms; scale intensity by progression | ADR-0002 |
| Full block destruction; weakest-path player-targeting AI; no structural-integrity sim | ADR-0003 |
| Tempering Beacon for MP storm targeting + anti-cheese proximity check | ADR-0004 |
| Composable AI behavior library (not bespoke classes) | ADR-0005 |

## Open tuning questions (not design — playtest)

These were intentionally left out of design:

- Specific HP / damage / break-time numbers per material × enemy tier.
- Sundered's exact behavior shape (charge windup time, target-selection logic when multiple base structures present).
- Storm intensity scaling formula (which player metric drives intensity — most-recent tool material? days survived? base footprint? composite?).
- Save/load behavior of in-flight storms. Default: cancel cleanly on save, fire fresh on next temporal trigger.
- Server admin commands (force-storm, skip-storm, set-intensity for testing).
- First-storm onboarding tutorial scope (chat hint vs. dedicated UI — punted to v1.5).
