using A2A;
using openLuo.Capabilities.Core;
using openLuo.Capabilities.Core.Models;

namespace openLuo.Capabilities.A2A;

/// <summary>
/// Discovers an A2A agent card and exposes each advertised skill as a RemoteAgent capability.
/// A2A is intentionally an external capability source; the core does not depend on the protocol.
/// </summary>
public sealed class A2ACapabilitySource : ICapabilitySource, IAsyncDisposable
{
    private readonly A2AAgentConfig _config;
    private A2AClient? _client;
    private AgentCard? _card;

    public A2ACapabilitySource(A2AAgentConfig config)
    {
        _config = config;
    }

    public string ProviderId => $"a2a:{_config.Id}";
    public bool IsHealthy => _client is not null && _card is not null;
    public AgentCard? AgentCard => _card;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            var baseUri = new Uri(_config.Url, UriKind.Absolute);
            var resolver = new A2ACardResolver(baseUri, agentCardPath: _config.AgentCardUrl ?? "/.well-known/agent-card.json");
            _card = await resolver.GetAgentCardAsync(ct);
            var endpoint = _card.SupportedInterfaces.FirstOrDefault()?.Url;
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException($"A2A agent '{_config.Id}' advertises no supported interface.");

            _client = new A2AClient(new Uri(endpoint, UriKind.Absolute));
        }
        catch
        {
            _card = null;
            _client?.Dispose();
            _client = null;
        }
    }

    public IReadOnlyList<CapabilityDescriptor> ListCapabilities()
    {
        if (!IsHealthy || _card is null)
            return [];

        return _card.Skills.Select(skill => new CapabilityDescriptor
        {
            CanonicalId = $"a2a:{_config.Id}:{skill.Id}",
            Kind = CapabilityKind.RemoteAgent,
            ProviderId = ProviderId,
            DisplayName = skill.Name,
            Summary = skill.Description,
            Usage = skill.Description,
            SideEffect = SideEffectClass.Delegation,
            Completion = CompletionPolicy.Continue,
            Visibility = OutputVisibility.Silent,
            ParallelSafe = false,
            Idempotency = IdempotencyKind.Unknown,
            Version = _card.Version,
            InputSchema = new { type = "object", properties = new { message = new { type = "string" } }, required = new[] { "message" } }
        }).ToList();
    }

    public ICapabilityInvoker CreateInvoker()
    {
        if (_client is null)
            throw new InvalidOperationException($"A2A agent '{_config.Id}' is not connected.");
        return new A2ACapabilityInvoker(_client, _config);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;
        _card = null;
        return ValueTask.CompletedTask;
    }
}
