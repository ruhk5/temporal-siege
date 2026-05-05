using Vintagestory.API.Common;

namespace VoxelEngine;

public class VoxelEngineModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification("[VoxelEngine] mod loaded ({0} side)", api.Side);
    }
}
