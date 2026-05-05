using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace TemporalSiege.Config;

public static class ConfigLoader
{
    private const string AssetDomain = "temporalsiege";
    private const string AssetPath = "config/temporalsiege.json";

    /// <summary>
    /// Read config from the mod's bundled asset. Always returns a usable config:
    ///   - asset missing  -> log + defaults
    ///   - JSON malformed -> log error + defaults
    ///   - JSON partial   -> defaults fill the gaps (Newtonsoft preserves
    ///     property defaults set by the C# initializers)
    /// </summary>
    public static TemporalSiegeConfig Load(ICoreAPI api)
    {
        var loc = new AssetLocation(AssetDomain, AssetPath);
        var asset = api.Assets.TryGet(loc);

        if (asset == null)
        {
            api.Logger.Notification("[TemporalSiege] {0} not found, using built-in defaults", loc);
            return new TemporalSiegeConfig();
        }

        try
        {
            var json = asset.ToText();
            var config = JsonConvert.DeserializeObject<TemporalSiegeConfig>(json);
            if (config == null)
            {
                api.Logger.Warning("[TemporalSiege] {0} parsed to null, using built-in defaults", loc);
                return new TemporalSiegeConfig();
            }
            api.Logger.Notification("[TemporalSiege] config loaded from {0}", loc);
            return config;
        }
        catch (JsonException ex)
        {
            api.Logger.Error("[TemporalSiege] {0} is malformed: {1}", loc, ex.Message);
            return new TemporalSiegeConfig();
        }
    }
}
