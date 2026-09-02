using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.Core;

/// <summary>
/// 会话级输出队列（D3-D6）。内存实现；平台适配层订阅消费，收到即可回复（D50）。
/// 同一会话/频道内按 Sequence 顺序发送：后一条等待前一条完成。
/// </summary>
public interface IOutputQueue
{
    /// <summary>入队一个输出项。Sequence 由队列单调分配。</summary>
    ValueTask<long> EnqueueAsync(OutputItem item, CancellationToken ct = default);

    /// <summary>按顺序读取待发送项。消费方负责在发送成功后 Ack。</summary>
    IAsyncEnumerable<OutputItem> ReadAsync(CancellationToken ct = default);

    /// <summary>标记发送成功，从待发送集合移除。</summary>
    Task AckAsync(long sequence, CancellationToken ct = default);

    /// <summary>标记发送失败。permanent=true 时放弃该条（以固定失败消息占位）；false 时保留可重试。</summary>
    Task FailAsync(long sequence, bool permanent, CancellationToken ct = default);
}
