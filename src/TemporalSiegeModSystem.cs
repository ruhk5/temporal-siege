using TemporalSiege.AI;
using TemporalSiege.Config;
using TemporalSiege.Damage;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace TemporalSiege;

public class TemporalSiegeModSystem : ModSystem
{
    public TemporalSiegeConfig Config { get; private set; } = new();
    public BlockDamageStore? BlockDamage { get; private set; }

    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification("[TemporalSiege] mod loaded ({0} side)", api.Side);
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
    }
}
