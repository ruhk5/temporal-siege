using TemporalSiege.AI;
using TemporalSiege.Config;
using TemporalSiege.Damage;
using TemporalSiege.Rifts;
using TemporalSiege.Storms;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalSiege;

public class TemporalSiegeModSystem : ModSystem
{
    public TemporalSiegeConfig Config { get; private set; } = new();
    public BlockDamageStore? BlockDamage { get; private set; }
    public StormCoordinator? Storms { get; private set; }
    public RiftSystem? Rifts { get; private set; }

    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification("[TemporalSiege] mod loaded ({0} side)", api.Side);

        // Custom entity classes must be registered on both sides so the client
        // can deserialize entity sync packets coming from the server. Must be
        // done in Start (before AssetsLoaded) so the entity JSON can resolve
        // its "class": "Rift" reference during asset processing.
        api.RegisterEntity("Rift", typeof(EntityRift));
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Asset reads must happen at AssetsLoaded or later. Loading in Start
        // fails on the client with a "Mods must not get assets before AssetsLoaded"
        // exception, which previously slipped through because nothing in
        // Phase 0–3 tripped a client-side codepath.
        Config = ConfigLoader.Load(api);
    }

    public override void StartServerSide(ICoreServerAPI sapi)
    {
        BlockDamage = new BlockDamageStore(sapi);
        BlockDamageDebugCommands.Register(sapi, BlockDamage);

        // AI behavior library (ADR-0005). AI runs server-side only in v1.
        // Fully-qualify our AiTaskMeleeAttack to disambiguate from vanilla VS's same-named class.
        AiTaskRegistry.Register<AiTaskTargetNearestPlayer>("temporalsiege:targetnearestplayer");
        AiTaskRegistry.Register<TemporalSiege.AI.AiTaskMeleeAttack>("temporalsiege:meleeattack");
        AiTaskRegistry.Register<AiTaskExplodeOnContact>("temporalsiege:explodeoncontact");
        AiTaskRegistry.Register<AiTaskChargeAtTarget>("temporalsiege:chargeattarget");
        AiTaskRegistry.Register<AiTaskAttackBlocksWeakestPath>("temporalsiege:attackblocksweakestpath");
        AiTaskRegistry.Register<AiTaskHordePursue>("temporalsiege:hordepursue");

        // Storm event loop (Phase 3).
        Storms = new StormCoordinator(sapi, Config);
        StormDebugCommands.Register(sapi, Storms);

        // Rift lifecycle (Phase 4).
        Rifts = new RiftSystem(sapi, Config, Storms);
        RiftDebugCommands.Register(sapi, Rifts);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        foreach (var code in new[] { "temporalshard", "aberrantcore" })
        {
            var item = api.World.GetItem(new AssetLocation("temporalsiege", code));
            if (item == null)
                api.Logger.Warning("[TemporalSiege] item temporalsiege:{0} did not register", code);
            else
                api.Logger.Notification("[TemporalSiege] item registered: {0}", item.Code);
        }

        // Confirm the rift entity loaded from JSON.
        var riftType = api.World.GetEntityType(new AssetLocation("temporalsiege", "rift"));
        if (riftType == null)
            api.Logger.Warning("[TemporalSiege] entity temporalsiege:rift did not register");
        else
            api.Logger.Notification("[TemporalSiege] entity registered: {0} (class={1})", riftType.Code, riftType.Class);

        // Stormdrifter variants (Phase 5).
        foreach (var tier in new[] { "locust", "surface", "deep", "tainted", "corrupt", "nightmare" })
        {
            var code = new AssetLocation("temporalsiege", $"stormdrifter-{tier}");
            var t = api.World.GetEntityType(code);
            if (t == null)
                api.Logger.Warning("[TemporalSiege] entity {0} did not register", code);
            else
                api.Logger.Notification("[TemporalSiege] entity registered: {0} (class={1})", t.Code, t.Class);
        }
    }
}
