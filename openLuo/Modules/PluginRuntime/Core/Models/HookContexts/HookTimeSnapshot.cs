using System.Text.Json.Serialization;

namespace openLuo.Modules.PluginRuntime.Core.Models.HookContexts;

public sealed class HookTimeSnapshot
{
    [JsonPropertyName("day")]
    public int Day { get; init; }

    [JsonPropertyName("minute")]
    public int Minute { get; init; }

    [JsonPropertyName("timeStr")]
    public string TimeStr { get; init; } = "00:00";

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "virtual";

    [JsonPropertyName("epochMs")]
    public long EpochMs { get; init; }
}
