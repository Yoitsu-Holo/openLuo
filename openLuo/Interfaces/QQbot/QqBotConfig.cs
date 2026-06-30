namespace openLuo.Interfaces.QQbot;

public sealed class QqBotConfig
{
    public bool Enabled { get; set; } = false;
    public string BaseAddress { get; set; } = "ws://localhost:3010/";
    public List<long> TargetGroupIds { get; set; } = [];
    public List<long> TargetFriendIds { get; set; } = [];
    public List<long> AdminUsers { get; set; } = [];
    public bool ReplyOnlyWhenMentioned { get; set; } = true;
    public bool CommandPrefixPassthrough { get; set; } = true;
    public bool IncludeStateSummaryInReply { get; set; } = false;
    public bool AutoInitializeSession { get; set; } = true;
    public string DefaultArchetypeId { get; set; } = "builtin-rin";
    public string PlayerName { get; set; } = "群友";

    /// <summary>
    /// [热加载] true=QQ 消息中的图片会下载并作为多模态 ImageBlock 传给 LLM；
    /// false（默认）=仅在文本中用 "[图片]" 占位，不将图片数据传递给 LLM。优先级高于模型能力自动检测。
    /// </summary>
    public bool EnableImageToLlm { get; set; } = false;

    public QqBotConfig Clone() => new()
    {
        Enabled = Enabled,
        BaseAddress = BaseAddress,
        TargetGroupIds = [.. TargetGroupIds],
        TargetFriendIds = [.. TargetFriendIds],
        AdminUsers = [.. AdminUsers],
        ReplyOnlyWhenMentioned = ReplyOnlyWhenMentioned,
        CommandPrefixPassthrough = CommandPrefixPassthrough,
        IncludeStateSummaryInReply = IncludeStateSummaryInReply,
        AutoInitializeSession = AutoInitializeSession,
        DefaultArchetypeId = DefaultArchetypeId,
        PlayerName = PlayerName,
        EnableImageToLlm = EnableImageToLlm
    };
}
