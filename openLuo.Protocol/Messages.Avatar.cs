using System.Text.Json.Nodes;

namespace openLuo.Protocol;

/// <summary>`avatar.state`：角色表现状态（表情 / 参数），供 Live2D/3D 客户端渲染。</summary>
public sealed record AvatarStateEvent
{
    public string AgentId { get; init; } = string.Empty;

    /// <summary>表情与参数集合（实现相关，如 <c>{"expr":"smile","params":{...}}</c>）。</summary>
    public JsonNode? State { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>`avatar.motion`：播放动作。</summary>
public sealed record AvatarMotionEvent
{
    public string AgentId { get; init; } = string.Empty;

    /// <summary>动作名 / 组名（实现相关）。</summary>
    public string Motion { get; init; } = string.Empty;

    /// <summary>优先级（高打断低）。</summary>
    public int Priority { get; init; }

    public bool Loop { get; init; }
}

/// <summary>口型帧（viseme 时间轴）。</summary>
public sealed record VisemeDto
{
    /// <summary>相对音频起点的偏移毫秒。</summary>
    public long TimeMs { get; init; }

    /// <summary>口型标识（实现相关，如 A/I/U/E/O 或模型参数名）。</summary>
    public string Viseme { get; init; } = string.Empty;

    /// <summary>权重 0..1。</summary>
    public double Weight { get; init; } = 1.0;
}

/// <summary>`avatar.lipsync`：音频 + 口型时间轴（驱动说话动画）。</summary>
public sealed record AvatarLipsyncEvent
{
    public string AgentId { get; init; } = string.Empty;

    /// <summary>驱动音频（走 assetRef，见 §9）。</summary>
    public AssetRefDto Audio { get; init; } = new();

    public IReadOnlyList<VisemeDto> Visemes { get; init; } = [];
    public long DurationMs { get; init; }
}

/// <summary>`avatar.command`：客户端请求角色表现（点击 / 触摸等互动触发）。</summary>
public sealed record AvatarCommand
{
    public string AgentId { get; init; } = string.Empty;
    public string? Motion { get; init; }
    public JsonNode? State { get; init; }
}
