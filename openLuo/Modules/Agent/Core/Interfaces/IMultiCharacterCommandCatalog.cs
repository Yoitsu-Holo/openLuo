using openLuo.Modules.Commanding.Core.Models;

namespace openLuo.Modules.Agent.Core.Interfaces;

public interface IMultiCharacterCommandCatalog
{
    IReadOnlyList<CommandDescriptor> GetCommands();
}
