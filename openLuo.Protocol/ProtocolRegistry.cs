namespace openLuo.Protocol;

/// <summary>
/// 协议类型注册表：命令 / 事件的**闭合集合**，作为「同名常量已登记」的单一事实源。
/// 服务端据此判定未知类型（回 <see cref="ErrorCodes.ProtocolUnknownType"/>），
/// 测试据此防止「新增常量却漏登记」的漂移。
/// </summary>
public static class ProtocolRegistry
{
    private static readonly HashSet<string> CommandSet = new(StringComparer.Ordinal)
    {
        MessageTypes.Hello,
        MessageTypes.SessionOpen,
        MessageTypes.SessionSubscribe,
        MessageTypes.SessionUnsubscribe,
        MessageTypes.SessionClose,
        MessageTypes.SessionResume,
        MessageTypes.TurnSubmit,
        MessageTypes.TurnCancel,
        MessageTypes.MessageAppend,
        MessageTypes.OutputAck,
        MessageTypes.OutputFail,
        MessageTypes.ConfirmResponse,
        MessageTypes.PresenceSubscribe,
        MessageTypes.PresenceUnsubscribe,
        MessageTypes.DeviceReport,
        MessageTypes.AvatarCommand,
        MessageTypes.AuditSubscribe,
        MessageTypes.ConfigGet,
        MessageTypes.ConfigSet,
        MessageTypes.ConfigDel,
        MessageTypes.Ping,
    };

    private static readonly HashSet<string> EventSet = new(StringComparer.Ordinal)
    {
        EventTypes.Welcome,
        EventTypes.SessionOpened,
        EventTypes.SessionClosed,
        EventTypes.TurnAccepted,
        EventTypes.TurnStarted,
        EventTypes.Decision,
        EventTypes.ToolCall,
        EventTypes.ToolResult,
        EventTypes.Output,
        EventTypes.TurnFinal,
        EventTypes.ContextUpdated,
        EventTypes.StateUpdated,
        EventTypes.ConfigUpdated,
        EventTypes.ConfirmRequest,
        EventTypes.Error,
        EventTypes.Pong,
        EventTypes.Notification,
        EventTypes.PresenceUpdated,
        EventTypes.MemberJoined,
        EventTypes.MemberLeft,
        EventTypes.DeviceState,
        EventTypes.JobAccepted,
        EventTypes.JobProgress,
        EventTypes.JobCompleted,
        EventTypes.JobFailed,
        EventTypes.AvatarState,
        EventTypes.AvatarMotion,
        EventTypes.AvatarLipsync,
        EventTypes.AuditEvent,
    };

    public static IReadOnlyCollection<string> Commands => CommandSet;

    public static IReadOnlyCollection<string> Events => EventSet;

    public static bool IsCommand(string type) => CommandSet.Contains(type);

    public static bool IsEvent(string type) => EventSet.Contains(type);

    public static bool IsKnown(string type) => CommandSet.Contains(type) || EventSet.Contains(type);
}
