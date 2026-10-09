namespace openLuo.Protocol;

/// <summary>协议常量（版本、路径、默认端口）。</summary>
public static class ProtocolInfo
{
    /// <summary>协议 major 版本；不匹配即拒绝连接/请求。</summary>
    public const int MajorVersion = 1;

    /// <summary>HTTP 控制面前缀。</summary>
    public const string HttpBasePath = "/v1";

    /// <summary>WebSocket 数据面路径。</summary>
    public const string StreamPath = "/v1/stream";

    /// <summary>默认监听端口（避让 LLBot 的 3001/3010）。</summary>
    public const int DefaultPort = 8674;

    /// <summary>默认心跳间隔（秒）。</summary>
    public const int DefaultHeartbeatSec = 20;
}

/// <summary>客户端 → 服务端命令类型。</summary>
public static class MessageTypes
{
    public const string Hello = "hello";
    public const string SessionOpen = "session.open";
    public const string SessionSubscribe = "session.subscribe";
    public const string SessionUnsubscribe = "session.unsubscribe";
    public const string SessionClose = "session.close";
    public const string TurnSubmit = "turn.submit";
    public const string TurnCancel = "turn.cancel";
    public const string MessageAppend = "message.append";
    public const string OutputAck = "output.ack";
    public const string OutputFail = "output.fail";
    public const string ConfirmResponse = "confirm.response";
    public const string ConfigGet = "config.get";
    public const string ConfigSet = "config.set";
    public const string ConfigDel = "config.del";
    public const string Ping = "ping";
}

/// <summary>服务端 → 客户端事件类型。</summary>
public static class EventTypes
{
    public const string Welcome = "welcome";
    public const string SessionOpened = "session.opened";
    public const string SessionClosed = "session.closed";
    public const string TurnAccepted = "turn.accepted";
    public const string Decision = "decision";
    public const string ToolCall = "tool.call";
    public const string ToolResult = "tool.result";
    public const string Output = "output";
    public const string TurnFinal = "turn.final";
    public const string ContextUpdated = "context.updated";
    public const string StateUpdated = "state.updated";
    public const string ConfigUpdated = "config.updated";
    public const string ConfirmRequest = "confirm.request";
    public const string Error = "error";
    public const string Pong = "pong";
}

/// <summary>
/// 错误码（int，分段）：<c>1000</c>=成功（<c>11xx</c>=配置）；<c>2xxx</c>=协议；<c>3xxx</c>=鉴权；
/// <c>4xxx</c>=会话；<c>5xxx</c>=回合；<c>6xxx</c>=能力；<c>7xxx</c>=资产；
/// <c>8xxx</c>=限流；<c>9xxx</c>=服务端。码值入 wire；<see cref="NameOf"/> 提供稳定标识
/// （日志/文档/调试用，不入 wire）。
/// </summary>
public static class ErrorCodes
{
    public const int Success = 1000;
    public const int Unknown = 1001;

    public const int ConfigNamespaceNotFound = 1101;
    public const int ConfigInvalidValue = 1102;
    public const int ConfigReadOnly = 1103;
    public const int ConfigPersistFailed = 1104;

    public const int ProtocolVersionMismatch = 2001;
    public const int ProtocolBadEnvelope = 2002;
    public const int ProtocolUnknownType = 2003;

    public const int AuthUnauthorized = 3001;
    public const int AuthForbidden = 3002;

    public const int SessionNotFound = 4001;
    public const int SessionLimitExceeded = 4002;

    public const int TurnBusy = 5001;
    public const int TurnCancelled = 5002;
    public const int TurnBudgetExceeded = 5003;

    public const int CapabilityConfirmationRequired = 6001;
    public const int CapabilityFailed = 6002;

    public const int AssetNotFound = 7001;

    public const int RateLimited = 8001;

    public const int ServerInternal = 9001;

    public static bool IsSuccess(int code) => code == Success;

    /// <summary>码值 → 稳定标识（如 <c>protocol.version_mismatch</c>）。</summary>
    public static string NameOf(int code) => code switch
    {
        Success => "success",
        Unknown => "unknown",
        ConfigNamespaceNotFound => "config.namespace_not_found",
        ConfigInvalidValue => "config.invalid_value",
        ConfigReadOnly => "config.read_only",
        ConfigPersistFailed => "config.persist_failed",
        ProtocolVersionMismatch => "protocol.version_mismatch",
        ProtocolBadEnvelope => "protocol.bad_envelope",
        ProtocolUnknownType => "protocol.unknown_type",
        AuthUnauthorized => "auth.unauthorized",
        AuthForbidden => "auth.forbidden",
        SessionNotFound => "session.not_found",
        SessionLimitExceeded => "session.limit_exceeded",
        TurnBusy => "turn.busy",
        TurnCancelled => "turn.cancelled",
        TurnBudgetExceeded => "turn.budget_exceeded",
        CapabilityConfirmationRequired => "capability.confirmation_required",
        CapabilityFailed => "capability.failed",
        AssetNotFound => "asset.not_found",
        RateLimited => "rate.limited",
        ServerInternal => "server.internal",
        _ => "unknown",
    };
}

/// <summary>特性开关名（<c>hello</c>/<c>welcome</c> 协商）。</summary>
public static class Features
{
    public const string Confirm = "confirm";
    public const string Streaming = "streaming";
    public const string Assets = "assets";
    public const string MultiSession = "multi-session";

    /// <summary>配置读写（get/set/del）。</summary>
    public const string Config = "config";
}

/// <summary>配置来源层：有效值 = <c>default ⊕ file ⊕ runtime</c>（后者覆盖前者）。</summary>
public static class ConfigSources
{
    /// <summary>内置/示例默认值。</summary>
    public const string Default = "default";

    /// <summary>磁盘 <c>config/{ns}.jsonc</c>。</summary>
    public const string File = "file";

    /// <summary>运行时覆盖（进程内，重启丢失，除非 persist）。</summary>
    public const string Runtime = "runtime";
}

/// <summary>能力种类取值（对齐内核 <c>CapabilityKind</c>）。</summary>
public static class CapabilityKinds
{
    public const string Builtin = "builtin";
    public const string Mcp = "mcp";
    public const string Workflow = "workflow";
    public const string RemoteAgent = "remoteAgent";
}

/// <summary>副作用等级取值（对齐内核 <c>SideEffectClass</c>）。</summary>
public static class SideEffects
{
    public const string Pure = "pure";
    public const string ReadOnly = "readOnly";
    public const string External = "external";
    public const string Mutation = "mutation";
    public const string Delegation = "delegation";
}

/// <summary>风险等级取值（对齐内核 <c>RiskLevel</c>）。</summary>
public static class RiskLevels
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
}

/// <summary>回合终止原因取值（对齐内核 <c>TerminationReason</c>）。</summary>
public static class TerminationReasons
{
    public const string FinalReply = "finalReply";
    public const string MaxDecisionsReached = "maxDecisionsReached";
    public const string OverallTimeout = "overallTimeout";
    public const string TerminalCapability = "terminalCapability";
    public const string NoProgress = "noProgress";
    public const string Cancelled = "cancelled";
    public const string EmptyReply = "emptyReply";
}

/// <summary>投递状态取值（对齐内核 <c>DeliveryState</c>）。</summary>
public static class DeliveryStates
{
    public const string Pending = "pending";
    public const string Sending = "sending";
    public const string Delivered = "delivered";
    public const string RetryableFailure = "retryableFailure";
    public const string PermanentFailure = "permanentFailure";
    public const string Cancelled = "cancelled";
}
