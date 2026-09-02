namespace openLuo.Capabilities.Core.Models;

/// <summary>公共输出项类型。</summary>
public enum ReplyItemKind
{
    Text,
    Image,
    Audio,
    File,
    Card,
    Asset
}

/// <summary>
/// 公共输出项（D3-D6）。Sequence 会话内单调递增；Fingerprint 用于当前 Turn 去重。
/// </summary>
public sealed record OutputItem
{
    public string Id { get; init; } = string.Empty;
    public ReplyItemKind Kind { get; init; } = ReplyItemKind.Text;
    public object Payload { get; init; } = string.Empty;
    public string SourceCapability { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Fingerprint { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public string? ConversationId { get; init; }
}

/// <summary>发送状态（平台适配层消费）。</summary>
public enum DeliveryState
{
    Pending,
    Sending,
    Delivered,
    RetryableFailure,
    PermanentFailure,
    Cancelled
}
