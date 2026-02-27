using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICharacterToolGateway
{
    Task<CharacterToolCallResult> ExecuteAsync(CharacterTurnContext context, CancellationToken ct = default);

    Task<CharacterToolCallResult> ExecuteAsync(
        CharacterTurnContext context,
        CharacterToolExecutionRequest executionRequest,
        CancellationToken ct = default);

    Task<CharacterToolCallResult> ExecuteCapabilityDirectlyAsync(
        CharacterTurnContext context,
        AgentCapabilityDescriptor capability,
        string[] args,
        Dictionary<string, string> options,
        CancellationToken ct = default);
}
