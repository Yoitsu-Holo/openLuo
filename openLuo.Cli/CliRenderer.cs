using System.Text.Json.Nodes;
using openLuo.Protocol;

namespace openLuo.Cli;

/// <summary>把 wire 输出项渲染为终端文本（CLI 无 Card 渲染语义 → 按契约降级）。</summary>
public static class CliRenderer
{
    public static string Render(OutputDto item) => item.Kind switch
    {
        OutputKind.Text => PayloadText(item),
        OutputKind.Image => $"[image] {PayloadText(item)}",
        OutputKind.Audio => $"[audio] {PayloadText(item)}",
        OutputKind.File => $"[file] {PayloadText(item)}",
        OutputKind.Card => RenderCard(item.Payload),
        OutputKind.Asset => $"[asset] {item.AssetRef?.Id}",
        _ => PayloadText(item),
    };

    private static string PayloadText(OutputDto item)
    {
        if (item.AssetRef is not null)
            return item.AssetRef.Id;

        return item.Payload switch
        {
            null => string.Empty,
            // 字符串载荷直接取值，避免 ToJsonString 把非 ASCII 转义成 \uXXXX
            JsonValue value when value.GetValueKind() == System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
            JsonValue value => value.ToJsonString(),
            JsonNode node => node.ToJsonString(),
        };
    }

    private static string RenderCard(JsonNode? payload)
    {
        if (payload is null)
            return "[card]";

        // 优先可点 Url（分享卡等），否则紧凑 JSON。
        if (payload is JsonObject obj)
        {
            foreach (var key in new[] { "url", "Url", "link" })
            {
                if (obj.TryGetPropertyValue(key, out var url) && url is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    return $"[card] {v.GetValue<string>()}";
            }
        }
        return $"[card] {payload.ToJsonString()}";
    }
}
