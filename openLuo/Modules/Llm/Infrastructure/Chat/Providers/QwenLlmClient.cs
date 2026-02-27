using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class QwenLlmClient : OpenAiCompatibleLlmClient
{
    public QwenLlmClient(LlmConfig config, IGameLogger logger) : base(config, logger)
    {
    }

    protected override bool ShouldUseCustomRequest(LlmOptions options) => true;

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
