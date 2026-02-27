using openLuo.Modules.Memory.Core.Models;

namespace openLuo.Modules.Executor.Application.MemoryRecall;

public interface IMemoryRecallFormatter
{
    MemoryRecallFormattedResult Format(
        IReadOnlyList<MemoryRecord> records,
        MemoryRecallOptions options);
}
