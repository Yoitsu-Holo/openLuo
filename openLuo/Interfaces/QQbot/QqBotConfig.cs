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
        PlayerName = PlayerName
    };
}
