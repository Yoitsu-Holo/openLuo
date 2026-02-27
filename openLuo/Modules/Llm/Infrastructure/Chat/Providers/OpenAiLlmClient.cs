using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class OpenAiLlmClient : OpenAiCompatibleLlmClient
{
    public OpenAiLlmClient(LlmConfig config, IGameLogger logger) : base(config, logger)
    {
    }
}
