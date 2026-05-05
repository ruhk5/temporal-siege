using Vintagestory.API.Common;

namespace TemporalSiege;

public class TemporalSiegeModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification("[TemporalSiege] mod loaded ({0} side)", api.Side);
    }
}
