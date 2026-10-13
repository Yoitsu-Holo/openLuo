namespace openLuo.AgentContext.Core;

/// <summary>
/// 上下文域（D45）。回答"这块内容属于谁"——改造前只有"会话"一根隔离轴，
/// 于是群聊里区分不了说话人、记忆也没法按人分区（见 docs/architecture/context-domains.md）。
/// </summary>
public enum ContextScope
{
    /// <summary>所有局部推理共用（键 = subjectId）：身份/persona、运行时规则、时间、能力清单。</summary>
    Global,

    /// <summary>一段对话（键 = conversationId）：场景/世界状态、对话历史、在场。</summary>
    Session,

    /// <summary>会话内的线程（键 = conversationId#threadId）；v1 只记录不切分存储。</summary>
    Thread,

    /// <summary>用户↔角色（键 = subjectId@userId，**跨会话**）：长期记忆的主要内容域。</summary>
    User,
}

/// <summary>
/// 域键：<see cref="ContextScope"/> + 该域的稳定标识。需要"按域存取"的地方（记忆、历史归因、在场）
/// 一律用它寻址，而不是各自拼字符串。
/// </summary>
public sealed record ContextDomainKey(ContextScope Scope, string Key)
{
    /// <summary>形如 <c>User:builtin-rin@10001</c>，用于日志/调试与存储键。</summary>
    public override string ToString() => $"{Scope}:{Key}";
}

/// <summary>域键构造（键形状的唯一定义处；改形状只改这里）。</summary>
public static class ContextDomainKeys
{
    /// <summary>用户维度的分隔符（`subjectId@userId`）。</summary>
    public const string UserSeparator = "@";

    /// <summary>线程维度的分隔符（`conversationId#threadId`）。</summary>
    public const string ThreadSeparator = "#";

    public static ContextDomainKey Global(string subjectId) => new(ContextScope.Global, subjectId);

    public static ContextDomainKey Session(string conversationId) => new(ContextScope.Session, conversationId);

    public static ContextDomainKey Thread(string conversationId, string threadId) =>
        new(ContextScope.Thread, conversationId + ThreadSeparator + threadId);

    /// <summary>用户↔角色（跨会话）：同一用户在不同群/不同端都是同一份关系记忆。</summary>
    public static ContextDomainKey User(string subjectId, string userId) =>
        new(ContextScope.User, subjectId + UserSeparator + userId);
}
