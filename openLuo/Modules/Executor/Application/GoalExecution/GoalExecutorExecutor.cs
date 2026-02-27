using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.GoalExecution;

public sealed class GoalExecutor : LlmStructuredExecutor<GoalExecutorInput, GoalExecutorOutput>
{
    public GoalExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<GoalExecutorInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, outputParser, logger)
    {
    }

    public override string Name => "goal_executor";
}
