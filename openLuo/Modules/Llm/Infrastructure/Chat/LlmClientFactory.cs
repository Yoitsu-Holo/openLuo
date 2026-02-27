using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Infrastructure.Chat.Providers;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public static class LlmClientFactory
{
    public static ILlmClient Create(
        LlmConfig config,
        IGameLogger logger)
    {
        ILlmClient client = config.Provider switch
        {
            LlmProvider.Ollama => new OllamaLlmClient(config, logger),
            LlmProvider.Qwen => new QwenLlmClient(config, logger),
            LlmProvider.DeepSeek => new DeepSeekLlmClient(config, logger),
            _ => new OpenAiLlmClient(config, logger)
        };

        logger.Info("llm", $"chat route: provider={config.Provider}, client={client.GetType().Name}");
        return client;
    }
}
