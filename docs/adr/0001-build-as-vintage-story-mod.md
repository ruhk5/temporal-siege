# ADR-0001: Build as a Vintage Story mod, not an engine clone

**Status:** Accepted
**Date:** 2026-05-05

## Context

The original concept could be interpreted three ways: (A) a Vintage Story mod, (B) a from-scratch C# engine deliberately mimicking VS's API surface, or (C) a from-scratch VS-inspired voxel game with an original engine and original mod API. The repo is named `voxel-engine`, which suggests temptation toward (B) or (C).

(B) and (C) are multi-year solo projects that compete with VS's 10+ years of accumulated engine work. (B) additionally carries legal-grey-area risk if VS's public API surface is mirrored too closely. The actual design innovation in this concept is the *gameplay loop* (horde nights bolted onto VS-style survival craft), not the *engine*.

## Decision

Build as a Vintage Story mod. The mod loads inside VS, uses VS's native APIs (`Vintagestory.API.*`), and ships as a standard mod zip via the VS mod portal.

## Consequences

- **Positive.** Engine work is zero. The horde event, AI, defenses, and storm system are all gameplay code on top of a working voxel engine. Realistic ship horizon: 6–9 months.
- **Positive.** Free reuse of VS's drifter system, temporal-storm framework, block/entity JSON system, drop-table system, mechanical-power system (post-v1), worldgen, and audio/visual atmosphere.
- **Positive.** No legal concerns from API mimicry.
- **Constraint.** Players must own VS to play the mod. Distribution via VS mod portal.
- **Constraint.** Locked into VS's engine performance characteristics, networking model, and update cadence. When VS ships breaking changes, the mod ports.
- **Forecast.** If the loop proves fun and constraints from VS become limiting, *then* a from-scratch engine becomes a defensible v3+ undertaking — informed by lessons from shipping the mod. Don't start with the engine.
