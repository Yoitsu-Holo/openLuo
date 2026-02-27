using openLuo.Modules.Executor.Core.Models;

namespace openLuo.Modules.Executor.Core.Interfaces;

public interface IStructuredOutputParser
{
    StructuredParseResult<T> Parse<T>(string raw);
}
