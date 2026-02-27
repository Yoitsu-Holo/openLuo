using openLuo.Core.Models;
using openLuo.Modules.Commanding.Core.Models;
using openLuo.Modules.Agent.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IMultiCharacterOrchestrator
{
    bool CanHandle(ParsedCommand command);

    Task<CommandResult> ExecuteAsync(MultiCharacterCommandContext context, CancellationToken ct = default);

    IReadOnlyList<CommandDescriptor> GetRegisteredCommands();
}
