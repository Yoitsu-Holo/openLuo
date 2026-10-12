namespace openLuo.Capabilities.Core;

/// <summary>
/// 世界状态的持久化端口（可选，由宿主/应用层实现）：状态事务提交后落盘、启动时回载，
/// 使每个 subject 的**版本与值跨重启保留**（协议文档 §5.4 说明）。内核不依赖具体存储实现。
/// </summary>
/// <remarks>
/// 值按 JSON 往返：回载后 <c>Values</c> 里的元素是 JSON 节点而非原 CLR 类型——Hub 投影到协议时本就序列化为 JSON，
/// 故线上表现一致（只影响内核内直接读取时的类型擦除）。
/// </remarks>
public interface IStatePersistence
{
    /// <summary>回载全部 subject 的状态（版本 + 扁平键值）。</summary>
    IReadOnlyList<(string SubjectId, long Version, IReadOnlyDictionary<string, object?> Values)> Load();

    /// <summary>持久化某 subject 的当前状态（提交成功后调用）。</summary>
    void Save(string subjectId, long Version, IReadOnlyDictionary<string, object?> Values);
}
