using openLuo.Modules.Executor.Core.Models;

namespace openLuo.Modules.Executor.Core.Interfaces;

public interface IExecutorPromptBuilder<in TInput>
{
    ExecutorPrompt Build(TInput input);
}
