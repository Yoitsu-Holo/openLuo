using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.TODOList;

public sealed class TODOListExecutor : LlmStructuredExecutor<TODOListInput, TODOListOutput>
{
    public TODOListExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<TODOListInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, outputParser, logger)
    {
    }

    public override string Name => "todo_list";
}
