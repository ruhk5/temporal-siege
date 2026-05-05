namespace TemporalSiege.Config;

/// <summary>
/// Root config tree. All tunables that don't belong in entity JSON live here
/// (per ADR-0005, behavior parameters live in entity JSON, not this file).
/// </summary>
public class TemporalSiegeConfig
{
    public WaveConfig Waves { get; set; } = new();
    public RiftConfig Rifts { get; set; } = new();
    public StormConfig Storm { get; set; } = new();
    public BeaconConfig Beacon { get; set; } = new();
}

public class WaveConfig
{
    /// <summary>
    /// Wave composition keyed by intensity tier ("minor", "moderate", "major").
    /// Defaults seed only the major matrix from CONTEXT.md so the mod is
    /// playable-ish even if the JSON file is missing or malformed.
    /// </summary>
    public Dictionary<string, IntensityWaves> ByIntensity { get; set; } = new()
    {
        ["minor"] = new IntensityWaves
        {
            Waves = new List<WaveSpec>
            {
                new() { TotalCount = 6, LullSecondsAfter = 30, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-locust", Weight = 1 } } },
                new() { TotalCount = 8, LullSecondsAfter = 0,  Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-locust", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 } } }
            }
        },
        ["moderate"] = new IntensityWaves
        {
            Waves = new List<WaveSpec>
            {
                new() { TotalCount = 8,  LullSecondsAfter = 30, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-locust", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 } } },
                new() { TotalCount = 14, LullSecondsAfter = 30, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-deep", Weight = 1 } } },
                new() { TotalCount = 16, LullSecondsAfter = 0,  Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-deep", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-tainted", Weight = 1 } } }
            }
        },
        ["major"] = new IntensityWaves
        {
            Waves = new List<WaveSpec>
            {
                new() { TotalCount = 8,  LullSecondsAfter = 30, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-locust", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 } } },
                new() { TotalCount = 14, LullSecondsAfter = 35, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-locust", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-deep", Weight = 1 } } },
                new() { TotalCount = 18, LullSecondsAfter = 35, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-surface", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-deep", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-tainted", Weight = 1 } } },
                new() { TotalCount = 22, LullSecondsAfter = 40, Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-deep", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-tainted", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-corrupt", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:sundered", FixedCount = 1 } } },
                new() { TotalCount = 16, LullSecondsAfter = 0,  Mobs = { new MobSpawn { EntityCode = "temporalsiege:stormdrifter-corrupt", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:stormdrifter-nightmare", Weight = 1 }, new MobSpawn { EntityCode = "temporalsiege:sundered", FixedCount = 2 } } }
            }
        }
    };

    /// <summary>VS entity-sim soft-cap. Spawner stops emitting once on-field mob count hits this.</summary>
    public int MaxConcurrentMobs { get; set; } = 22;
}

public class IntensityWaves
{
    public List<WaveSpec> Waves { get; set; } = new();
}

public class WaveSpec
{
    public List<MobSpawn> Mobs { get; set; } = new();

    /// <summary>Approximate total mob count in this wave (4-player baseline). Density-scaled at runtime by online-player count.</summary>
    public int TotalCount { get; set; } = 8;

    /// <summary>Lull duration after this wave ends, in seconds. Last wave should set 0.</summary>
    public float LullSecondsAfter { get; set; } = 30f;
}

public class MobSpawn
{
    /// <summary>Entity code (e.g. "temporalsiege:stormdrifter-locust"). Resolved against the entity registry at spawn time.</summary>
    public string EntityCode { get; set; } = "";

    /// <summary>Relative weight when filling out the wave's TotalCount. Ignored when FixedCount > 0.</summary>
    public float Weight { get; set; } = 1f;

    /// <summary>Fixed spawn count (e.g. "exactly 1 Sundered in wave 4"). Counts against TotalCount; if nonzero, Weight is ignored.</summary>
    public int FixedCount { get; set; } = 0;
}

public class RiftConfig
{
    public int MinRiftsPerStorm { get; set; } = 3;
    public int MaxRiftsPerStorm { get; set; } = 5;
    public float MinRingRadius { get; set; } = 30f;
    public float MaxRingRadius { get; set; } = 50f;

    /// <summary>Minimum air blocks above the candidate position before a rift can spawn there (anti-buried/underwater).</summary>
    public int MinAirBlocksAbove { get; set; } = 3;

    /// <summary>Fresh rifts that open at the start of each between-wave lull.</summary>
    public int FreshRiftsPerLullMin { get; set; } = 1;
    public int FreshRiftsPerLullMax { get; set; } = 2;

    /// <summary>HP of a fresh rift. Closing one alone is meant to take ~30–60s of focused attack.</summary>
    public float RiftHp { get; set; } = 600f;

    /// <summary>Persistent loot pile lifetime after storm-end, in in-game hours.</summary>
    public float StormEndLootLifetimeGameHours { get; set; } = 24f;
}

public class StormConfig
{
    /// <summary>
    /// Player metric that drives intensity tier. v1 starts with a placeholder;
    /// final formula is TBD-in-tuning per ADR-0002 / CONTEXT.md.
    /// Candidates: "tool_material", "days_survived", "base_footprint", "composite".
    /// </summary>
    public string IntensityMetric { get; set; } = "tool_material";

    /// <summary>Score thresholds for promotion to the next intensity tier. Compared against the metric above.</summary>
    public float MinorThreshold { get; set; } = 0f;
    public float ModerateThreshold { get; set; } = 3f;
    public float MajorThreshold { get; set; } = 6f;

    /// <summary>Distance from any online player at which a mob is despawned during the storm-end straggler-cleanup sweep.</summary>
    public float StragglerDespawnDistanceBlocks { get; set; } = 100f;

    /// <summary>Length of the active straggler-hunt phase before the despawn sweep, in real-time minutes.</summary>
    public float StragglerPhaseMinutes { get; set; } = 5f;
}

public class BeaconConfig
{
    /// <summary>Storm-start proximity check: at least one online player must be within this many blocks of the active beacon.</summary>
    public float ProximityRadiusBlocks { get; set; } = 50f;

    /// <summary>Beacon activation locks in this many in-game hours before storm-start (anti-reactive-cheese).</summary>
    public float LockInWindowGameHours { get; set; } = 1f;

    /// <summary>Activation validity check: at least this many air blocks above the beacon on the surface plane.</summary>
    public int MinAirBlocksAbove { get; set; } = 3;

    /// <summary>Beacon HP. Falls under attack mid-storm severs rift spawning per ADR-0004.</summary>
    public float BeaconHp { get; set; } = 1200f;
}
