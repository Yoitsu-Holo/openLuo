using System.Text.Json;
using System.Text.Json.Nodes;
using openLuo.AgentContext.Core;
using openLuo.AgentContext.Core.Models;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;
using openLuo.Abstractions;
using openLuo.Modules.Llm.Core.Interfaces;
using openLuo.Modules.Llm.Core.Models;

namespace OpenLuo.Extensions.Companion;

public sealed class CompanionExtension : IAgentExtension
{
    private readonly ILlmClient _llm;
    private readonly string _dataDir;

    public CompanionExtension(ILlmClient llm, string? dataDir = null)
    {
        _llm = llm;
        _dataDir = dataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
    }

    public void Configure(ExtensionBuilder builder)
    {
        builder.AddCapability(CompanionDescriptors.Chat, new CompanionChatInvoker(_llm));
        builder.AddCapability(CompanionDescriptors.Plan, new CompanionPlanInvoker(_llm));
        builder.AddContextContributor(new CompanionIdentityContributor(_dataDir));
    }
}

internal static class CompanionDescriptors
{
    public static CapabilityDescriptor Chat => new()
    {
        CanonicalId = "chat", DisplayName = "Companion chat",
        Summary = "Produce the companion's persona reply for the user's message.",
        Usage = "Only for a final character response; pass the user's original message, never a drafted reply.",
        Kind = CapabilityKind.Builtin, ProviderId = "companion", SideEffect = SideEffectClass.Terminal,
        Completion = CompletionPolicy.Terminal, Visibility = OutputVisibility.Replyable, ParallelSafe = false,
        InputSchema = new { type = "object" }
    };

    public static CapabilityDescriptor Plan => new()
    {
        CanonicalId = "plan", DisplayName = "Companion plan",
        Summary = "Plan a response or sequence of actions for the active companion.", Usage = "Use when a request needs deliberate multi-step planning.",
        Kind = CapabilityKind.Builtin, ProviderId = "companion", SideEffect = SideEffectClass.Pure,
        Completion = CompletionPolicy.Continue, Visibility = OutputVisibility.Silent, ParallelSafe = false,
        InputSchema = new { type = "object" }
    };
}

internal sealed class CompanionChatInvoker : ICapabilityInvoker
{
    private readonly ILlmClient _llm;
    public CompanionChatInvoker(ILlmClient llm) => _llm = llm;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var prompt = ReadPrompt(call);
        // Terminal 角色（companion:chat）只负责说话：过滤 [Capabilities] 块，
        // 避免模型在无原生 tools 的调用里用文本模拟工具调用（裸 XML）。
        var systemMessages = context.SystemBlocks
            .Where(b => !b.Contains("[Capabilities]", StringComparison.Ordinal))
            .Select(b => new ChatMessage(ChatMessageRole.System, b))
            .ToList();
        if (systemMessages.Count == 0)
            systemMessages.Add(new ChatMessage(ChatMessageRole.System, "Respond as the active companion. Preserve the supplied persona and answer only the user's request."));
        systemMessages.Add(new ChatMessage(ChatMessageRole.System, "Respond as the active companion. Preserve the supplied persona and answer only the user's request."));
        var response = await _llm.CompleteAsync(
            [.. systemMessages, new ChatMessage(ChatMessageRole.User, prompt)],
            new LlmOptions { Temperature = 0.7f, MaxTokens = 1024 }, ct);
        var empty = string.IsNullOrWhiteSpace(response.Content);
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = !empty,
            Status = empty ? CapabilityStatus.Failed : CapabilityStatus.Ok,
            Text = response.Content,
            Outputs = empty ? [] : [new OutputItem
            {
                Id = Guid.NewGuid().ToString("N"), Kind = ReplyItemKind.Text, Payload = response.Content,
                SourceCapability = call.CanonicalId, Fingerprint = $"companion-chat:{response.Content.GetHashCode()}"
            }],
            Error = empty ? "empty companion response" : null
        };
    }

    private static string ReadPrompt(CapabilityCall call) =>
        call.Options.TryGetValue("message", out var message) ? message : string.Join(" ", call.Args);
}

internal sealed class CompanionPlanInvoker : ICapabilityInvoker
{
    private readonly ILlmClient _llm;
    public CompanionPlanInvoker(ILlmClient llm) => _llm = llm;

    public async Task<CapabilityResult> InvokeAsync(CapabilityCall call, CapabilityExecutionContext context, CancellationToken ct = default)
    {
        var prompt = call.Options.TryGetValue("goal", out var goal) ? goal : string.Join(" ", call.Args);
        var response = await _llm.CompleteAsync(
            [new ChatMessage(ChatMessageRole.System, "Create a concise action plan. Return plain text steps."), new ChatMessage(ChatMessageRole.User, prompt)],
            new LlmOptions { Temperature = 0.3f, MaxTokens = 1024 }, ct);
        return new CapabilityResult
        {
            InvocationId = call.InvocationId, Success = !string.IsNullOrWhiteSpace(response.Content),
            Status = string.IsNullOrWhiteSpace(response.Content) ? CapabilityStatus.Failed : CapabilityStatus.Ok,
            Text = response.Content, Error = string.IsNullOrWhiteSpace(response.Content) ? "empty plan" : null
        };
    }
}

public sealed class CompanionIdentityContributor : IContextContributor
{
    private readonly string _dataDir;
    public CompanionIdentityContributor(string dataDir) => _dataDir = dataDir;
    public string Id => "companion:identity";

    public Task<ContextContributionResult> ContributeAsync(ContextBuildRequest request, CancellationToken ct = default)
    {
        try
        {
            var archetype = LoadArchetype(request.SubjectId);
            if (archetype is null)
                return Task.FromResult(new ContextContributionResult
                {
                    State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = "active companion definition not found", Retryable = false }
                });

            var identity = $"Active companion: {archetype.DisplayName}. {archetype.Description}\nPersona: {archetype.Prompt}";
            return Task.FromResult(new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Ok },
                Contributions = [new ContextContribution
                {
                    Id = "companion-identity", ContributorId = Id, Region = ContextRegion.Identity,
                    Content = identity, Priority = 100, TokenEstimate = Math.Max(1, identity.Length / 4)
                }]
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ContextContributionResult
            {
                State = new ContextSourceState { SourceId = Id, Status = ContextSourceStatus.Unavailable, Reason = ex.Message, Retryable = true }
            });
        }
    }

    private Archetype? LoadArchetype(string subjectId)
    {
        var dir = Path.Combine(_dataDir, "archetypes");
        if (!Directory.Exists(dir)) return null;
        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonc"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            if (root is null || !string.Equals(root["id"]?.GetValue<string>(), subjectId, StringComparison.OrdinalIgnoreCase)) continue;
            return new Archetype
            {
                DisplayName = root["displayName"]?.GetValue<string>() ?? root["name"]?.GetValue<string>() ?? subjectId,
                Description = root["description"]?.GetValue<string>() ?? string.Empty,
                Prompt = root["prompt"]?.GetValue<string>() ?? root["basePrompt"]?.GetValue<string>() ?? string.Empty
            };
        }
        return null;
    }

    private sealed class Archetype
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Prompt { get; init; } = string.Empty;
    }
}
