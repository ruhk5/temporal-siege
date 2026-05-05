# ADR-0004: Tempering Beacon for multiplayer storm targeting

**Status:** Accepted
**Date:** 2026-05-05

## Context

In MP, players scatter — one mining 600 blocks underground, one exploring a far ruin, one at the base. When a temporal storm fires, the system must decide *where* the horde event happens.

Candidates:
- Per-player rift bubbles around each player's current position
- Single shared origin (host's nearest base, or first-spawned-player)
- Designated site (player-claimed beacon)
- Global all-converge

Per-player punishes scattering. Single-shared has hidden ambiguity (which base is "primary"?). Global all-converge ruins exploration. Designated-site is the only model that gives players agency over the storm's location.

Designated-site introduces a cheese vector: place beacon in a remote sacrificial location, keep the real base far away, log out, "win" the storm by absence.

## Decision

**Tempering Beacon block.** Players craft + place a Tempering Beacon (T2 — 5 temporal shards). The active beacon designates the storm's target location. Rifts open in a ~30–50 block ring around it. Only one beacon per world may be active at a time.

**Activation lock-in.** Once a temporal storm is imminent (~1 in-game hour out), the active beacon cannot be changed. Prevents reactive cheese.

**Validity at activation-time.** Beacon must have ≥3 air blocks above it on the surface plane. Rejected otherwise (prevents underwater / buried-cell cheese).

**Anti-cheese proximity check at storm-start.**
- If at least one online player is within ~50 blocks of the active beacon → beacon stands as siege site.
- If no online player is within range → beacon is consumed (block destroyed, lore: "temporal feedback shatters the abandoned anchor"), and the storm fires using the per-player rift fallback for that single storm.

This produces a two-layer disincentive for cheese: material loss (lost beacon ≈ 5 shards + crafting time) AND unprepared storm (per-player rifts at the player's actual location).

**No-beacon fallback.** Worlds with no active beacon (first storm in a fresh world, post-cheese-failure, or beacon never crafted) use per-player rift bubbles around each online player's spawn point. This is also the *first-storm tutorial* shape — players survive their first storm without a beacon and craft their first one as the survival reward.

**Beacon destructible during storm.** Yes. If the beacon falls mid-storm, ongoing waves continue against existing mobs but no new spawns occur. Functional cool moment with no extra coding (rifts simply lose their anchor and stop spawning).

**Trust model in MP.** Any player can activate/deactivate any beacon. Acceptable for "play with friends" co-op. v2 may add per-server config for trust gating.

## Consequences

- **Positive.** Solves "what is home?" cleanly. The beacon is the explicit declaration.
- **Positive.** Players retain exploration freedom. A player 600 blocks underground when a storm fires is unaffected — they may *want* to come back, but they're not punished for not making it.
- **Positive.** Cheese strictly worse than honest play. Two-layer disincentive.
- **Positive.** Supports "remote killbox" base design. Players can build a separate kill-funnel 30 blocks from their living quarters and stand in it during storms; the 50-block proximity radius is generous enough.
- **Positive.** First-storm tutorial shape falls out for free — same code path as the no-beacon fallback.
- **Cost.** One custom block to author (small art + activation logic + proximity-check tick).
- **Cost.** "Sky beacon" cheese is theoretically possible in v1 (place a beacon high enough that mobs cannot path to its surroundings). Pushed to v2 alongside skybase fixes (ADR-0003).
- **Open.** Trust-based ownership in MP. v2 reconsiders with a config flag.
