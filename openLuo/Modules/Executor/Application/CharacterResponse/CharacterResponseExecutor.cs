using openLuo.Modules.Executor.Core.Interfaces;
using openLuo.Modules.Executor.Infrastructure;
using openLuo.Core.Interfaces;
using openLuo.Modules.Llm.Core.Interfaces;

namespace openLuo.Modules.Executor.Application.CharacterResponse;

public sealed class CharacterResponseExecutor : LlmTextExecutor<CharacterResponseInput>
{
    public CharacterResponseExecutor(
        ILlmClient llmClient,
        IExecutorPromptBuilder<CharacterResponseInput> promptBuilder,
        IGameLogger? logger = null)
        : base(llmClient, promptBuilder, logger)
    {
    }

    public override string Name => "character_response";
}
