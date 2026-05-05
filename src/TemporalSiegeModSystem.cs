using TemporalSiege.Config;
using Vintagestory.API.Common;

namespace TemporalSiege;

public class TemporalSiegeModSystem : ModSystem
{
    public TemporalSiegeConfig Config { get; private set; } = new();

    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification("[TemporalSiege] mod loaded ({0} side)", api.Side);
        Config = ConfigLoader.Load(api);
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
