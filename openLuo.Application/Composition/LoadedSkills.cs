using openLuo.Capabilities.Core;

namespace openLuo.Composition;

/// <summary>
/// 「本对话已加载了哪些技能」——**只存 id（引用），不存正文**（D30）。
/// <para>
/// 正文在构造每个请求时从技能库展开（见 <see cref="LoadedSkillsContributor"/>），于是：
/// ① 上下文里永远是当前版本（不存在"半个旧正文"）；
/// ② 正文不会被当成"对话内容"（不进对话历史、不参与对话预算，而是走系统块预算且可被裁剪）；
/// ③ 加载状态可随时卸载、可被上限淘汰。
/// </para>
/// <para>
/// 分区维度是**对话**（conversationId，缺省 sessionId）而不是连接：多端共享同一段对话时，加载状态也跟着共享。
/// 上限默认 3 且按最久未用淘汰——"加载过的技能永远占着上下文"才是真正需要维护的负担。
/// </para>
/// </summary>
public interface ILoadedSkillSet
{
    /// <summary>标记已加载（并刷新为最近使用）；返回刷新后的集合（最久未用在前）。</summary>
    IReadOnlyList<string> MarkLoaded(string conversationKey, string skillId);

    /// <summary>取当前集合（最久未用在前）；未知对话返回空表。</summary>
    IReadOnlyList<string> Get(string conversationKey);

    /// <summary>卸载一个技能；返回它此前是否处于已加载状态。</summary>
    bool Unload(string conversationKey, string skillId);

    /// <summary>单对话上限（超出时淘汰最久未用者）。</summary>
    int Capacity { get; }
}

/// <summary>把"当前上下文的身份"折算成已加载集的分区键（规则唯一定义处）。</summary>
public static class ConversationKeys
{
    /// <summary>优先用会话键（多端共享同一段对话的支点），为空则退回 sessionId。</summary>
    public static string For(string? conversationId, string sessionId) =>
        string.IsNullOrWhiteSpace(conversationId) ? sessionId : conversationId;

    /// <summary>能力执行上下文的口径（与上面同一规则，供 invoker 使用）。</summary>
    public static string For(CapabilityExecutionContext context) =>
        For(context.ConversationId, context.SessionId);
}

/// <summary>内存实现（进程生命周期；重启后模型按需重新加载——与"技能只在启动期装载"一致）。</summary>
public sealed class InMemoryLoadedSkillSet(int capacity = 3) : ILoadedSkillSet
{
    private readonly Dictionary<string, List<string>> _byConversation = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public int Capacity { get; } = Math.Max(1, capacity);

    public IReadOnlyList<string> MarkLoaded(string conversationKey, string skillId)
    {
        lock (_gate)
        {
            if (!_byConversation.TryGetValue(conversationKey, out var list))
            {
                list = [];
                _byConversation[conversationKey] = list;
            }

            // 已存在则提到末尾（最近使用）；否则追加
            list.RemoveAll(id => string.Equals(id, skillId, StringComparison.OrdinalIgnoreCase));
            list.Add(skillId);

            // 超上限：淘汰最久未用（列表头部）
            while (list.Count > Capacity)
                list.RemoveAt(0);

            return [.. list];
        }
    }

    public IReadOnlyList<string> Get(string conversationKey)
    {
        lock (_gate)
        {
            return _byConversation.TryGetValue(conversationKey, out var list) ? [.. list] : [];
        }
    }

    public bool Unload(string conversationKey, string skillId)
    {
        lock (_gate)
        {
            return _byConversation.TryGetValue(conversationKey, out var list)
                && list.RemoveAll(id => string.Equals(id, skillId, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }
}
