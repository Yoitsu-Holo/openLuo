using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.ToolUse;

public sealed class ToolUseExecutor : LlmStructuredExecutor<ToolUseInput, ToolUseOutput>
{
    public ToolUseExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<ToolUseInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, outputParser, logger)
    {
    }

    public override string Name => "tool_use";
}
