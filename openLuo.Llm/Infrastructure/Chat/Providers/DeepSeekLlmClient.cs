using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class DeepSeekLlmClient : OpenAiCompatibleLlmClient
{
    public DeepSeekLlmClient(LlmRouteConfig config) : base(config)
    {
    }

    protected override LlmOptions NormalizeOptions(LlmOptions options)
    {
        var normalized = base.NormalizeOptions(options);
        normalized.ExtraBody["thinking"] = new Dictionary<string, object?>
        {
            ["type"] = "disabled"
        };
        normalized.ExtraBody.Remove("reasoning_effort");
        return normalized;
    }
}
