using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Core.Models;

public sealed class AgentInvocationRequest
{
    public required string RawInput { get; init; }

    public required ParsedCommand Parsed { get; init; }

    public required GameState State { get; init; }

    public required Character ActiveCharacter { get; init; }
}
