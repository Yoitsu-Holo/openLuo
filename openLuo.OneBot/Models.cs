using System.Text.Json;
using System.Text.Json.Nodes;

namespace openLuo.OneBot;

/// <summary>OneBot 11 消息段(发送/接收共用模型:{type,data})。</summary>
public sealed class OneBotSegment
{
    public string Type { get; }
    public JsonObject Data { get; }

    public OneBotSegment(string type, JsonObject? data = null)
    {
        Type = type;
        Data = data ?? [];
    }

    public JsonObject ToJson() => new() { ["type"] = Type, ["data"] = Data };

    public static OneBotSegment Text(string text) => new("text", new JsonObject { ["text"] = text });
    public static OneBotSegment At(long userId) => new("at", new JsonObject { ["qq"] = userId.ToString() });
    public static OneBotSegment AtAll() => new("at", new JsonObject { ["qq"] = "all" });
    public static OneBotSegment Image(string fileOrUrl) => new("image", new JsonObject { ["file"] = fileOrUrl });
    public static OneBotSegment Record(string fileOrUrl) => new("record", new JsonObject { ["file"] = fileOrUrl });
    /// <summary>网易云音乐卡片(LLBot OneBot 原生段)。</summary>
    public static OneBotSegment Music163(long songId) => new("music", new JsonObject
    {
        ["type"] = "163",
        ["id"] = songId
    });

    /// <summary>从接收事件 JSON 段还原(未知段类型降级保留原始 data 供调试)。</summary>
    public static OneBotSegment? FromJson(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return null;
        var type = obj["type"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type))
            return null;
        var data = obj["data"] as JsonObject ?? new JsonObject();
        return new OneBotSegment(type, data);
    }
}

/// <summary>接收到的消息事件(message 事件;私聊与群聊统一)。</summary>
public sealed class OneBotMessageEvent
{
    public long Time { get; init; }
    public long SelfId { get; init; }
    /// <summary>"private" | "group"</summary>
    public string MessageType { get; init; } = string.Empty;
    /// <summary>消息事件细分(群聊: normal/anonymous/notice;私聊: friend/group/other)</summary>
    public string SubType { get; init; } = string.Empty;
    public long UserId { get; init; }
    public long? GroupId { get; init; }
    public string RawMessage { get; init; } = string.Empty;
    public IReadOnlyList<OneBotSegment> Segments { get; init; } = [];
    /// <summary>发送者显示名:群聊取 card&gt;nickname,私聊取 nickname&gt;remark。</summary>
    public string? SenderDisplayName { get; init; }
    public long MessageId { get; init; }

    /// <summary>文本化:文本原样、@他人→@名字、图片→[image]、语音→[voice]、文件→[file](与平台占位约定一致)。</summary>
    public string ToPlainText(long? botUserId = null)
    {
        var parts = new List<string>();
        foreach (var segment in Segments)
        {
            switch (segment.Type)
            {
                case "text" when segment.Data["text"]?.GetValue<string>() is { } text:
                    parts.Add(text);
                    break;
                case "at" when segment.Data["qq"]?.GetValue<string>() is { } qq && qq != "all" && qq != (botUserId?.ToString() ?? string.Empty):
                    parts.Add($"@{segment.Data["name"]?.GetValue<string>() ?? qq}");
                    break;
                case "image":
                    parts.Add("[image]");
                    break;
                case "record":
                    parts.Add("[voice]");
                    break;
                case "file":
                    parts.Add("[file]");
                    break;
            }
        }
        return string.Join(' ', parts).Trim();
    }

    /// <summary>群聊消息是否 @ 了指定 bot。</summary>
    public bool Mentions(long botUserId) =>
        Segments.Any(s => s.Type == "at"
            && s.Data["qq"]?.GetValue<string>() == botUserId.ToString());

    /// <summary>图片段下载地址(url 优先,退 file)。</summary>
    public IReadOnlyList<string> ImageUrls()
    {
        var urls = new List<string>();
        foreach (var segment in Segments.Where(s => s.Type == "image"))
        {
            var url = segment.Data["url"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(url))
                url = segment.Data["file"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(url))
                urls.Add(url);
        }
        return urls;
    }

    public static OneBotMessageEvent? FromJson(JsonNode? root)
    {
        if (root is not JsonObject obj || obj["post_type"]?.GetValue<string>() != "message")
            return null;
        var segments = new List<OneBotSegment>();
        if (obj["message"] is JsonArray messageArray)
        {
            foreach (var node in messageArray)
            {
                if (OneBotSegment.FromJson(node) is { } segment)
                    segments.Add(segment);
            }
        }
        else if (obj["message"] is JsonValue messageValue)   // 兼容字符串 message(纯文本)
        {
            segments.Add(OneBotSegment.Text(messageValue.GetValue<string>()));
        }

        string? SenderNameOf(JsonObject? sender)
        {
            if (sender is null)
                return null;
            var card = sender["card"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(card))
                return card;
            var nickname = sender["nickname"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(nickname) ? null : nickname;
        }

        return new OneBotMessageEvent
        {
            Time = obj["time"]?.GetValue<long>() ?? 0,
            SelfId = obj["self_id"]?.GetValue<long>() ?? 0,
            MessageType = obj["message_type"]?.GetValue<string>() ?? string.Empty,
            SubType = obj["sub_type"]?.GetValue<string>() ?? string.Empty,
            UserId = obj["user_id"]?.GetValue<long>() ?? 0,
            GroupId = obj["group_id"]?.GetValue<long>(),
            RawMessage = obj["raw_message"]?.GetValue<string>() ?? string.Empty,
            Segments = segments,
            SenderDisplayName = SenderNameOf(obj["sender"] as JsonObject),
            MessageId = obj["message_id"]?.GetValue<long>() ?? 0
        };
    }
}

/// <summary>action 响应(status/retcode 业务语义)。</summary>
public sealed class OneBotSendResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public long? MessageId { get; init; }

    public static OneBotSendResult FromJson(JsonObject? root)
    {
        if (root is null)
            return new OneBotSendResult { Ok = false, Error = "empty response" };
        var status = root["status"]?.GetValue<string>() ?? string.Empty;
        var retcode = root["retcode"]?.GetValue<int>() ?? -1;
        var ok = status is "ok" || retcode == 0;
        long? messageId = null;
        if (root["data"] is JsonObject data && data["message_id"] is JsonValue mid)
            messageId = mid.GetValue<long>();
        return new OneBotSendResult
        {
            Ok = ok,
            MessageId = messageId,
            Error = ok ? null : $"{status}/{retcode}: {root["message"]?.GetValue<string>() ?? string.Empty}"
        };
    }
}
