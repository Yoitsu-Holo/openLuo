namespace openLuo.Protocol;

/// <summary>
/// HTTP 控制面的**路径常量与 URL 构造**。REST 约定：路径只用**资源名词**（复数集合 + `{id}` 子资源），
/// 动作由 HTTP 方法/子资源表达，**路径里不出现动词**。
/// </summary>
/// <remarks>
/// 服务端用 `*Template` 常量注册路由（`app.MapGet(HttpPaths.Health, …)`）；
/// 客户端用构造方法拼具体 URL（`HttpPaths.Asset(id)`）——两侧共用同一份定义，禁止手写路径字面量。
/// 子资源视图（`/v1/logs/archive`、`/v1/logs/stats`）仍属资源名词，符合本约定。
/// </remarks>
public static class HttpPaths
{
    public const string Health = "/v1/health";
    public const string Version = "/v1/version";

    /// <summary>`POST` 签发令牌（创建 token 资源；替代早期的 `POST /v1/auth/token` 动作式路径）。</summary>
    public const string Tokens = "/v1/tokens";

    public const string Sessions = "/v1/sessions";
    public const string SessionTemplate = "/v1/sessions/{id}";
    public const string SessionContextTemplate = "/v1/sessions/{id}/context";
    public const string SessionStateTemplate = "/v1/sessions/{id}/state";

    /// <summary>会话范围内上传资产（父资源形态，替代早期的 `POST /v1/assets?sessionId=`）。</summary>
    public const string SessionAssetsTemplate = "/v1/sessions/{id}/assets";

    public const string Assets = "/v1/assets";
    public const string AssetTemplate = "/v1/assets/{id}";

    public const string Agents = "/v1/agents";
    public const string Capabilities = "/v1/capabilities";

    public const string Config = "/v1/config";
    public const string ConfigNamespaceTemplate = "/v1/config/{ns}";

    public const string Jobs = "/v1/jobs";
    public const string JobTemplate = "/v1/jobs/{id}";

    public const string Schedules = "/v1/schedules";
    public const string ScheduleTemplate = "/v1/schedules/{id}";

    public const string Logs = "/v1/logs";
    public const string LogsArchive = "/v1/logs/archive";
    public const string LogsStats = "/v1/logs/stats";

    public const string Metrics = "/v1/metrics";
    public const string Traces = "/v1/traces";
    public const string TraceTemplate = "/v1/traces/{turnId}";

    // ── 具体 URL 构造（客户端用；路径段做转义）──

    public static string Session(string id) => $"{Sessions}/{Encode(id)}";
    public static string SessionContext(string id) => $"{Session(id)}/context";
    public static string SessionState(string id) => $"{Session(id)}/state";
    public static string SessionAssets(string id) => $"{Session(id)}/assets";
    public static string Asset(string id) => $"{Assets}/{Encode(id)}";
    public static string ConfigNamespace(string ns) => $"{Config}/{Encode(ns)}";
    public static string Job(string id) => $"{Jobs}/{Encode(id)}";
    public static string Schedule(string id) => $"{Schedules}/{Encode(id)}";
    public static string Trace(string turnId) => $"/v1/traces/{Encode(turnId)}";

    private static string Encode(string segment) => Uri.EscapeDataString(segment);
}

/// <summary>HTTP 查询参数名（REST：查询串只承载**过滤 / 分页 / 视图选项**，不承载资源身份）。</summary>
public static class HttpQueryKeys
{
    public const string From = "from";
    public const string To = "to";
    public const string Level = "level";
    public const string Module = "module";
    public const string Category = "category";
    public const string Keyword = "keyword";
    public const string SessionId = "sessionId";
    public const string TurnId = "turnId";
    public const string Limit = "limit";
    public const string BeforeId = "beforeId";
    public const string Bucket = "bucket";

    /// <summary>归档视图的日期过滤（`yyyyMMdd` 或 `*`）。</summary>
    public const string Date = "date";

    /// <summary>配置写入是否落盘（`true`/`false`）。</summary>
    public const string Persist = "persist";
}

/// <summary>
/// HTTP 响应的 Envelope `type` 取值（与 WS 的 `EventTypes` 分开：控制面响应以**资源名**标识）。
/// </summary>
public static class HttpResponseTypes
{
    public const string Health = "health";
    public const string Version = "version";

    /// <summary>`POST /v1/tokens`。</summary>
    public const string Token = "token";

    public const string Sessions = "sessions";
    public const string Context = "context";
    public const string State = "state";

    public const string Agents = "agents";
    public const string Capabilities = "capabilities";

    public const string Config = "config";
    public const string Job = "job";
    public const string Schedule = "schedule";
    public const string Schedules = "schedules";
    public const string Logs = "logs";
    public const string Metrics = "metrics";
    public const string Trace = "trace";
    public const string Asset = "asset";
}

/// <summary>HTTP 面的通用校验规则（服务端入口治理与客户端构造共用）。</summary>
public static class HttpRules
{
    /// <summary>
    /// 资源 id 白名单：字母、数字、`-`、`_`。
    /// 服务端中间件用它挡掉路径穿越与分隔符注入（hub-protocol §4.9）；客户端构造 URL 时同样受此约束。
    /// </summary>
    public const string ResourceIdPattern = "[A-Za-z0-9_-]+";

    /// <summary>
    /// 文件名的**标准载体**：`Content-Disposition: attachment; filename="…"`。
    /// 早期用自定义头 `X-File-Name` + `?fileName=`，已废弃（REST：元数据随标准头走，不塞查询串）。
    /// </summary>
    public const string FileNameHeader = "Content-Disposition";

    /// <summary>`Content-Disposition` 中承载文件名的参数名（`filename="…"`）。</summary>
    public const string FileNameParameter = "filename";

    /// <summary>上传/下载文件名所在处置类型（`Content-Disposition: attachment`）。</summary>
    public const string ContentDispositionAttachment = "attachment";

    /// <summary>Envelope 的 JSON 媒体类型（手工写响应用；`Results.Json` 由框架负责）。</summary>
    public const string JsonContentType = "application/json; charset=utf-8";

    /// <summary>鉴权方案（`Authorization: Bearer &lt;token&gt;`）。</summary>
    public const string AuthorizationScheme = "Bearer";

    /// <summary>`Authorization` 头里的方案前缀（含尾随空格，供解析用）。</summary>
    public const string AuthorizationPrefix = AuthorizationScheme + " ";

    /// <summary>
    /// 「资源 id 出现在路径里」的集合（供服务端中间件拼校验正则，见 hub-protocol §4.9）：
    /// 只校验**按 id 直取**的资源（会话 / 资产 / 轨迹），避免误伤 agent 等自由命名资源。
    /// </summary>
    public const string IdAddressedCollections = "sessions|assets|traces";
}
