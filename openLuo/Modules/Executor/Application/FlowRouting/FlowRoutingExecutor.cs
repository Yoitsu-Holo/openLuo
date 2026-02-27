using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.FlowRouting;

public sealed class FlowRoutingExecutor : LlmStructuredExecutor<FlowRoutingInput, FlowRoutingOutput>
{
    public FlowRoutingExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<FlowRoutingInput> promptBuilder,
        IStructuredOutputParser outputParser,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, outputParser, logger)
    {
    }

    public override string Name => "flow_routing";
}
