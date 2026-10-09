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
    public const string ConfirmRequest = "confirm.request";
    public const string Error = "error";
    public const string Pong = "pong";
}

/// <summary>错误码（<c>&lt;domain&gt;.&lt;reason&gt;</c>）。</summary>
public static class ErrorCodes
{
    public const string VersionMismatch = "protocol.version_mismatch";
    public const string BadEnvelope = "protocol.bad_envelope";
    public const string UnknownType = "protocol.unknown_type";

    public const string Unauthorized = "auth.unauthorized";
    public const string Forbidden = "auth.forbidden";

    public const string SessionNotFound = "session.not_found";
    public const string SessionLimitExceeded = "session.limit_exceeded";

    public const string TurnBusy = "turn.busy";
    public const string TurnCancelled = "turn.cancelled";
    public const string TurnBudgetExceeded = "turn.budget_exceeded";

    public const string ConfirmationRequired = "capability.confirmation_required";
    public const string CapabilityFailed = "capability.failed";

    public const string AssetNotFound = "asset.not_found";

    public const string RateLimited = "rate.limited";
    public const string Internal = "server.internal";
}

/// <summary>特性开关名（<c>hello</c>/<c>welcome</c> 协商）。</summary>
public static class Features
{
    public const string Confirm = "confirm";
    public const string Streaming = "streaming";
    public const string Assets = "assets";
    public const string MultiSession = "multi-session";
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
