using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.StateUpdate;

public sealed class StateUpdateExecutor : LlmStructuredExecutor<StateUpdateInput, StateUpdateOutput>
{
    public StateUpdateExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<StateUpdateInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, outputParser, logger)
    {
    }

    public override string Name => "state_update";
}
