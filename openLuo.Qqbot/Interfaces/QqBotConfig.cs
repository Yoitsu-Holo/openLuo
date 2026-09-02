namespace openLuo.Interfaces.QQbot;

public sealed class QqBotConfig
{
    public bool Enabled { get; set; }
    public string BaseAddress { get; set; } = "ws://localhost:3010/";
    public int RequestTimeoutSeconds { get; set; } = 120;
    public List<long> TargetGroupIds { get; set; } = [];
    public List<long> TargetFriendIds { get; set; } = [];
    public List<long> AdminUsers { get; set; } = [];
    public bool ReplyOnlyWhenMentioned { get; set; } = true;
    public bool LogMessages { get; set; } = true;
    /// <summary>中途消息（伴随工具调用的文本）是否即时推送（群聊即时反馈）。[热加载]</summary>
    public bool SendInterimMessages { get; set; } = true;
    public string DefaultAgentId { get; set; } = "companion";
    public string DefaultSubjectId { get; set; } = "builtin-rin";

    public QqBotConfig Clone() => new()
    {
        Enabled = Enabled, BaseAddress = BaseAddress, RequestTimeoutSeconds = RequestTimeoutSeconds,
        TargetGroupIds = [.. TargetGroupIds], TargetFriendIds = [.. TargetFriendIds], AdminUsers = [.. AdminUsers],
        ReplyOnlyWhenMentioned = ReplyOnlyWhenMentioned, LogMessages = LogMessages, SendInterimMessages = SendInterimMessages,
        DefaultAgentId = DefaultAgentId, DefaultSubjectId = DefaultSubjectId
    };
}
