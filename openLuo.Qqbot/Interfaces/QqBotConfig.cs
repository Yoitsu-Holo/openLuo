namespace openLuo.Interfaces.QQbot;

public sealed class QqBotConfig
{
    public bool Enabled { get; set; }
    /// <summary>LLBot(幸运莉莉娅) OneBot 11 正向 WebSocket 地址(在 LLBot 设置中开启 WS 正向并填端口)。</summary>
    public string BaseAddress { get; set; } = "ws://localhost:3001/";
    public int RequestTimeoutSeconds { get; set; } = 120;
    public List<long> TargetGroupIds { get; set; } = [];
    public List<long> TargetFriendIds { get; set; } = [];
    public List<long> AdminUsers { get; set; } = [];
    public bool ReplyOnlyWhenMentioned { get; set; } = true;
    public bool LogMessages { get; set; } = true;
    /// <summary>中途消息(伴随工具调用的文本)是否即时推送(群聊即时反馈)。[热加载]</summary>
    public bool SendInterimMessages { get; set; } = true;
    public string DefaultAgentId { get; set; } = "companion";
    public string DefaultSubjectId { get; set; } = "builtin-rin";
    /// <summary>OneBot access_token(LLBot 设置了 token 时必填;连接带 Authorization: Bearer 头)。</summary>
    public string? AccessToken { get; set; }

    public QqBotConfig Clone() => new()
    {
        Enabled = Enabled, BaseAddress = BaseAddress, RequestTimeoutSeconds = RequestTimeoutSeconds,
        TargetGroupIds = [.. TargetGroupIds], TargetFriendIds = [.. TargetFriendIds], AdminUsers = [.. AdminUsers],
        ReplyOnlyWhenMentioned = ReplyOnlyWhenMentioned, LogMessages = LogMessages, SendInterimMessages = SendInterimMessages,
        DefaultAgentId = DefaultAgentId, DefaultSubjectId = DefaultSubjectId,
        AccessToken = AccessToken
    };
}
