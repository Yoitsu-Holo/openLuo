using openLuo.Modules.Agent.Application;
using openLuo.Modules.AgentCapabilities.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface ICharacterPromptContextBuilder
{
    Task<CharacterPromptContext> BuildAsync(
        CharacterTurnRequest request,
        CharacterMemorySnapshot memory,
        AgentCapabilitySnapshot capabilitySnapshot,
        string currentStateSummary,
        CancellationToken ct = default);
}
