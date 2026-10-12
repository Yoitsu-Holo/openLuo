namespace openLuo.Capabilities.Core;

/// <summary>世界状态的一个只读快照（按 subject 分区，与记忆/状态 owner 一致）。</summary>
public sealed record StateView(long Version, IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// 世界状态的**只读**端口：供 Hub 投影成协议 <c>GET /v1/sessions/{id}/state</c> 与 <c>state.updated</c> 事件。
/// 写入路径仍然是 <see cref="IStateTransaction"/>（版本 + 原子提交 + 冲突检测）。
/// </summary>
/// <remarks>
/// 按 subject 分区而不是按 session：会话只是寻址单位，记忆与状态的所有者一直是 subjectId
/// （后续的用户认证 / 公共·私有记忆划分也挂在这一层，见 docs/architecture/hub-protocol.md §6.4）。
/// 状态当前是**进程内**的（重启后版本与值重置）——持久化是独立决策，协议文档已注明。
/// </remarks>
public interface IStateReader
{
    /// <summary>读取该 subject 的当前状态；从未提交过变更时返回 null。</summary>
    StateView? TryGet(string subjectId);
}
