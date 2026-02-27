using openLuo.Modules.Agent.Core.Interfaces;

namespace openLuo.Modules.Agent.Application;

public sealed class DefaultAgentMessageHandler : IAgentMessageHandler
{
    private readonly ICharacterTurnRequestBuilder _requestBuilder;
    private readonly ICharacterAgent _characterAgent;
    private readonly ICharacterTurnResultApplier _resultApplier;

    public DefaultAgentMessageHandler(
        ICharacterTurnRequestBuilder requestBuilder,
        ICharacterAgent characterAgent,
        ICharacterTurnResultApplier resultApplier)
    {
        _requestBuilder = requestBuilder;
        _characterAgent = characterAgent;
        _resultApplier = resultApplier;
    }

    public async Task<AgentMessage?> HandleAsync(AgentContext context, AgentMessage message, CancellationToken ct = default)
    {
        var request = await _requestBuilder.BuildAsync(context, message, ct);
        var result = await _characterAgent.RunTurnAsync(request, ct);
        return await _resultApplier.ApplyAsync(context, message, result, ct);
    }
}
