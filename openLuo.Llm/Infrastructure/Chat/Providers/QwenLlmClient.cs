using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class QwenLlmClient : OpenAiCompatibleLlmClient
{
    public QwenLlmClient(LlmRouteConfig config) : base(config)
    {
    }


    protected override LlmOptions NormalizeOptions(LlmOptions options)
    {
        var normalized = base.NormalizeOptions(options);
        if (!normalized.ExtraBody.ContainsKey("chat_template_kwargs"))
        {
            var extra = new Dictionary<string, object?>
            {
                ["enable_thinking"] = false
            };
            normalized.ExtraBody["chat_template_kwargs"] = extra;
        }

        if (!normalized.ExtraBody.ContainsKey("enable_thinking"))
            normalized.ExtraBody["enable_thinking"] = false;

        return normalized;
    }
}
