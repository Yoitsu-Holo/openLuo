namespace openLuo.Capabilities.Core.Models;

/// <summary>
/// 公共输出项类型（D3-D6）。
/// Text/Image/Audio/File 是跨平台有通用渲染直觉的内容；Card 是"结构化不透明载荷"——
/// 内核不解析 Payload 语义，只把它透传给平台；渲染语义由 Payload 结构与产出/消费两端
/// 的联合契约决定（如 QQ 将音乐分享卡渲染为 music 段，CLI 降级为文本）。见 D3 内容分类。
/// </summary>
public enum ReplyItemKind
{
    /// <summary>纯文本内容（Payload: string）。</summary>
    Text,
    /// <summary>图片内容（Payload: data URL 或资源引用）。</summary>
    Image,
    /// <summary>音频内容。</summary>
    Audio,
    /// <summary>文件内容。</summary>
    File,
    /// <summary>结构化不透明载荷：任意复杂度的结构化数据（如 {Platform, Id} 分享卡）。
    /// 管道只路由不解析；两端（产出能力与消费平台）按契约联合解析，未知结构必须降级。</summary>
    Card,
    /// <summary>通用资产内容。</summary>
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
