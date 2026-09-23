using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Providers;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;
using static openLuo.Infrastructure.Logging.Logger;

namespace openLuo.Modules.Llm.Infrastructure.Chat;

public static class LlmClientFactory
{
    public static ILlmClient Create(LlmRouteConfig route)
    {
        ILlmClient client = route.Provider switch
        {
            LlmProvider.Ollama => new OllamaLlmClient(route),
            LlmProvider.Qwen => new QwenLlmClient(route),
            LlmProvider.DeepSeek => new DeepSeekLlmClient(route),
            _ => new OpenAiCompatibleLlmClient(route)
        };

        Info("llm", $"chat route: route={LlmRouteSelector.RouteName(route)}, provider={route.Provider}, client={client.GetType().Name}");
        return client;
    }
}
