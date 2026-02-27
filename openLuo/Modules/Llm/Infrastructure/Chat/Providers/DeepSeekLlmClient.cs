using openLuo.Core.Interfaces;
using openLuo.Modules.AppShell.Application;
using openLuo.Modules.Llm.Core.Models;
using openLuo.Modules.Llm.Infrastructure.Chat.Adapters;

namespace openLuo.Modules.Llm.Infrastructure.Chat.Providers;

public sealed class DeepSeekLlmClient : OpenAiCompatibleLlmClient
{
    public DeepSeekLlmClient(LlmConfig config, IGameLogger logger) : base(config, logger)
    {
    }

    protected override bool ShouldUseCustomRequest(LlmOptions options) => true;

    protected override LlmOptions NormalizeOptions(LlmOptions options)
    {
        var normalized = base.NormalizeOptions(options);

        normalized.ExtraBody["thinking"] = new Dictionary<string, object?>
        {
            ["type"] = "disabled"
        };

        normalized.ExtraBody.Remove("reasoning_effort");

        // 勾吧 DeepSeek 有问题，先屏蔽这个参数。
        if (options.JsonMode)
        {
            // normalized.ExtraBody["response_format"] = new Dictionary<string, object?>
            // {
            //     ["type"] = "json_object"
            // };
        }

        return normalized;
    }
}
