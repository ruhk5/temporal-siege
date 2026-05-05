<h1 align="center">Temporal Siege</h1>

<p align="center"><em>A 7&nbsp;Days&nbsp;to&nbsp;Die-style horde-night loop for Vintage Story.</em></p>

<p align="center">
  <img src="https://img.shields.io/badge/Vintage_Story-1.22.x-c47b3a?style=flat-square" alt="Vintage Story 1.22.x">
  <img src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square" alt=".NET 10">
  <img src="https://img.shields.io/badge/status-pre--alpha-orange?style=flat-square" alt="Status: pre-alpha">
</p>

---

Vintage Story already ships a temporal-storm system: periodic world events with warning UI, audio cues, and atmospheric distortion. **Temporal Siege** turns every one of those storms into a *fight*.

When a storm fires, **temporal rifts** open around a player-placed **Tempering Beacon**. Waves of corrupted entities spawn from the rifts, target the nearest player, and chew through whichever wall offers the path of least resistance. A signature enemy — **the Sundered** — explicitly hunts your base. Survive five waves over twenty to thirty real-time minutes, claim the loot from collapsed rifts, repair, restock, and brace for the next storm in nine to thirteen in-game days.

Vanilla survival craft remains untouched. Knapping, smelting, basebuilding, exploration — all there. The mod is purely additive: a parallel defense progression that runs alongside the vanilla tech tree.

## The loop

```
[Vanilla VS gameplay]
    ↓ (in-game ~9–13 days)
[Temporal-storm warning fires]
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

## Status

**Pre-alpha. Not yet playable.**

This repo currently contains:

- A complete design doc ([`CONTEXT.md`](CONTEXT.md)) and five accepted ADRs ([`docs/adr/`](docs/adr)).
- A 47-issue [v1 build plan](https://github.com/ruhk5/temporal-siege/issues), grouped by phase.
- An empty mod skeleton that loads in Vintage Story 1.22 and logs a hello-world line.

No gameplay is wired up yet. Follow [the issue tracker](https://github.com/ruhk5/temporal-siege/issues) for progress.

## Design pillars

- **Storms become sieges.** Every vanilla temporal storm is a horde event; storm cadence is inherited from the base game ([ADR-0002](docs/adr/0002-temporal-storm-hook-progression-scaling.md)).
- **Intensity scales with progression, not player count.** A day-2 dirt-hut storm is survivable; a steel-age fortified storm is climactic. Co-op adjusts mob density rather than raw count.
- **Weakest-path AI.** Mobs target the nearest player and pathfind through the cheapest wall, the way the 7D2D base-attack AI does it. Material tiering maps directly to defense progression ([ADR-0003](docs/adr/0003-block-destruction-and-weakest-path-ai.md)).
- **Tempering Beacon anchors the fight.** A craftable block declares the storm's location and lets scattered co-op groups converge cleanly. Solo and exploration players are unpunished — the no-beacon fallback is the first-storm tutorial ([ADR-0004](docs/adr/0004-tempering-beacon-mp-storm-targeting.md)).
- **Composable AI behaviors.** v1 ships ~5 reusable behaviors in C#; v2 enemies become pure JSON. Forward-loading the architecture pays off when the roster grows ([ADR-0005](docs/adr/0005-composable-ai-behavior-library.md)).
- **Broad before deep.** v1's 10-item defense menu covers each defensive category once (passive walls, ablative, traps, automated fire, repair tool) before any single category gets a tier-3 deepening.

## Compatibility

| | |
|---|---|
| Game | Vintage Story 1.22.x (latest) |
| Runtime | .NET 10 |
| Side | Server + singleplayer |
| Mod dependencies | None — standalone |
| Existing worlds | Additive — drop-in. Next temporal storm is the no-beacon tutorial. |

> **Required:** vanilla temporal storms must be enabled in your world settings. Temporal Siege hooks them as its trigger; worlds with them disabled are not supported.

## Installation (when v1 ships)

1. Download the latest `temporalsiege-X.Y.Z.zip` from the [Releases](https://github.com/ruhk5/temporal-siege/releases) page.
2. Drop the zip into your Vintage Story `Mods` folder:
   - Windows: `%APPDATA%\VintagestoryData\Mods\`
   - Linux: `~/.config/VintagestoryData/Mods/`
   - macOS: `~/Library/Application Support/VintagestoryData/Mods/`
3. Launch the game. **Temporal Siege** appears in the mod list.

No releases yet — see Status above.

## Building from source

Requirements:

- [Vintage Story 1.22.x](https://www.vintagestory.at/) installed (the build references DLLs from your local install).
- [.NET 10 SDK](https://dotnet.microsoft.com/download).

If your Vintage Story is installed somewhere other than `%APPDATA%\Vintagestory`, set the `VINTAGE_STORY` environment variable to the install path.

```powershell
# Build the mod and symlink it into the VS Mods folder for fast dev iteration.
# Subsequent rebuilds are picked up automatically.
.\scripts\dev-install.ps1

# Or produce a distributable zip at dist\temporalsiege-<version>.zip
.\scripts\package.ps1
```

## Project layout

```
temporal-siege/
├── CONTEXT.md              ← design doc, start here
├── docs/adr/               ← accepted architecture decisions
├── modinfo.json            ← mod metadata
├── src/                    ← C# code (ModSystem, behaviors, block-entities)
├── assets/temporalsiege/   ← blocks, items, entities, recipes, lang, config
└── scripts/                ← build / dev-install / package
```

## Roadmap

The v1 build plan is broken into 11 phases, each tagged with a `phase:N` label on the issue tracker:

| Phase | Theme |
|---|---|
| 0 | Project scaffolding, JSON config loader, item primitives |
| 1 | Per-block damage substrate |
| 2 | Composable AI behavior library |
| 3 | Storm event loop (trigger, waves, end) |
| 4 | Temporal rifts |
| 5 | Stormdrifter retheme + composition |
| 6 | The Sundered — signature custom enemy |
| 7 | Tempering Beacon |
| 8 | 10-item defense menu |
| 9 | HUD + onboarding |
| 10 | Tuning passes (solo, co-op) |

Full scope in [`CONTEXT.md` § v1 scope boundary](CONTEXT.md#v1-scope-boundary). Items deferred to v1.5 and v2 (skybase fixes, ranged enemies, tier-3 walls, auto-reload turrets, themed waves, public mod API, …) are listed in the same section.

## Contributing

Pre-alpha and solo for now. Issues are public if you want to follow along or comment, but implementation work isn't open to outside contributors yet.

## Credits

- Built on top of [Vintage Story](https://www.vintagestory.at/) by Anego Studios.
- Loop concept inspired by [7 Days to Die](https://7daystodie.com/)'s blood-moon model.
